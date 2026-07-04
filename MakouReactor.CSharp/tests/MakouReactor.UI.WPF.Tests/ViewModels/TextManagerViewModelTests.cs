using FluentAssertions;

using MakouReactor.Core.Models;
using MakouReactor.UI.WPF.ViewModels;

using Xunit;

namespace MakouReactor.UI.WPF.Tests.ViewModels;

public sealed class TextManagerViewModelTests
{
    [Fact]
    public void loads_synthetic_ff7_strings_into_text_rows()
    {
        var texts = new[]
        {
            new FF7String(FF7TextCodec.Decode([0xEE, 0xED, 0xD3, 0x48, 0x69, 0x03, 0xD9, 0xFF])),
            new FF7String(string.Empty),
        };

        var viewModel = new TextManagerViewModel("md1stin", texts);

        viewModel.FieldName.Should().Be("md1stin");
        viewModel.Texts.Should().HaveCount(2);
        viewModel.Texts[0].Id.Should().Be(0);
        viewModel.Texts[0].Value.Should().Be("{CLOUD}{NEW}{BLUE}Hi{x03}{WHITE}");
        viewModel.Texts[0].Length.Should().Be(32);
        viewModel.Texts[0].Preview.Should().Be("{CLOUD}{NEW}{BLUE}Hi{x03}{WHITE}");
        viewModel.Texts[1].Id.Should().Be(1);
        viewModel.Texts[1].Preview.Should().Be("(empty)");
    }

    [Fact]
    public void edited_text_rows_round_trip_through_ff7_codec()
    {
        var viewModel = new TextManagerViewModel(
            "md1stin",
            [new FF7String("Placeholder")]);

        viewModel.Texts[0].Value = "{CLOUD}{NEW}{BLUE}Hi{x03}{WHITE}";

        var text = viewModel.ToFF7Strings().Single();
        var encoded = FF7TextCodec.Encode(text.Value);

        encoded.Should().Equal([0xEE, 0xED, 0xD3, 0x48, 0x69, 0x03, 0xD9, 0xFF]);
        FF7TextCodec.Decode(encoded).Should().Be(viewModel.Texts[0].Value);
    }

    [Fact]
    public void insert_and_remove_renumber_text_entries()
    {
        var viewModel = new TextManagerViewModel(
            "md1stin",
            [new FF7String("First"), new FF7String("Third")]);

        viewModel.InsertText(1, "Second");

        viewModel.Texts.Select(static text => text.Id).Should().Equal(0, 1, 2);
        viewModel.Texts.Select(static text => text.Value).Should().Equal("First", "Second", "Third");

        viewModel.RemoveTextAt(0);

        viewModel.Texts.Select(static text => text.Id).Should().Equal(0, 1);
        viewModel.Texts.Select(static text => text.Value).Should().Equal("Second", "Third");
    }

    [Fact]
    public void row_preview_and_length_update_when_value_changes()
    {
        var row = new TextEntryViewModel(7, "Short");
        var changedProperties = new List<string?>();
        row.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        row.Value = "This line is intentionally longer than the compact preview length.";

        row.Id.Should().Be(7);
        row.Length.Should().Be(66);
        row.Preview.Should().Be("This line is intentionally longer than the compa...");
        changedProperties.Should().Contain([nameof(row.Value), nameof(row.Length), nameof(row.Preview)]);
    }
}
