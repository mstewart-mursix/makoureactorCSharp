using System.ComponentModel;
using System.IO;

using MakouReactor.Core.IO;
using MakouReactor.Core.Services;

namespace MakouReactor.UI.WPF.Fields;

public static class ShellFieldListService
{
    public static IReadOnlyList<ShellFieldListItem> BuildItems(
        IEnumerable<FieldArchiveEntry> fields,
        string? search,
        string? sortProperty,
        ListSortDirection sortDirection,
        ISet<string> unavailableFields) =>
        FieldListService.FilterAndSort(fields, search, sortProperty, sortDirection)
            .Select(field => new ShellFieldListItem(field, FieldStatus(field, unavailableFields)))
            .ToArray();

    public static string? LastSelectedFieldForArchive(
        IDictionary<string, string> lastSelectedFields,
        string archivePath)
    {
        var fullPath = Path.GetFullPath(archivePath);
        return lastSelectedFields.TryGetValue(fullPath, out var fieldName)
            ? fieldName
            : null;
    }

    public static bool RememberSelectedField(
        IDictionary<string, string> lastSelectedFields,
        string? archivePath,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
            return false;

        var fullPath = Path.GetFullPath(archivePath);
        if (lastSelectedFields.TryGetValue(fullPath, out var current) &&
            current.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        lastSelectedFields[fullPath] = fieldName;
        return true;
    }

    public static string FieldStatus(FieldArchiveEntry field, ISet<string> unavailableFields)
    {
        if (unavailableFields.Contains(field.Name))
            return "Unavailable";

        return field.Size == 0 ? "Empty" : "Ready";
    }
}

public sealed record ShellFieldListItem(FieldArchiveEntry Entry, string Status)
{
    public int Id => Entry.Id;
    public string Name => Entry.Name;
    public int Size => Entry.Size;
}
