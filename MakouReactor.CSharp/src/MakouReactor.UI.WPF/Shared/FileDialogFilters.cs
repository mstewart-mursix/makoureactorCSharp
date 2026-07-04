namespace MakouReactor.UI.WPF.Shared;

public static class FileDialogFilters
{
    public const string OpenFieldArchiveOrFile =
        "Compatible Files (*.lgp;*.DAT;*.bin;*.iso;*.img;*.lzs;*.dec)|*.lgp;*.DAT;*.bin;*.iso;*.img;*.lzs;*.dec|" +
        "Lgp Files (*.lgp)|*.lgp|" +
        "DAT File (*.DAT)|*.DAT|" +
        "PC field File (*;*.lzs;*.dec)|*;*.lzs;*.dec|" +
        "Disc Image (*.bin;*.iso;*.img)|*.bin;*.iso;*.img|" +
        "All files (*.*)|*.*";

    public const string SaveLgpArchive =
        "Lgp File (*.lgp)|*.lgp|" +
        "All files (*.*)|*.*";

    public const string ExportPcField =
        "PC Field Map (*;*.lzs)|*;*.lzs|" +
        "Uncompressed PC Field Map (*.dec)|*.dec";

    public const string ImportField =
        "Data DAT File (*.DAT)|*.DAT|" +
        "PC Field Map (*)|*|" +
        "Field chunk (*.chunk*.?)|*.chunk*.*|" +
        "All files (*.*)|*.*";

    public const string ArchiveAnyFile =
        "All files (*.*)|*.*";

    public const string ExportAnimation =
        "FF7 Animation (*.a)|*.a|" +
        "All files (*.*)|*.*";
}
