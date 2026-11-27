using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClubEQ.UI;

// How to set up the Voicemeeter insert, the best way to run ClubEQ (no doubled sound, ~4 ms delay).
static class Help
{
    static readonly (string Title, string Text)[] Steps =
    {
        ("1. Send the browser to a Voicemeeter input",
            "Windows 10: Settings > System > Sound > Advanced sound options (App volume and device preferences). "
            + "Windows 11: Settings > System > Sound > Volume mixer. Play something in the browser so it shows up, then set its Output "
            + "to one of Voicemeeter's inputs, e.g. \"Voicemeeter In 5\" (yours is named \"vm5 Browser\"). Restart the browser if it doesn't switch."),
        ("2. Check Voicemeeter",
            "The browser now moves that input's strip meter (yours: strip 5 \"Browser\"). Its A1 button (your headphones) must be on. "
            + "Menu > System Settings: Preferred Main SampleRate 48000 Hz. A smaller Buffering there (e.g. 192 or 256) means less delay."),
        ("3. Switch ClubEQ to the insert",
            "Settings (top bar) > Path: Voicemeeter insert, Strip: the browser's strip, Latency: Lowest (block 128, gap 0). Apply, then Start. "
            + "Voicemeeter must be running before ClubEQ starts."),
        ("That's it",
            "ClubEQ now replaces the strip's sound inside Voicemeeter: you hear the room, never the dry sound on top, and nothing needs muting. "
            + "Stop ClubEQ and the strip plays dry again. Input and Output in the top bar aren't used in this mode."),
    };

    public static void Show(Window owner)
    {
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = "Voicemeeter insert: setup", Style = Ui.Res<Style>("H2"), Margin = new Thickness(0, 0, 0, 10) });
        foreach (var (title, text) in Steps)
        {
            body.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("CyanBrush"), Margin = new Thickness(0, 6, 0, 2) });
            body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Res<Brush>("TextBrush"), Margin = new Thickness(0, 0, 0, 6) });
        }
        var close = new Button { Content = "Close", Style = Ui.Res<Style>("BtnPrimary"), IsCancel = true, IsDefault = true, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(close);
        var w = new Window
        {
            Owner = owner, Title = "Help", Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.Res<Brush>("CardBg"), ShowInTaskbar = false, Content = body,
        };
        close.Click += (_, _) => w.Close();
        w.ShowDialog();
    }
}
