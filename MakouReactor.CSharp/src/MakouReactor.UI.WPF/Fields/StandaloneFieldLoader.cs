using System.IO;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.Fields;

public static class StandaloneFieldLoader
{
    public static FieldPC Open(string fieldName, string path, byte[] data)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".lzs", StringComparison.OrdinalIgnoreCase))
            return FieldPC.OpenCompressed(fieldName, data);

        if (extension.Equals(".dec", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".dat", StringComparison.OrdinalIgnoreCase))
        {
            return FieldPC.OpenDecompressed(fieldName, data);
        }

        try
        {
            return FieldPC.OpenCompressed(fieldName, data);
        }
        catch
        {
            return FieldPC.OpenDecompressed(fieldName, data);
        }
    }

    public static void Save(string path, FieldPC field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(field);

        var extension = Path.GetExtension(path);
        var data = extension.Equals(".dec", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".dat", StringComparison.OrdinalIgnoreCase)
            ? field.SaveDecompressed()
            : field.SaveCompressed();

        File.WriteAllBytes(path, data);
        field.SetSaved();
    }
}
