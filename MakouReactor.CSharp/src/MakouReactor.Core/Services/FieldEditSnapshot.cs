using MakouReactor.Core.Models;

namespace MakouReactor.Core.Services;

public sealed record FieldEditSnapshot(string FieldName, byte[] DecompressedData, bool WasModified)
{
    public static FieldEditSnapshot Capture(FieldPC field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return new FieldEditSnapshot(field.Name, field.SaveDecompressed(), field.IsModified);
    }

    public FieldPC Restore()
    {
        var restored = FieldPC.OpenDecompressed(FieldName, DecompressedData);
        if (WasModified)
            restored.SetModified();
        return restored;
    }
}
