using System.ComponentModel;

using MakouReactor.Core.IO;

namespace MakouReactor.Core.Services;

public static class FieldListService
{
    public static IReadOnlyList<FieldArchiveEntry> FilterAndSort(
        IEnumerable<FieldArchiveEntry> fields,
        string? search,
        string? sortProperty,
        ListSortDirection sortDirection)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var query = search?.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? fields
            : fields.Where(field =>
                field.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                field.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));

        var sorted = (sortProperty ?? string.Empty).ToUpperInvariant() switch
        {
            "ID" => Sort(filtered, field => field.Id, sortDirection),
            "SIZE" => Sort(filtered, field => field.Size, sortDirection),
            "NAME" => Sort(filtered, field => field.Name, sortDirection),
            _ => filtered,
        };

        return sorted.ToArray();
    }

    private static IEnumerable<FieldArchiveEntry> Sort<TKey>(
        IEnumerable<FieldArchiveEntry> fields,
        Func<FieldArchiveEntry, TKey> selector,
        ListSortDirection direction) =>
        direction == ListSortDirection.Descending
            ? fields.OrderByDescending(selector)
            : fields.OrderBy(selector);
}
