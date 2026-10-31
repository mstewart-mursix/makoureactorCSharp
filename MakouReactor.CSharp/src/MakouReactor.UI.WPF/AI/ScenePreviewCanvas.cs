using System.Globalization;
using System.Windows;
using System.Windows.Media;

using CorePoint = MakouReactor.Core.Models.Point;

namespace MakouReactor.UI.WPF.AI;

/// <summary>
/// Top-down 2D preview of a scene plan on its field: walkmesh triangles (light fill), exit lines
/// (gray), trigger zones (dashed), props (squares), move targets (small circles), spawn (star)
/// and planned actors (labelled dots; red when off the walkmesh).
/// </summary>
public sealed class ScenePreviewCanvas : FrameworkElement
{
    private static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)));
    private static readonly Brush MeshFill = Freeze(new SolidColorBrush(Color.FromRgb(0xDD, 0xEE, 0xDD)));
    private static readonly Pen MeshPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xA8, 0xC8, 0xA8)), 0.5));
    private static readonly Pen ExitPen = Freeze(new Pen(Brushes.Gray, 2));
    private static readonly Pen ZonePen = Freeze(new Pen(Brushes.DarkOrange, 1.5) { DashStyle = DashStyles.Dash });
    private static readonly Brush PropFill = Freeze(new SolidColorBrush(Color.FromRgb(0x8D, 0x6E, 0x63)));
    private static readonly Brush ActorFill = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)));
    private static readonly Brush OffMeshFill = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)));
    private static readonly Pen MovePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)), 1));
    private static readonly Brush SpawnFill = Freeze(new SolidColorBrush(Color.FromRgb(0xFB, 0xC0, 0x2D)));
    private static readonly Pen OutlinePen = Freeze(new Pen(Brushes.Black, 0.75));

    private ScenePreviewModel? _model;

    /// <summary>The preview to draw; null clears the canvas.</summary>
    public ScenePreviewModel? Model
    {
        get => _model;
        set
        {
            _model = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        dc.DrawRectangle(Background, null, new Rect(size));
        if (_model == null || size.Width < 10 || size.Height < 10)
            return;

        var (scale, ox, oy) = _model.Fit(size.Width, size.Height);
        System.Windows.Point Map(CorePoint p) => new(ox + p.X * scale, oy + p.Y * scale);

        foreach (var tri in _model.Triangles)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(Map(tri[0]), isFilled: true, isClosed: true);
                ctx.LineTo(Map(tri[1]), isStroked: true, isSmoothJoin: false);
                ctx.LineTo(Map(tri[2]), isStroked: true, isSmoothJoin: false);
            }
            geometry.Freeze();
            dc.DrawGeometry(MeshFill, MeshPen, geometry);
        }

        foreach (var exit in _model.Exits)
        {
            dc.DrawLine(ExitPen, Map(exit.A), Map(exit.B));
            DrawLabel(dc, exit.Label, Map(exit.A), Brushes.Gray);
        }

        foreach (var zone in _model.TriggerZones)
        {
            var a = Map(new CorePoint(zone.X, zone.Y));
            var b = Map(new CorePoint(zone.X + zone.W, zone.Y + zone.H));
            dc.DrawRectangle(null, ZonePen, new Rect(a, b));
        }

        foreach (var prop in _model.Props)
        {
            var c = Map(prop.Position);
            dc.DrawRectangle(PropFill, OutlinePen, new Rect(c.X - 4, c.Y - 4, 8, 8));
            DrawLabel(dc, prop.Label, c, PropFill);
        }

        foreach (var move in _model.MoveTargets)
            dc.DrawEllipse(null, MovePen, Map(move.Position), 3, 3);

        if (_model.Spawn is CorePoint spawn)
            DrawStar(dc, Map(spawn));

        foreach (var actor in _model.Actors)
        {
            var c = Map(actor.Position);
            var fill = actor.OnWalkmesh ? ActorFill : OffMeshFill;
            dc.DrawEllipse(fill, OutlinePen, c, 5, 5);
            DrawLabel(dc, actor.OnWalkmesh ? actor.Id : actor.Id + " (off walkmesh)", c, fill);
        }
    }

    private void DrawLabel(DrawingContext dc, string text, System.Windows.Point at, Brush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new System.Windows.Point(at.X + 7, at.Y - 7));
    }

    private static void DrawStar(DrawingContext dc, System.Windows.Point c)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < 10; i++)
            {
                var r = i % 2 == 0 ? 8.0 : 3.5;
                var angle = Math.PI / 2 + i * Math.PI / 5;
                var p = new System.Windows.Point(c.X + r * Math.Cos(angle), c.Y - r * Math.Sin(angle));
                if (i == 0)
                    ctx.BeginFigure(p, isFilled: true, isClosed: true);
                else
                    ctx.LineTo(p, isStroked: true, isSmoothJoin: false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(SpawnFill, OutlinePen, geometry);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
