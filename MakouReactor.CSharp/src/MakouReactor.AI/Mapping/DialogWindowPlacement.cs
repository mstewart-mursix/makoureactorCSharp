using System;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Mapping;

/// <summary>
/// Where <c>say</c> dialog windows go (work plan phase 8, "dialog window auto-placement"): in
/// the vertical half of the screen opposite the speaking actor, clamped on screen exactly like
/// the game editor's <c>FF7Window::realPos()</c>.
/// </summary>
/// <remarks>
/// Screen limits, all from the C++ reference implementation:
/// <list type="bullet">
/// <item>The field screen is 320 x 240: <c>WalkmeshWidget::computeFov()</c>
/// (<c>src/3d/WalkmeshWidget.cpp</c> line 110) derives the field of view from a 240 px tall
/// screen, and <c>LayoutGenerator.DefaultBounds</c> is 320 x 240.</item>
/// <item>Windows stay within x = 8..312 and y = 8..223: <c>FF7Window::realPos()</c>
/// (<c>src/core/field/Opcode.h</c> lines 223-247) moves a window left/up when
/// <c>x + w &gt; 312</c> (line 230) or <c>y + h &gt; 223</c> (line 234), then right/down when
/// <c>x &lt; 8</c> (line 238) or <c>y &lt; 8</c> (line 242). The text preview that draws windows at
/// <c>realPos()</c> is 320 x 224 (<c>src/widgets/TextPreview.cpp</c> line 43, drawing at line 350).</item>
/// </list>
/// </remarks>
public static class DialogWindowPlacement
{
    /// <summary>Field screen width in pixels.</summary>
    public const int ScreenWidth = 320;

    /// <summary>Field screen height in pixels (<c>WalkmeshWidget::computeFov</c> uses 240).</summary>
    public const int ScreenHeight = 240;

    /// <summary>Smallest window x kept by <c>FF7Window::realPos()</c> (Opcode.h line 238).</summary>
    public const int MinX = 8;

    /// <summary>Largest window right edge kept by <c>FF7Window::realPos()</c> (Opcode.h line 230).</summary>
    public const int MaxRight = 312;

    /// <summary>Smallest window y kept by <c>FF7Window::realPos()</c> (Opcode.h line 242).</summary>
    public const int MinY = 8;

    /// <summary>Largest window bottom edge kept by <c>FF7Window::realPos()</c> (Opcode.h line 234).</summary>
    public const int MaxBottom = 223;

    /// <summary>
    /// Port of <c>FF7Window::realPos()</c> (Opcode.h lines 223-247) for a normal window: the
    /// position the game shows a <paramref name="width"/> x <paramref name="height"/> window at.
    /// As in the C++, the minimum wins when a window is larger than the allowed area.
    /// </summary>
    public static (short X, short Y) RealPos(int x, int y, int width, int height)
    {
        if (x + width > MaxRight)
            x = MaxRight - width;
        if (y + height > MaxBottom)
            y = MaxBottom - height;
        if (x < MinX)
            x = MinX;
        if (y < MinY)
            y = MinY;
        return ((short)x, (short)y);
    }

    /// <summary>
    /// Placement used when the speaker is unknown or the narrator: horizontally centred at the
    /// top of the screen (the emitter's original placement), passed through <see cref="RealPos"/>.
    /// </summary>
    public static (short X, short Y) Default(int width, int height) =>
        RealPos(CentredX(width), MinY, width, height);

    /// <summary>
    /// Placement that keeps the window off the speaker: a speaker in the top half of the screen
    /// (<paramref name="actorScreenY"/> &lt; <see cref="ScreenHeight"/> / 2) gets a window flush with
    /// the bottom limit, a speaker in the bottom half one flush with the top limit. The window is
    /// horizontally centred and clamped with <see cref="RealPos"/>, so it is always fully on screen
    /// (a window taller than the allowed area is pinned to the top, as <c>realPos()</c> does).
    /// </summary>
    /// <param name="actorScreenY">The speaker's screen y in pixels (0 = top, 240 = bottom).</param>
    /// <param name="width">Window width.</param>
    /// <param name="height">Window height.</param>
    public static (short X, short Y) AvoidingActor(double actorScreenY, int width, int height)
    {
        var y = actorScreenY < ScreenHeight / 2.0 ? MaxBottom - height : MinY;
        return RealPos(CentredX(width), y, width, height);
    }

    /// <summary>
    /// Projects a field position (walkmesh units) to 320 x 240 screen pixels with a field camera,
    /// porting the view and projection of <c>WalkmeshWidget::paintGL()</c>
    /// (<c>src/3d/WalkmeshWidget.cpp</c> lines 145-181) with the widget's own model transform reset
    /// (<c>resetCamera()</c>, line 557) and the viewport set to the 320 x 240 game screen.
    /// </summary>
    /// <remarks>
    /// The result is relative to the camera's centre. Fields whose background is larger than the
    /// screen scroll to follow the player, which this does not model.
    /// </remarks>
    /// <returns>False when the camera has no zoom or the point is not in front of the camera.</returns>
    public static bool TryProjectToScreen(FieldCamera camera, int x, int y, int z, out double screenX, out double screenY)
    {
        screenX = screenY = 0;
        if (camera.Zoom == 0)
            return false;

        // WalkmeshWidget.cpp lines 161-175: axes and position in 4.12 fixed point; the Y axis and
        // the second position component are negated.
        var axisX = new Vec(camera.AxisX.X, camera.AxisX.Y, camera.AxisX.Z) / 4096.0;
        var axisY = new Vec(-camera.AxisY.X, -camera.AxisY.Y, -camera.AxisY.Z) / 4096.0;
        var axisZ = new Vec(camera.AxisZ.X, camera.AxisZ.Y, camera.AxisZ.Z) / 4096.0;
        var position = new Vec(camera.PositionX, -camera.PositionY, camera.PositionZ) / 4096.0;

        // Lines 176-178.
        var eye = new Vec(
            -((position.X * axisX.X) + (position.Y * axisY.X) + (position.Z * axisZ.X)),
            -((position.X * axisX.Y) + (position.Y * axisY.Y) + (position.Z * axisZ.Y)),
            -((position.X * axisX.Z) + (position.Y * axisY.Z) + (position.Z * axisZ.Z)));

        // Lines 180-181: lookAt(eye, eye + axisZ, axisY), as QMatrix4x4::lookAt builds it.
        var forward = axisZ.Normalized();
        var side = Vec.Cross(forward, axisY).Normalized();
        var up = Vec.Cross(side, forward);
        if (!forward.IsFinite || !side.IsFinite)
            return false;

        // Walkmesh vertices are drawn at (x, y, z) / 4096 (line 195).
        var relative = (new Vec(x, y, z) / 4096.0) - eye;
        var viewX = Vec.Dot(side, relative);
        var viewY = Vec.Dot(up, relative);
        var depth = Vec.Dot(forward, relative); // -z in view space: the perspective w

        // Line 146: perspective(fovy, aspect, 0.001, 1000), fovy from computeFov (line 110):
        // 2 * atan(240 / (2 * zoom)), so cot(fovy / 2) = 2 * zoom / 240.
        const double near = 0.001;
        if (!(depth > near))
            return false;

        var focal = 2.0 * camera.Zoom / ScreenHeight;
        const double aspect = (double)ScreenWidth / ScreenHeight;
        var ndcX = focal / aspect * viewX / depth;
        var ndcY = focal * viewY / depth;

        screenX = (ndcX + 1) / 2 * ScreenWidth;
        screenY = (1 - ndcY) / 2 * ScreenHeight;
        return double.IsFinite(screenX) && double.IsFinite(screenY);
    }

    /// <summary>
    /// Approximate screen y without a camera: the field <paramref name="y"/> mapped linearly over
    /// the walkmesh's Y extent, with the largest Y at the top of the screen. That direction is an
    /// assumption: it holds for the default camera of <c>CaFile::initEmpty()</c>, which looks
    /// towards +Y (its Z axis is (81, 3325, -2391)), but real fields can use any camera.
    /// </summary>
    /// <param name="walkmeshBounds">Walkmesh X/Y bounding box (<c>WalkmeshGeometry.Bounds</c>).</param>
    /// <param name="y">Field Y of the actor.</param>
    public static double ApproximateScreenY(Rect walkmeshBounds, int y)
    {
        var minY = walkmeshBounds.Y;
        var maxY = walkmeshBounds.Y + Math.Max(walkmeshBounds.H - 1, 0);
        if (maxY <= minY)
            return ScreenHeight / 2.0;

        var fraction = Math.Clamp((maxY - (double)y) / (maxY - minY), 0, 1);
        return fraction * ScreenHeight;
    }

    private static int CentredX(int width) => (ScreenWidth - width) / 2;

    private readonly record struct Vec(double X, double Y, double Z)
    {
        public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec operator /(Vec a, double d) => new(a.X / d, a.Y / d, a.Z / d);

        public static double Dot(Vec a, Vec b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        public static Vec Cross(Vec a, Vec b) => new(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));

        public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

        public Vec Normalized()
        {
            var length = Math.Sqrt(Dot(this, this));
            return length > 0 ? this / length : new Vec(double.NaN, double.NaN, double.NaN);
        }
    }
}
