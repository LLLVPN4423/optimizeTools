using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OptiForge.App;

/// <summary>Minimal modal text prompt (rename chat, etc.).</summary>
internal static class PromptDialog
{
    public static string? Ask(string title, string label, string initial)
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x17, 0x19, 0x1E)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF3, 0xF5)),
            Owner = Application.Current?.MainWindow
        };

        var box = new TextBox
        {
            Text = initial,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8),
            Background = new SolidColorBrush(Color.FromRgb(0x1D, 0x20, 0x27)),
            Foreground = win.Foreground,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x34, 0x3D))
        };

        string? result = null;
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            result = box.Text;
            win.DialogResult = true;
        };
        cancel.Click += (_, _) => win.DialogResult = false;

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                result = box.Text;
                win.DialogResult = true;
            }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { ok, cancel }
        };

        win.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = label, Opacity = 0.85 },
                box,
                buttons
            }
        };

        box.Focus();
        box.SelectAll();
        return win.ShowDialog() == true ? result : null;
    }
}
