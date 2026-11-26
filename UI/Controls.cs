using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ClubEQ.Model;

namespace ClubEQ.UI;

// Small builders for settings rows in the house style (TrackGlow's theme). Every row writes straight into the model
// and calls `changed`; values typed into a box may go past the slider's range (no hard limits).
static class Ui
{
    public static T Res<T>(string key) => (T)Application.Current.FindResource(key);

    public static TextBlock Heading(string text) => new()
    {
        Text = text.ToUpperInvariant(), Style = Res<Style>("Label"), Foreground = Res<Brush>("NeonBrush"), Margin = new Thickness(0, 18, 0, 8),
    };

    public static TextBlock Note(string text) => new() { Text = text, Style = Res<Style>("Small"), Margin = new Thickness(0, 0, 0, 8) };

    static Grid Row(string label, UIElement control, UIElement extra = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 6), MinHeight = 32 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new TextBlock { Text = label, Foreground = Res<Brush>("TextSoft"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = label };
        g.Children.Add(t);
        Grid.SetColumn(control, 1); g.Children.Add(control);
        if (extra != null) { Grid.SetColumn(extra, 2); g.Children.Add(extra); }
        return g;
    }

    public static string Fmt(float v, string unit)
    {
        string n = MathF.Abs(v) >= 10000 ? (v / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
            : MathF.Abs(v) >= 100 ? v.ToString("0", CultureInfo.InvariantCulture)
            : MathF.Abs(v) >= 10 ? v.ToString("0.#", CultureInfo.InvariantCulture)
            : v.ToString("0.##", CultureInfo.InvariantCulture);
        return unit.Length > 0 ? n + " " + unit : n;
    }

    public static bool Parse(string s, out float v)
    {
        s = s.Trim().TrimEnd('%', '°').Replace("Hz", "").Replace("dB", "").Replace("ms", "").Replace("m", "").Trim();
        float mul = 1;
        if (s.EndsWith("k", StringComparison.OrdinalIgnoreCase)) { mul = 1000; s = s[..^1]; }
        bool ok = float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        v *= mul;
        return ok;
    }

    public static FrameworkElement Slider(string label, ParamDef d, Func<float> get, Action<float> set, Action changed)
    {
        double ToT(float v) => d.Log ? (Math.Log(Math.Max(v, d.Min * .01f)) - Math.Log(d.Min)) / (Math.Log(d.Max) - Math.Log(d.Min)) : (v - d.Min) / (d.Max - d.Min);
        float FromT(double t) => (float)(d.Log ? Math.Exp(Math.Log(d.Min) + t * (Math.Log(d.Max) - Math.Log(d.Min))) : d.Min + t * (d.Max - d.Min));
        var sl = new Slider { Minimum = 0, Maximum = 1, Value = Math.Clamp(ToT(get()), 0, 1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var box = new TextBox { Width = 76, MinHeight = 30, FontSize = 12, Text = Fmt(get(), d.Unit), Foreground = Res<Brush>("CyanBrush"), HorizontalContentAlignment = HorizontalAlignment.Right };
        bool busy = false;
        sl.ValueChanged += (_, _) =>
        {
            if (busy) return;
            float v = FromT(sl.Value);
            v = d.Max - d.Min > 20 ? MathF.Round(v) : MathF.Round(v * 1000) / 1000;
            set(v); box.Text = Fmt(v, d.Unit); changed();
        };
        void Commit()
        {
            if (!Parse(box.Text, out var v)) { box.Text = Fmt(get(), d.Unit); return; }
            set(v); busy = true; sl.Value = Math.Clamp(ToT(v), 0, 1); busy = false; box.Text = Fmt(v, d.Unit); changed();
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); Keyboard.ClearFocus(); } };
        box.LostKeyboardFocus += (_, _) => Commit();
        sl.MouseDoubleClick += (_, _) => { set(d.Default); busy = true; sl.Value = ToT(d.Default); busy = false; box.Text = Fmt(d.Default, d.Unit); changed(); };
        sl.ToolTip = $"{d.Label}: {Fmt(d.Min, d.Unit)} .. {Fmt(d.Max, d.Unit)}. Double-click resets to {Fmt(d.Default, d.Unit)}; type any value in the box.";
        return Row(label, sl, box);
    }

    public static FrameworkElement Choice(string label, string[] items, Func<int> get, Action<int> set, Action changed)
    {
        var cb = new ComboBox { ItemsSource = items, SelectedIndex = get() < 0 ? -1 : Math.Min(get(), items.Length - 1) }; // -1 = nothing picked (an action list)
        cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) { set(cb.SelectedIndex); changed(); } };
        return Row(label, cb);
    }

    public static FrameworkElement Switch(string label, Func<bool> get, Action<bool> set, Action changed)
    {
        var c = new CheckBox { Style = Res<Style>("Switch"), IsChecked = get(), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        c.Click += (_, _) => { set(c.IsChecked == true); changed(); };
        return Row(label, c);
    }

    public static FrameworkElement Text(string label, Func<string> get, Action<string> set, Action changed)
    {
        var t = new TextBox { Text = get() };
        void Commit() { if (t.Text != get()) { set(t.Text); changed(); } }
        t.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); Keyboard.ClearFocus(); } };
        t.LostKeyboardFocus += (_, _) => Commit();
        return Row(label, t);
    }

    public static Button Button(string text, Action click, string style = "BtnSmall", string tip = null)
    {
        var b = new Button { Content = text, Style = Res<Style>(style), Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
        b.Click += (_, _) => click();
        return b;
    }

    public static ToggleButton Pill(string text, bool on, Action<bool> set)
    {
        var p = new ToggleButton { Content = text, Style = Res<Style>("Pill"), IsChecked = on };
        p.Click += (_, _) => set(p.IsChecked == true);
        return p;
    }

    public static WrapPanel Wrap(params UIElement[] items)
    {
        var w = new WrapPanel();
        foreach (var i in items) w.Children.Add(i);
        return w;
    }

    public static Border Card(UIElement child) => new()
    {
        Background = Res<Brush>("Card2Bg"), BorderBrush = Res<Brush>("BorderBrush1"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 6), Margin = new Thickness(0, 0, 0, 10), Child = child,
    };
}
