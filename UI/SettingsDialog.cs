using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubEQ.Audio;
using ClubEQ.Model;
using static ClubEQ.UI.Ui;

namespace ClubEQ.UI;

// Audio path and latency. Edits a copy; Apply writes it back and the main window rebuilds the engine.
static class SettingsDialog
{
    static readonly int[] Blocks = { 64, 128, 256, 512, 1024, 2048 };
    static readonly (string Name, int Block, int Count, int Gap, string Tip)[] Presets =
    {
        ("Safe", 512, 4, 50, "The old default: never crackles"),
        ("Low", 256, 3, 25, "About half the delay; fine on most PCs"),
        ("Lowest", 128, 3, 12, "As low as it goes; crackles on a busy PC"),
    };

    public static void Show(Window owner, Settings s, Action apply)
    {
        bool voicemeeter = s.Voicemeeter;
        int strip = s.VoicemeeterStrip, block = s.BufferLength, count = s.BufferCount, gap = s.GapMs;
        var strips = Voicemeeter.Strips();
        var body = new StackPanel();
        var estimate = new TextBlock { Foreground = Res<Brush>("CyanBrush"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };

        void Estimate()
        {
            estimate.Text = voicemeeter
                ? $"About {Math.Max(gap, (block + 64) / 48.0):0} ms from the strip to your headphones (measured: block 128 and gap 0 = 4 ms, the same as Voicemeeter alone)."
                : $"About {gap + block * count / 48.0:0} ms in ClubEQ ({gap} gap + {block * count / 48.0:0} output buffer), plus ~10-20 ms for Windows' loopback and Voicemeeter.";
        }

        void Build()
        {
            body.Children.Clear();
            body.Children.Add(Heading("Audio path"));
            body.Children.Add(Button("? How to set up the Voicemeeter insert (recommended)", () => Help.Show(Window.GetWindow(body)), tip: "Lowest delay, no doubled sound"));
            body.Children.Add(Choice("Path", new[] { "Windows devices (Input -> Output)", "Voicemeeter insert (lowest latency)" }, () => voicemeeter ? 1 : 0, i =>
            {
                voicemeeter = i == 1;
                // each path's best measured values: the insert needs no gap (one clock), loopback needs one
                (block, count, gap) = voicemeeter ? (128, 2, 0) : (512, 4, 50);
            }, Build));
            if (voicemeeter)
            {
                if (strips.Count == 0) body.Children.Add(Note("Voicemeeter isn't running or isn't installed. Start it, then open Settings again."));
                else
                {
                    body.Children.Add(Choice("Strip", strips.ToArray(), () => strip >= 0 ? strip : Voicemeeter.DefaultStrip(strips), i => strip = i, Estimate));
                    body.Children.Add(Note("ClubEQ replaces this strip's sound with its mix inside Voicemeeter: the strip's buses (A1 = your headphones) play ClubEQ, not the dry sound. "
                        + "Input and Output in the top bar aren't used. Voicemeeter's Buffering (Menu > System Settings) adds its own delay; lower it there too. Voicemeeter must run at 48 kHz."));
                }
            }
            else body.Children.Add(Note("ClubEQ records the Input device (Windows loopback) and plays to the Output device."));

            body.Children.Add(Heading("Latency"));
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var p in Presets)
            {
                int g = voicemeeter ? 0 : p.Gap; // the insert has one clock: a gap would only add delay
                row.Children.Add(Button(p.Name, () => { block = p.Block; count = p.Count; gap = g; Build(); }, tip: $"{p.Tip}: block {p.Block}, {p.Count} buffers, gap {g} ms"));
            }
            body.Children.Add(row);
            body.Children.Add(Choice("Mix block", Blocks.Select(b => $"{b} frames ({b / 48.0:0.#} ms)").ToArray(),
                () => Math.Max(0, Array.IndexOf(Blocks, block)), i => block = Blocks[i], Estimate));
            if (!voicemeeter)
                body.Children.Add(Slider("Output buffers", new ParamDef("Count", "Output buffers", 2, 8, 4), () => count, v => count = (int)Math.Clamp(MathF.Round(v), 2, 16), Estimate));
            body.Children.Add(Slider("Gap", new ParamDef("Gap", "Gap", 0, 200, 50, "ms"), () => gap, v => gap = (int)Math.Clamp(MathF.Round(v), 0, 400), Estimate));
            body.Children.Add(Note(voicemeeter
                ? "Smaller blocks = less delay, more CPU. Keep the gap at 0: the insert runs on Voicemeeter's clock and needs none (ClubEQ keeps one block anyway)."
                : "Smaller blocks and fewer buffers = less delay, more CPU, and crackles if the PC can't keep up. The gap must stay above Windows' loopback packets (~10 ms) plus one block, or the sound breaks up and restarts."));
            body.Children.Add(estimate);
            Estimate();
        }
        Build();

        var ok = new Button { Content = "Apply", Style = Res<Style>("BtnPrimary"), IsDefault = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), ToolTip = "Saves and restarts the audio" };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90 };
        var w = new Window
        {
            Owner = owner, Title = "Settings", Width = 560, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Res<Brush>("CardBg"), ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children = { body, new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { ok, cancel } } },
            },
        };
        ok.Click += (_, _) => w.DialogResult = true;
        if (w.ShowDialog() != true) return;
        s.Voicemeeter = voicemeeter; s.VoicemeeterStrip = strip; s.BufferLength = block; s.BufferCount = count; s.GapMs = gap;
        apply();
    }
}
