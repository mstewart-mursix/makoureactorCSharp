using System.IO;
using System.Windows.Media.Imaging;

namespace MakouReactor.UI.WPF.Archive;

public static class ArchiveImagePreviewLoader
{
    public static bool TryLoad(string path, byte[] data, out BitmapImage? image)
    {
        image = null;
        if (!LooksLikeBitmap(path, data))
            return false;

        try
        {
            using var stream = new MemoryStream(data);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
            return true;
        }
        catch
        {
            image = null;
            return false;
        }
    }

    public static bool LooksLikeBitmap(string path, byte[] data)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return data.Length >= 8 &&
            ((data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) ||
             (data[0] == 0xFF && data[1] == 0xD8) ||
             (data[0] == 0x42 && data[1] == 0x4D) ||
             (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46));
    }
}
