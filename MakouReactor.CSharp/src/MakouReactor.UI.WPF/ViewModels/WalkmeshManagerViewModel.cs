using CommunityToolkit.Mvvm.ComponentModel;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.ViewModels;

public sealed partial class WalkmeshManagerViewModel : ObservableObject
{
    private readonly FieldPC _field;
    private readonly IdFile _walkmesh;

    [ObservableProperty]
    private IReadOnlyList<WalkmeshTriangleViewModel> _triangles;

    [ObservableProperty]
    private IReadOnlyList<WalkmeshPreviewTriangleViewModel> _previewTriangles;

    [ObservableProperty]
    private WalkmeshTriangleViewModel? _selectedTriangle;

    [ObservableProperty]
    private bool _isModified;

    public WalkmeshManagerViewModel(string fieldName, FieldPC field)
    {
        if (field.Walkmesh == null)
            throw new ArgumentException("Field does not contain parsed walkmesh data.", nameof(field));

        _field = field;
        _walkmesh = field.Walkmesh;
        FieldName = fieldName;
        _triangles = BuildRows();
        _previewTriangles = BuildPreviewRows();
        SelectedTriangle = Triangles.FirstOrDefault();
    }

    public string FieldName { get; }

    public string StatusText =>
        $"{Triangles.Count} triangle{(Triangles.Count == 1 ? string.Empty : "s")} loaded" +
        $"{(IsModified ? "; unapplied walkmesh changes" : string.Empty)}.";

    public bool CanRemoveTriangle => SelectedTriangle != null;

    partial void OnSelectedTriangleChanged(WalkmeshTriangleViewModel? value)
    {
        OnPropertyChanged(nameof(CanRemoveTriangle));
        PreviewTriangles = BuildPreviewRows();
    }

    partial void OnIsModifiedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    public void AddTriangle()
    {
        var index = SelectedTriangle?.Id + 1 ?? Triangles.Count;
        _walkmesh.InsertTriangle(index, new Triangle(
            new VertexSR(0, 0, 0, 0),
            new VertexSR(0, 0, 0, 0),
            new VertexSR(0, 0, 0, 0)));
        RefreshRows(index);
        IsModified = true;
    }

    public void RemoveSelectedTriangle()
    {
        if (SelectedTriangle == null)
            return;

        var index = SelectedTriangle.Id;
        _walkmesh.RemoveTriangle(index);
        RefreshRows(Math.Min(index, Triangles.Count - 1));
        IsModified = true;
    }

    public void ApplyChanges()
    {
        _field.ApplyWalkmeshChanges();
        IsModified = false;
    }

    private void RefreshRows(int selectedIndex)
    {
        Triangles = BuildRows();
        SelectedTriangle = selectedIndex >= 0 && selectedIndex < Triangles.Count
            ? Triangles[selectedIndex]
            : Triangles.FirstOrDefault();
        PreviewTriangles = BuildPreviewRows();
        OnPropertyChanged(nameof(StatusText));
    }

    private IReadOnlyList<WalkmeshTriangleViewModel> BuildRows() =>
        _walkmesh.Triangles
            .Select((triangle, index) => new WalkmeshTriangleViewModel(
                index,
                FormatVertex(triangle.Vertices[0]),
                FormatVertex(triangle.Vertices[1]),
                FormatVertex(triangle.Vertices[2]),
                FormatAccess(_walkmesh.AccessPoint(index))))
            .ToArray();

    private IReadOnlyList<WalkmeshPreviewTriangleViewModel> BuildPreviewRows()
    {
        if (_walkmesh.Triangles.Count == 0)
            return [];

        var vertices = _walkmesh.Triangles
            .SelectMany(static triangle => triangle.Vertices)
            .ToArray();
        var minX = vertices.Min(static vertex => vertex.X);
        var maxX = vertices.Max(static vertex => vertex.X);
        var minZ = vertices.Min(static vertex => vertex.Z);
        var maxZ = vertices.Max(static vertex => vertex.Z);
        var width = Math.Max(1, maxX - minX);
        var height = Math.Max(1, maxZ - minZ);
        const double canvasSize = 260;
        const double padding = 14;
        var scale = Math.Min((canvasSize - (padding * 2)) / width, (canvasSize - (padding * 2)) / height);
        var selectedId = SelectedTriangle?.Id;

        return _walkmesh.Triangles
            .Select((triangle, index) => new WalkmeshPreviewTriangleViewModel(
                index,
                triangle.Vertices
                    .Select(vertex => FormatPoint(
                        padding + ((vertex.X - minX) * scale),
                        canvasSize - padding - ((vertex.Z - minZ) * scale)))
                    .Aggregate((left, right) => $"{left} {right}"),
                index == selectedId))
            .ToArray();
    }

    private static string FormatVertex(VertexSR vertex) =>
        $"{vertex.X}, {vertex.Y}, {vertex.Z}";

    private static string FormatPoint(double x, double y) =>
        FormattableString.Invariant($"{x:F1},{y:F1}");

    private static string FormatAccess(Access? access) =>
        access == null
            ? string.Empty
            : $"{access.Value.A[0]}, {access.Value.A[1]}, {access.Value.A[2]}";
}

public sealed record WalkmeshTriangleViewModel(int Id, string V0, string V1, string V2, string Access);

public sealed record WalkmeshPreviewTriangleViewModel(int Id, string Points, bool IsSelected);
