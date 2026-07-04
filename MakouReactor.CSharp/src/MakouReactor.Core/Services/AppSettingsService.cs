using System.Text.Json;

namespace MakouReactor.Core.Services;

public interface IAppSettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}

public interface IRecentFilesService
{
    IReadOnlyList<string> RecentFiles { get; }
    void Add(string path);
    void Remove(string path);
    void Clear();
}

public sealed class JsonAppSettingsService : IAppSettingsService
{
    private const string LegacyLlmConfigFileName = "llm.config.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string? _appDirectory;
    private readonly string _settingsPath;

    public JsonAppSettingsService(string? settingsPath = null, string? appDirectory = null)
    {
        _appDirectory = appDirectory;
        _settingsPath = settingsPath ?? DefaultSettingsPath(appDirectory);
    }

    public AppSettings Load()
    {
        var settings = LoadSettings();
        MigrateLegacyLlmConfig(settings);
        return settings;
    }

    private AppSettings LoadSettings()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            using var stream = File.OpenRead(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(stream, SerializerOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    private void MigrateLegacyLlmConfig(AppSettings settings)
    {
        if (!HasDefaultLlmSettings(settings.Llm))
            return;

        var legacyPath = FindLegacyLlmConfigPath();
        if (legacyPath is null)
            return;

        try
        {
            using var stream = File.OpenRead(legacyPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var migrated = settings.Llm;

            migrated.Backend = ReadString(root, "backend", migrated.Backend);
            migrated.CodexExecutable = ReadString(root, "codex_executable", migrated.CodexExecutable);
            migrated.CodexModel = ReadString(root, "codex_model", migrated.CodexModel);
            migrated.TimeoutMs = ReadInt32(root, "timeout_ms", migrated.TimeoutMs);
            migrated.Endpoint = NormalizeLegacyEndpoint(ReadString(root, "endpoint", migrated.Endpoint));
            migrated.ApiKey = ReadString(root, "api_key", migrated.ApiKey);
            migrated.Model = ReadString(root, "model", migrated.Model);
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string? FindLegacyLlmConfigPath()
    {
        var candidates = new List<string>();
        var settingsDirectory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(settingsDirectory))
            candidates.Add(Path.Combine(settingsDirectory, LegacyLlmConfigFileName));

        if (!string.IsNullOrWhiteSpace(_appDirectory))
            candidates.Add(Path.Combine(_appDirectory, LegacyLlmConfigFileName));

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private static bool HasDefaultLlmSettings(LlmAppSettings settings)
    {
        var defaults = new LlmAppSettings();
        return string.Equals(settings.Backend, defaults.Backend, StringComparison.Ordinal)
            && string.Equals(settings.CodexExecutable, defaults.CodexExecutable, StringComparison.Ordinal)
            && string.Equals(settings.CodexModel, defaults.CodexModel, StringComparison.Ordinal)
            && settings.TimeoutMs == defaults.TimeoutMs
            && settings.RepairAttempts == defaults.RepairAttempts
            && string.Equals(settings.Endpoint, defaults.Endpoint, StringComparison.Ordinal)
            && string.Equals(settings.ApiKey, defaults.ApiKey, StringComparison.Ordinal)
            && string.Equals(settings.Model, defaults.Model, StringComparison.Ordinal);
    }

    private static string ReadString(JsonElement root, string propertyName, string fallback)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? fallback
            : fallback;
    }

    private static int ReadInt32(JsonElement root, string propertyName, int fallback)
    {
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetInt32()
            : fallback;
    }

    private static string NormalizeLegacyEndpoint(string endpoint)
    {
        return endpoint.EndsWith("/v1", StringComparison.Ordinal)
            ? string.Concat(endpoint, "/chat/completions")
            : endpoint;
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = string.Concat(_settingsPath, ".tmp");
        using (var stream = File.Create(tempPath))
            JsonSerializer.Serialize(stream, settings, SerializerOptions);

        if (File.Exists(_settingsPath))
            File.Replace(tempPath, _settingsPath, null);
        else
            File.Move(tempPath, _settingsPath);
    }

    public static string DefaultSettingsPath(string? appDirectory = null)
    {
        var localPath = Path.Combine(appDirectory ?? AppContext.BaseDirectory, "settings.json");
        if (File.Exists(localPath))
            return localPath;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MakouReactor",
            "settings.json");
    }
}

public sealed class RecentFilesService : IRecentFilesService
{
    public const int DefaultMaxItems = 10;

    private readonly AppSettings _settings;
    private readonly IAppSettingsService _settingsService;
    private readonly int _maxItems;

    public RecentFilesService(AppSettings settings, IAppSettingsService settingsService, int maxItems = DefaultMaxItems)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settingsService);
        if (maxItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxItems), "Recent file capacity must be positive.");

        _settings = settings;
        _settingsService = settingsService;
        _maxItems = maxItems;
        Normalize();
    }

    public IReadOnlyList<string> RecentFiles => _settings.RecentFiles;

    public void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var fullPath = Path.GetFullPath(path);
        _settings.RecentFiles.RemoveAll(item => item.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        _settings.RecentFiles.Insert(0, fullPath);
        Trim();
        _settingsService.Save(_settings);
    }

    public void Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var fullPath = Path.GetFullPath(path);
        _settings.RecentFiles.RemoveAll(item => item.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        _settingsService.Save(_settings);
    }

    public void Clear()
    {
        _settings.RecentFiles.Clear();
        _settingsService.Save(_settings);
    }

    private void Normalize()
    {
        var normalized = _settings.RecentFiles
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(_maxItems)
            .ToArray();

        _settings.RecentFiles.Clear();
        _settings.RecentFiles.AddRange(normalized);
    }

    private void Trim()
    {
        if (_settings.RecentFiles.Count > _maxItems)
            _settings.RecentFiles.RemoveRange(_maxItems, _settings.RecentFiles.Count - _maxItems);
    }
}
