using System;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;

using FluentAssertions;

using MakouReactor.AI.Parsing;
using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.AI;

using Xunit;

using CorePoint = MakouReactor.Core.Models.Point;
using CoreRect = MakouReactor.Core.Models.Rect;

namespace MakouReactor.UI.WPF.Tests.AI;

public sealed class ScenePreviewModelTests
{
    private sealed class MeshField : Field
    {
        public MeshField() : base("testmap")
        {
            static VertexSR V(short x, short y) => new(x, y, 0, 0);
            var id = new IdFile();
            id.Triangles.Add(new Triangle(V(0, 0), V(1000, 0), V(1000, 400)));
            id.Triangles.Add(new Triangle(V(0, 0), V(1000, 400), V(0, 400)));
            Walkmesh = id;
        }
    }

    private static ScenePlan Plan()
    {
        const string json =
            "{\"meta\":{\"title\":\"T\",\"model\":\"m\",\"version\":\"1.0\"}," +
            "\"actors\":[{\"id\":\"cloud\",\"position\":{\"x\":100,\"y\":100}},{\"id\":\"tifa\",\"position\":{\"x\":800,\"y\":900}}]," +
            "\"events\":[{\"id\":\"e\",\"trigger\":\"zone\",\"triggerZone\":{\"x\":10,\"y\":10,\"w\":50,\"h\":40},\"steps\":[" +
            "{\"type\":\"if_flag\",\"key\":\"k\",\"then\":[{\"type\":\"move\",\"actorId\":\"cloud\",\"to\":{\"x\":300,\"y\":200}}]}]}]," +
            "\"layout\":{\"spawnPoint\":{\"x\":50,\"y\":50},\"props\":[{\"id\":\"crate\",\"position\":{\"x\":500,\"y\":300}}]}}";
        var parsed = ScenePlanParser.Parse(Encoding.UTF8.GetBytes(json));
        parsed.Ok.Should().BeTrue(parsed.Error);
        return parsed.Plan;
    }

    [Fact]
    public void build_collects_every_drawable()
    {
        var model = ScenePreviewModel.Build(new MeshField(), Plan());

        model.Triangles.Should().HaveCount(2);
        model.Actors.Should().HaveCount(2);
        model.Actors.Should().Contain(a => a.Id == "cloud" && a.OnWalkmesh);
        model.Actors.Should().Contain(a => a.Id == "tifa" && !a.OnWalkmesh);
        model.Props.Should().ContainSingle(p => p.Label == "crate");
        model.MoveTargets.Should().ContainSingle(m => m.Position == new CorePoint(300, 200), "nested if_flag branches are included");
        model.TriggerZones.Should().ContainSingle().Which.Should().Be(new CoreRect(10, 10, 50, 40));
        model.Spawn.Should().Be(new CorePoint(50, 50));
    }

    [Fact]
    public void extent_grows_to_include_off_mesh_points()
    {
        var model = ScenePreviewModel.Build(new MeshField(), Plan());

        model.Extent.X.Should().Be(0);
        model.Extent.Y.Should().Be(0);
        (model.Extent.Y + model.Extent.H).Should().BeGreaterOrEqualTo(900);
    }

    [Fact]
    public void fit_preserves_aspect_and_centres()
    {
        var model = ScenePreviewModel.Build(null, null); // 320x240 default extent

        var (scale, ox, oy) = model.Fit(344, 500, margin: 12);

        scale.Should().BeApproximately(1.0, 1e-9);
        ox.Should().BeApproximately(12, 1e-9);
        oy.Should().BeApproximately((500 - 240) / 2.0, 1e-9);
    }

    [Fact]
    public void empty_inputs_are_safe()
    {
        var model = ScenePreviewModel.Build(null, null);

        model.Triangles.Should().BeEmpty();
        model.Actors.Should().BeEmpty();
        model.Spawn.Should().BeNull();
    }

    [Fact]
    public void canvas_renders_without_errors()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var canvas = new ScenePreviewCanvas { Model = ScenePreviewModel.Build(new MeshField(), Plan()) };
                canvas.Measure(new System.Windows.Size(400, 300));
                canvas.Arrange(new System.Windows.Rect(0, 0, 400, 300));
                var bitmap = new RenderTargetBitmap(400, 300, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(canvas);
                canvas.Model = null;
                bitmap.Render(canvas);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure.Should().BeNull();
    }
}
