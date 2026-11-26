using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClubEQ.UI;

// A one-line text prompt in the app's style (WPF has none).
static class Prompt
{
    public static string Ask(Window owner, string title, string value)
    {
        var box = new TextBox { Text = value, Margin = new Thickness(0, 10, 0, 14) };
        var ok = new Button { Content = "OK", Style = Ui.Res<Style>("BtnPrimary"), IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        var w = new Window
        {
            Owner = owner, Title = title, Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.Res<Brush>("CardBg"), ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = title, Style = Ui.Res<Style>("H2") }, box,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
                },
            },
        };
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); Keyboard.Focus(box); };
        return w.ShowDialog() == true ? box.Text : null;
    }
}
