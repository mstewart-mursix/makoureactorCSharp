using System;
using System.IO;

using FluentAssertions;

using MakouReactor.Core.IO;
using MakouReactor.Core.Models;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.Services;

public sealed class SafeSaveRoutingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mr_saveroute_" + Guid.NewGuid().ToString("N"));

    public SafeSaveRoutingTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void pc_lgp_archives_save_through_safe_writer()
    {
        var lgp = Path.Combine(_dir, "flevel.lgp");
        LgpArchive.Create(lgp, new[] { new LgpArchiveFile("testmap", FieldPC.CreateEmpty("testmap").SaveCompressed()) });

        MainWindow.UsesSafeArchiveWriter(FieldArchive.Open(lgp)).Should().BeTrue();
    }

    [Fact]
    public void loose_directories_keep_their_save_path()
    {
        var loose = Path.Combine(_dir, "loose");
        Directory.CreateDirectory(loose);

        MainWindow.UsesSafeArchiveWriter(FieldArchive.OpenPcDirectory(loose)).Should().BeFalse();
    }
}
