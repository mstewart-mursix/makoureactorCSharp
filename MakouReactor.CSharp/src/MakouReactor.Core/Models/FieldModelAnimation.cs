namespace MakouReactor.Core.Models;

public sealed class FieldModelAnimation
{
    private readonly List<FieldModelAnimationFrame> _frames = [];

    public FieldModelAnimation(ModelCoordinate initialRotation)
    {
        InitialRotation = initialRotation;
    }

    public ModelCoordinate InitialRotation { get; }

    public IReadOnlyList<FieldModelAnimationFrame> Frames => _frames;

    public int FrameCount => _frames.Count;

    public int BoneCount => _frames.Count == 0 ? 0 : _frames[0].Rotations.Count;

    public void AddFrame(IReadOnlyList<ModelCoordinate> rotations, ModelCoordinate translation)
    {
        ArgumentNullException.ThrowIfNull(rotations);
        if (_frames.Count > 0 && rotations.Count != BoneCount)
            throw new InvalidDataException("All animation frames must contain the same bone rotation count.");

        _frames.Add(new FieldModelAnimationFrame(rotations.ToArray(), translation));
    }
}

public sealed record FieldModelAnimationFrame(
    IReadOnlyList<ModelCoordinate> Rotations,
    ModelCoordinate Translation);

public readonly record struct ModelCoordinate(float X, float Y, float Z);
