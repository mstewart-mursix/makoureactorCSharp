using MakouReactor.Core.Models;

namespace MakouReactor.Tests;

/// <summary>Concrete <see cref="Field"/> for tests that lets a test attach a walkmesh and Section 1.</summary>
public sealed class MeshField : Field
{
    public MeshField(string name, IdFile? walkmesh = null) : base(name) => Walkmesh = walkmesh;

    public static IdFile Square(short min, short max)
    {
        var id = new IdFile();
        id.Triangles.Add(new Triangle(V(min, min), V(max, min), V(max, max)));
        id.Triangles.Add(new Triangle(V(min, min), V(max, max), V(min, max)));
        // Edge order is v0-v1, v1-v2, v2-v0; the two triangles share the diagonal.
        id.Access.Add(new Access(-1, -1, 1));
        id.Access.Add(new Access(0, -1, -1));
        return id;
    }

    /// <summary>L shape: a 100x300 vertical bar plus a 200x100 foot at the bottom (x 0..200, y 0..300).</summary>
    public static IdFile LShape()
    {
        var id = new IdFile();
        void Quad(short x0, short y0, short x1, short y1)
        {
            id.Triangles.Add(new Triangle(V(x0, y0), V(x1, y0), V(x1, y1)));
            id.Triangles.Add(new Triangle(V(x0, y0), V(x1, y1), V(x0, y1)));
            id.Access.Add(new Access(-1, -1, -1));
            id.Access.Add(new Access(-1, -1, -1));
        }

        Quad(0, 0, 100, 300);
        Quad(100, 200, 200, 300);
        return id;
    }

    private static VertexSR V(short x, short y) => new(x, y, 0, 0);
}
