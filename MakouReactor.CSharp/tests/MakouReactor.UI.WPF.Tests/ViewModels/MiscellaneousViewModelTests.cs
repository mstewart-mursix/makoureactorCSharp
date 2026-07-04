using System.Buffers.Binary;
using System.IO;
using System.Text;

using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.ViewModels;

public sealed class MiscellaneousViewModelTests
{
    [Fact]
    public void apply_changes_updates_inf_and_marks_field_modified()
    {
        var field = FieldPC.OpenDecompressed("md1stin", BuildField());
        var viewModel = new MiscellaneousViewModel("md1stin", field, section: null)
        {
            MapName = "newmap",
            Control = "44",
            CameraFocusHeight = "321",
            CameraRangeLeft = "-1",
            CameraRangeTop = "-2",
            CameraRangeRight = "3",
            CameraRangeBottom = "4",
        };

        viewModel.ApplyChanges().Should().BeTrue();
        var reopened = FieldPC.OpenDecompressed("md1stin", field.SaveDecompressed());

        field.IsModified.Should().BeTrue();
        viewModel.IsModified.Should().BeFalse();
        reopened.Inf!.MapName.Should().Be("newmap");
        reopened.Inf.Control.Should().Be(0x44);
        reopened.Inf.CameraFocusHeight.Should().Be(321);
        reopened.Inf.CameraRange.Should().Be(new InfRange(-1, -2, 3, 4));
    }

    [Fact]
    public void apply_changes_reports_validation_errors()
    {
        var field = FieldPC.OpenDecompressed("md1stin", BuildField());
        var viewModel = new MiscellaneousViewModel("md1stin", field, section: null)
        {
            Control = "not-byte",
        };

        viewModel.ApplyChanges().Should().BeFalse();

        viewModel.ValidationMessage.Should().Be("Control must be a byte value such as 0x80.");
        field.IsModified.Should().BeFalse();
    }

    private static byte[] BuildField()
    {
        var sections = Enumerable.Range(1, 9)
            .Select(index => new byte[] { (byte)index })
            .ToArray();
        sections[7] = BuildInf();

        var headerSize = 42;
        var footer = "FINAL FANTASY7"u8.ToArray();
        var size = headerSize + sections.Sum(static section => 4 + section.Length) + footer.Length;
        var data = new byte[size];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2, 4), 9);
        var cursor = headerSize;
        for (var index = 0; index < sections.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6 + (index * 4), 4), (uint)cursor);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(cursor, 4), (uint)sections[index].Length);
            sections[index].CopyTo(data.AsSpan(cursor + 4));
            cursor += 4 + sections[index].Length;
        }

        footer.CopyTo(data.AsSpan(cursor));
        return data;
    }

    private static byte[] BuildInf()
    {
        var data = new byte[740];
        Encoding.Latin1.GetBytes("md1stin").CopyTo(data, 0);
        data[9] = 0x80;
        WriteInt16(data, 10, 120);
        WriteInt16(data, 12, -256);
        WriteInt16(data, 14, -128);
        WriteInt16(data, 16, 256);
        WriteInt16(data, 18, 384);
        return data;
    }

    private static void WriteInt16(byte[] data, int offset, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, 2), value);
}
