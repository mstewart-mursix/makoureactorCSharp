using System.Windows;
using System.Windows.Controls;

namespace MakouReactor.UI.WPF.Fields;

public sealed class FieldNameDialog : Window
{
    private readonly TextBox _fieldNameBox;

    public FieldNameDialog(string title, string fieldName)
    {
        Title = title;
        Width = 320;
        Height = 140;
        MinWidth = 280;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _fieldNameBox = new TextBox
        {
            Text = fieldName,
            MaxLength = 20,
            Margin = new Thickness(0, 4, 0, 12),
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 76,
            IsDefault = true,
            Margin = new Thickness(0, 0, 6, 0),
        };
        okButton.Click += (_, _) =>
        {
            FieldName = _fieldNameBox.Text;
            DialogResult = true;
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 76,
            IsCancel = true,
        };

        Content = new StackPanel
        {
            Margin = new Thickness(10),
            Children =
            {
                new TextBlock { Text = "Field name:" },
                _fieldNameBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { okButton, cancelButton },
                },
            },
        };

        Loaded += (_, _) =>
        {
            _fieldNameBox.Focus();
            _fieldNameBox.SelectAll();
        };
    }

    public string FieldName { get; private set; } = string.Empty;
}
