using System.Windows;
using System.Windows.Controls;

using MakouReactor.Core.Models;

namespace MakouReactor.UI.WPF.Scripts;

public partial class RawOpcodeDialog : Window
{
    private readonly RawOpcode _opcode;
    private readonly List<TypedOpcodeField> _typedFields = [];

    public byte[] EditedBytes { get; private set; } = [];

    public RawOpcodeDialog(RawOpcode opcode)
    {
        InitializeComponent();
        _opcode = opcode;
        DataContext = new RawOpcodeViewModel(opcode);
        InitializeTypedEditor(opcode);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!TryApplyTypedEditor())
            return;

        if (!RawOpcodeHexParser.TryParse(RawBytesBox.Text, _opcode.Bytes.Length, out var bytes, out var error))
        {
            StatusLabel.Text = error;
            return;
        }

        EditedBytes = bytes;
        DialogResult = true;
    }

    private void InitializeTypedEditor(RawOpcode opcode)
    {
        _typedFields.Clear();
        _typedFields.AddRange(TypedOpcodeEditor.GetFields(opcode));
        if (_typedFields.Count == 0)
            return;

        TypedEditorPanel.Visibility = Visibility.Visible;
        var labels = TypedLabels();
        var boxes = TypedBoxes();
        for (var index = 0; index < labels.Count; index++)
        {
            var isVisible = index < _typedFields.Count;
            labels[index].Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            boxes[index].Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            if (!isVisible)
                continue;

            labels[index].Text = _typedFields[index].Label;
            boxes[index].Text = _typedFields[index].Value.ToString();
            boxes[index].ToolTip = $"0-{_typedFields[index].MaxValue}";
        }
    }

    private bool TryApplyTypedEditor()
    {
        if (TypedEditorPanel.Visibility != Visibility.Visible)
            return true;

        var values = new List<int>();
        var boxes = TypedBoxes();
        for (var index = 0; index < _typedFields.Count; index++)
        {
            if (!TryReadInt32(boxes[index].Text, _typedFields[index], out var value))
                return false;

            values.Add(value);
        }

        if (!TypedOpcodeEditor.TryBuild(_opcode, values, out var bytes, out var error))
        {
            StatusLabel.Text = error;
            return false;
        }

        RawBytesBox.Text = string.Join(" ", bytes.Select(static value => value.ToString("X2")));
        return true;
    }

    private IReadOnlyList<TextBlock> TypedLabels() =>
    [
        TypedWindowLabel,
        TypedTextLabel,
        TypedXLabel,
        TypedYLabel,
        TypedWidthLabel,
        TypedHeightLabel,
        TypedExtra1Label,
        TypedExtra2Label
    ];

    private IReadOnlyList<System.Windows.Controls.TextBox> TypedBoxes() =>
    [
        TypedWindowBox,
        TypedTextBox,
        TypedXBox,
        TypedYBox,
        TypedWidthBox,
        TypedHeightBox,
        TypedExtra1Box,
        TypedExtra2Box
    ];

    private bool TryReadInt32(string text, TypedOpcodeField field, out int value)
    {
        if (int.TryParse(text.Trim(), out value) &&
            value >= field.MinValue &&
            value <= field.MaxValue)
        {
            return true;
        }

        StatusLabel.Text = $"{field.Label} must be between {field.MinValue} and {field.MaxValue}.";
        return false;
    }

    private sealed record RawOpcodeViewModel(RawOpcode Opcode)
    {
        public string OffsetHex => Opcode.OffsetHex;
        public string Name => Opcode.Name;
        public string IdHex => Opcode.IdHex;
        public string SizeLabel => Opcode.IsTruncated
            ? $"{Opcode.Size}/{Opcode.DeclaredSize}"
            : Opcode.Size.ToString();
        public string Arguments => string.IsNullOrWhiteSpace(Opcode.Arguments)
            ? "(none)"
            : Opcode.Arguments;
        public string RawBytesHex => Opcode.RawBytesHex;
        public string Warning => string.IsNullOrWhiteSpace(Opcode.Warning)
            ? "(none)"
            : Opcode.Warning;
    }
}
