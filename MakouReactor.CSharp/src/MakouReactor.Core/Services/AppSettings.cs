namespace MakouReactor.Core.Services;

public sealed class AppSettings
{
    public List<string> RecentFiles { get; set; } = [];
    public ShellWindowSettings Window { get; set; } = new();
    public bool JapaneseText { get; set; }
    public string Language { get; set; } = "en";
    public string? Ff7ExecutablePath { get; set; }
    public string? Ff7DataPath { get; set; }
    public Dictionary<string, string> LastSelectedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public LlmAppSettings Llm { get; set; } = new();
}

public sealed class LlmAppSettings
{
    public string Backend { get; set; } = "codex";
    public string CodexExecutable { get; set; } = "codex";
    public string CodexModel { get; set; } = string.Empty;
    public int TimeoutMs { get; set; }
    public int RepairAttempts { get; set; } = 1;
    public string Endpoint { get; set; } = "http://localhost:1234/v1/chat/completions";
    public string ApiKey { get; set; } = "lm-studio";
    public string Model { get; set; } = "qwen/qwen3-coder-30b";
}

public sealed class ShellWindowSettings
{
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 900;
    public double Left { get; set; } = -1;
    public double Top { get; set; } = -1;
    public double LeftPanelWidth { get; set; } = 220;
    public double PreviewPanelHeight { get; set; } = 250;
    public bool FieldListVisible { get; set; } = true;
    public bool PreviewVisible { get; set; } = true;
    public string PreviewMode { get; set; } = "Background";
    public int SelectedMainTab { get; set; }
}
