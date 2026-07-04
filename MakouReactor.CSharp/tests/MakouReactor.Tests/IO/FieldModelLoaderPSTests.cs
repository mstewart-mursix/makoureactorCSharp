using FluentAssertions;

using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.Tests.IO;

public sealed class FieldModelLoaderPSTests
{
    [Fact]
    public void open_reads_playstation_model_loader_records()
    {
        var loader = FieldModelLoaderPS.Open(BuildModelLoader());

        loader.ModelCount.Should().Be(2);
        loader.Models[0].FaceId.Should().Be(1);
        loader.Models[0].BonesCount.Should().Be(2);
        loader.Models[0].PartsCount.Should().Be(3);
        loader.Models[0].AnimationCount.Should().Be(4);
        loader.Models[0].ModelId.Should().Be(8);
        loader.Models[1].AnimationCount.Should().Be(12);
    }

    [Fact]
    public void open_rejects_invalid_size()
    {
        var open = () => FieldModelLoaderPS.Open([12, 0, 1, 0, 1, 2, 3, 4]);

        open.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void save_round_trips_playstation_model_loader_records()
    {
        var loader = FieldModelLoaderPS.Open(BuildModelLoader());
        loader.ReplaceModels(
        [
            loader.Models[0] with { AnimationCount = 7, ModelId = 0x22 },
            loader.Models[1] with { FaceId = 0x33, Unknown3 = 0x44 },
        ]);

        var reopened = FieldModelLoaderPS.Open(loader.Save());

        reopened.ModelCount.Should().Be(2);
        reopened.Models[0].AnimationCount.Should().Be(7);
        reopened.Models[0].ModelId.Should().Be(0x22);
        reopened.Models[1].FaceId.Should().Be(0x33);
        reopened.Models[1].Unknown3.Should().Be(0x44);
    }

    internal static byte[] BuildModelLoader()
    {
        return
        [
            20, 0, 2, 0,
            1, 2, 3, 4, 5, 6, 7, 8,
            9, 10, 11, 12, 13, 14, 15, 16,
        ];
    }
}
