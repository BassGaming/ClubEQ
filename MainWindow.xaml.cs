using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClubEQ.Audio;
using ClubEQ.Model;
using ClubEQ.UI;
using static ClubEQ.UI.Ui;

namespace ClubEQ;

public partial class MainWindow : Window
{
    Engine engine;
    readonly Settings settings = Store.LoadSettings();
    List<Preset> presets;
    Preset cur;
    object selected;
    readonly Inspector insp;
    readonly DispatcherTimer frame = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    readonly DispatcherTimer saveSoon = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Stopwatch clock = Stopwatch.StartNew();
    double lastT;
    bool loading;
    string note = ""; double noteUntil;

    public MainWindow()
    {
        InitializeComponent();
        Width = settings.Width; Height = settings.Height;
        if (settings.Maximized) WindowState = WindowState.Maximized;

        insp = new Inspector(InspectorPanel, Changed, Reshaped) { Preset = () => cur, Select = Select, PresetAction = PresetAction };
        Room.SelectionChanged += o => Select(o);
        Room.Committed += () => { Changed(); if (selected is not Macro) insp.Show(selected); };
        Room.Level = i => engine?.Levels is { } l && i < l.Length ? l[i] : 0;
        Room.InsideOf = i => engine?.IsInside(i) == true;

        presets = Store.LoadPresets();
        LoadPreset(presets.FirstOrDefault(p => p.Name == settings.Preset) ?? presets[0]);

        try { engine = new Engine(); }
        catch (Exception e) { Status("Audio engine failed: " + e.Message + " (is fmod.dll next to ClubEQ.exe?)"); }
        FillDevices();

        PresetBox.SelectionChanged += (_, _) => { if (!loading && PresetBox.SelectedIndex >= 0) { SaveNow(); LoadPreset(presets[PresetBox.SelectedIndex]); } };
        InputBox.SelectionChanged += (_, _) => { if (loading) return; settings.Input = InputBox.SelectedItem as string ?? ""; SaveSettings(); if (engine?.Running == true) Start(); };
        OutputBox.SelectionChanged += (_, _) => { if (loading) return; settings.Output = OutputBox.SelectedItem as string ?? ""; SaveSettings(); SetOutput(); };
        MasterSlider.ValueChanged += (_, _) => { if (loading) return; cur.Master = (float)MasterSlider.Value; MasterLabel.Text = $"MASTER {cur.Master * 100:0} %"; Changed(); };
        PowerButton.Click += (_, _) => { if (engine?.Running == true) { engine.Stop(); UpdatePower(); } else Start(); };

        saveSoon.Tick += (_, _) => SaveNow();
        frame.Tick += (_, _) => Frame();
        SizeChanged += (_, _) => Reflow();
        SourceInitialized += (_, _) => DarkTitleBar();
        Closing += (_, _) => Shutdown();
        Loaded += (_, _) =>
        {
            frame.Start();
            if (engine != null && settings.Input.Length > 0) Start(); // used before: play right away
        };
    }

    // ---------- devices ----------
    void FillDevices()
    {
        if (engine == null) return;
        loading = true;
        try
        {
            var ins = engine.Inputs();
            var outs = engine.Outputs();
            InputBox.ItemsSource = ins; OutputBox.ItemsSource = outs;
            // first run: the virtual cable the browser plays into
            string wantIn = settings.Input.Length > 0 ? settings.Input : ins.FirstOrDefault(n => n.Contains("vm5 Browser", StringComparison.OrdinalIgnoreCase)) ?? ""; // a Voicemeeter strip: FMOD records it as "[loopback]"
            InputBox.SelectedItem = ins.FirstOrDefault(n => n == wantIn);
            OutputBox.SelectedItem = outs.FirstOrDefault(n => n == settings.Output) ?? outs.FirstOrDefault();
            settings.Input = InputBox.SelectedItem as string ?? "";
            settings.Output = OutputBox.SelectedItem as string ?? "";
        }
        catch (Exception e) { Status("Devices: " + e.Message); }
        finally { loading = false; }
    }

    void SetOutput()
    {
        try { engine?.SetOutput(OutputBox.SelectedIndex); }
        catch (Exception e) { Status("Output: " + e.Message); }
    }

    void Start()
    {
        if (engine == null) return;
        if (InputBox.SelectedIndex < 0) { Status("Pick an input device first."); return; }
        try
        {
            SetOutput();
            engine.Start(InputBox.SelectedIndex);
            string i = settings.Input.Replace("[loopback]", "").Trim(), o = settings.Output;
            if (i.Length > 0 && (o.Contains(i, StringComparison.OrdinalIgnoreCase) || i.Contains(o, StringComparison.OrdinalIgnoreCase)))
                Flash("Input and output look like the same device: that can feed back. Pick your headphones as output.");
        }
        catch (Exception e) { Status("Start: " + e.Message); }
        UpdatePower();
    }

    void UpdatePower() => PowerButton.Content = engine?.Running == true ? "Stop" : "Start";

    // ---------- presets ----------
    void LoadPreset(Preset p)
    {
        loading = true;
        cur = p;
        settings.Preset = p.Name;
        PresetBox.ItemsSource = presets.Select(x => x.Name).ToList();
        PresetBox.SelectedIndex = presets.IndexOf(p);
        MasterSlider.Value = p.Master;
        MasterLabel.Text = $"MASTER {p.Master * 100:0} %";
        loading = false;
        Room.Preset = p;
        Select(null);
        BuildMacros();
        SaveSettings();
    }

    void PresetAction(string what)
    {
        switch (what)
        {
            case "save as":
                {
                    var name = Prompt.Ask(this, "Save as a new preset", cur.Name + " 2");
                    if (string.IsNullOrWhiteSpace(name)) return;
                    var copy = Store.FromJson<Preset>(Store.ToJson(cur));
                    copy.Name = Unique(name.Trim());
                    presets.Add(copy); Store.SavePreset(copy); LoadPreset(copy);
                    break;
                }
            case "rename":
                {
                    var name = Prompt.Ask(this, "Rename the preset", cur.Name);
                    if (string.IsNullOrWhiteSpace(name) || name.Trim() == cur.Name) return;
                    Store.DeletePreset(cur.Name);
                    cur.Name = Unique(name.Trim());
                    Store.SavePreset(cur); LoadPreset(cur);
                    break;
                }
            case "delete":
                if (MessageBox.Show(this, $"Delete the preset \"{cur.Name}\"? Built-in presets come back as new.", "ClubEQ", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                Store.DeletePreset(cur.Name);
                presets = Store.LoadPresets();
                LoadPreset(presets[0]);
                break;
            case "reset":
                {
                    var b = Builtin.All().FirstOrDefault(x => x.Name == cur.Name);
                    if (b == null) { Flash("Only built-in presets can be reset."); return; }
                    if (MessageBox.Show(this, $"Reset \"{cur.Name}\" to the built-in version? Your changes to it are lost.", "ClubEQ", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    presets[presets.IndexOf(cur)] = b; Store.SavePreset(b); LoadPreset(b);
                    break;
                }
            case "export":
                {
                    var d = new Microsoft.Win32.SaveFileDialog { FileName = cur.Name + ".clubeq.json", Filter = "ClubEQ preset|*.json" };
                    if (d.ShowDialog(this) == true) { File.WriteAllText(d.FileName, Store.ToJson(cur)); Flash("Exported to " + d.FileName); }
                    break;
                }
            case "import":
                {
                    var d = new Microsoft.Win32.OpenFileDialog { Filter = "ClubEQ preset|*.json" };
                    if (d.ShowDialog(this) != true) return;
                    try
                    {
                        var p = Store.FromJson<Preset>(File.ReadAllText(d.FileName)) ?? throw new InvalidDataException("empty file");
                        foreach (var s in p.Speakers) foreach (var e in s.Chain) Effects.Get(e.Type); // unknown effect types fail here, not in the engine
                        p.Name = Unique(p.Name);
                        presets.Add(p); Store.SavePreset(p); LoadPreset(p);
                    }
                    catch (Exception e) { Flash("Import failed: " + e.Message); }
                    break;
                }
        }
    }

    string Unique(string name)
    {
        string n = name; int i = 2;
        while (presets.Any(p => p.Name == n && !ReferenceEquals(p, cur))) n = $"{name} {i++}";
        return n;
    }

    // ---------- macros ----------
    void BuildMacros()
    {
        MacroStrip.Children.Clear();
        foreach (var m in cur.Macros)
        {
            var name = new CheckBox { Style = Res<Style>("Switch"), IsChecked = m.On, Content = new TextBlock { Text = m.Name.ToUpperInvariant(), FontWeight = FontWeights.Bold, FontSize = 13 } };
            var pct = new TextBlock { Text = $"{m.Amount * 100:0} %", Foreground = Res<Brush>("CyanBrush"), Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var sl = new Slider { Minimum = 0, Maximum = 1, Value = m.Amount, Width = 180, Margin = new Thickness(14, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var card = new Border
            {
                Background = Res<Brush>("CardBg"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 8, 8, 8), Margin = new Thickness(0, 0, 10, 0),
            };
            void Glow()
            {
                card.BorderBrush = m.On ? Res<Brush>("NeonBrush") : Res<Brush>("BorderBrush1");
                card.Effect = m.On ? new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0xFF, 0x3D, 0xA8), BlurRadius = 18, ShadowDepth = 0, Opacity = .55 } : null;
            }
            name.Click += (_, _) => { m.On = name.IsChecked == true; Glow(); Changed(); };
            sl.ValueChanged += (_, _) => { m.Amount = (float)sl.Value; pct.Text = $"{m.Amount * 100:0} %"; Changed(); };
            var edit = Button("✎", () => Select(m), tip: "Edit what this macro changes");
            edit.Margin = new Thickness(8, 0, 0, 0);
            card.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, sl, pct, edit } };
            Glow();
            MacroStrip.Children.Add(card);
        }
        var add = Button("+ Macro", () =>
        {
            var m = new Macro { Name = "Macro " + (cur.Macros.Count + 1), Targets = { new MacroTarget { Param = "Volume", From = 1, To = 1, Multiply = true } } };
            cur.Macros.Add(m); Reshaped(); Select(m);
        });
        add.VerticalAlignment = VerticalAlignment.Center;
        MacroStrip.Children.Add(add);
    }

    // ---------- selection, changes ----------
    void Select(object o)
    {
        selected = o;
        Room.Selected = o is Macro ? null : o;
        insp.Show(o);
        Room.InvalidateVisual();
    }

    void Changed() { saveSoon.Stop(); saveSoon.Start(); }

    void Reshaped()
    {
        Changed();
        BuildMacros();
        if (selected is Speaker s && !cur.Speakers.Contains(s)) selected = null;
        if (selected is Macro m && !cur.Macros.Contains(m)) selected = null;
        insp.Show(selected);
        Room.InvalidateVisual();
    }

    void SaveNow()
    {
        saveSoon.Stop();
        try { Store.SavePreset(cur); } catch (Exception e) { Flash("Saving failed: " + e.Message); }
    }

    void SaveSettings()
    {
        try { Store.SaveSettings(settings); } catch { /* next change retries */ }
    }

    // ---------- every frame ----------
    void Frame()
    {
        double t = clock.Elapsed.TotalSeconds;
        float dt = (float)Math.Min(.1, t - lastT);
        lastT = t;
        if (engine == null) return;
        bool was = engine.Running;
        engine.Update(cur, dt);
        if (was != engine.Running) UpdatePower();
        InMeter.Width = 120 * Math.Clamp(engine.InputLevel, 0, 1);
        OutMeter.Width = 120 * Math.Clamp(engine.OutputLevel, 0, 1);
        OutMeter.Background = Res<Brush>(engine.OutputLevel > 1 ? "LiveBrush" : "NeonBrush");
        Room.InvalidateVisual();
        StatusText.Text = t < noteUntil ? note : engine.Status;
    }

    void Status(string s) { note = s; noteUntil = clock.Elapsed.TotalSeconds + 8; StatusText.Text = s; }
    void Flash(string s) => Status(s);

    // ---------- layout ----------
    void Reflow()
    {
        bool narrow = ActualWidth < 1000;
        RoomCol.Width = new GridLength(1, GridUnitType.Star);
        InspCol.Width = narrow ? new GridLength(0) : new GridLength(ActualWidth > 1500 ? 460 : 400);
        RoomRow.Height = new GridLength(narrow ? .5 : 1, GridUnitType.Star);
        InspRow.Height = narrow ? new GridLength(.5, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(InspBorder, narrow ? 1 : 0);
        Grid.SetColumn(InspBorder, narrow ? 0 : 1);
        InspBorder.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(12, 0, 0, 0);
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    void DarkTitleBar()
    {
        int on = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    void Shutdown()
    {
        SaveNow();
        settings.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal) { settings.Width = Width; settings.Height = Height; }
        SaveSettings();
        frame.Stop();
        engine?.Dispose();
    }
}
