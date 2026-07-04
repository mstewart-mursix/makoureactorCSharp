using CommunityToolkit.Mvvm.ComponentModel;

using MakouReactor.Core.Models;
using MakouReactor.Core.Services;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class VariableManagerViewModel : ObservableObject
{
    private readonly IReadOnlyList<VariableReference> _allReferences;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<VariableReference> _references;

    [ObservableProperty]
    private VariableReference? _selectedReference;

    public VariableManagerViewModel(string fieldName, Section1File section)
    {
        FieldName = fieldName;
        _allReferences = new VariableReferenceScanner().Scan(section);
        _references = _allReferences;
        SelectedReference = References.FirstOrDefault();
    }

    public string FieldName { get; }

    public bool HasSelectedReference => SelectedReference != null;

    public string StatusText =>
        $"{References.Count} of {_allReferences.Count} variable reference" +
        $"{(_allReferences.Count == 1 ? string.Empty : "s")} shown.";

    partial void OnQueryChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedReferenceChanged(VariableReference? value)
    {
        OnPropertyChanged(nameof(HasSelectedReference));
    }

    public void ClearFilter()
    {
        Query = string.Empty;
    }

    private void ApplyFilter()
    {
        var query = Query.Trim();
        References = string.IsNullOrWhiteSpace(query)
            ? _allReferences
            : _allReferences
                .Where(reference =>
                    Contains(reference.BankHex, query) ||
                    Contains(reference.AddressHex, query) ||
                    Contains(reference.Size, query) ||
                    Contains(reference.OpcodeName, query) ||
                    Contains(reference.GroupName, query) ||
                    Contains(reference.Location, query))
                .ToArray();
        SelectedReference = References.FirstOrDefault();
        OnPropertyChanged(nameof(StatusText));
    }

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
