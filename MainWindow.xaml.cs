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
    Voicemeeter vm; // the Voicemeeter insert, while it plays
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

        CreateEngine();

        PresetBox.SelectionChanged += (_, _) => { if (!loading && PresetBox.SelectedIndex >= 0) { SaveNow(); LoadPreset(presets[PresetBox.SelectedIndex]); } };
        InputBox.SelectionChanged += (_, _) => { if (loading) return; settings.Input = InputBox.SelectedItem as string ?? ""; SaveSettings(); if (engine?.Running == true) Start(); };
        OutputBox.SelectionChanged += (_, _) => { if (loading) return; settings.Output = OutputBox.SelectedItem as string ?? ""; SaveSettings(); if (engine?.Running == true) Start(); else SetOutput(); };
        MasterSlider.ValueChanged += (_, _) => { MasterLabel.Text = MasterText(MasterSlider.Value); if (loading) return; cur.Master = FromDb(MasterSlider.Value); Changed(); };
        MasterLabel.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) MasterSlider.Value = 0; };
        LimiterBox.Click += (_, _) => { cur.Limiter = LimiterBox.IsChecked == true; Changed(); };
        ClipChip.MouseLeftButtonDown += (_, _) => clipped = limited = false;
        EqButton.Click += (_, _) => EqPanel.Visibility = EqButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        EqOn.Click += (_, _) => { cur.Eq.On = EqOn.IsChecked == true; Changed(); };
        EqFlat.Click += (_, _) => { foreach (var sl in eqSliders) sl.Value = 0; };
        BuildEq();
        PowerButton.Click += (_, _) => { if (engine?.Running == true) { StopAudio(); UpdatePower(); } else Start(); };
        SettingsButton.Click += (_, _) => SettingsDialog.Show(this, settings, ApplySettings);
        HelpButton.Click += (_, _) => Help.Show(this);

        saveSoon.Tick += (_, _) => SaveNow();
        frame.Tick += (_, _) => Frame();
        SizeChanged += (_, _) => Reflow();
        SourceInitialized += (_, _) => DarkTitleBar();
        Closing += (_, _) => Shutdown();
        Loaded += (_, _) =>
        {
            frame.Start();
            if (engine != null && (settings.Voicemeeter || settings.Input.Length > 0)) Start(); // used before: play right away
        };
    }

    // ---------- engine, devices ----------
    // The audio path and latency come from the settings; changing them builds a new engine.
    void CreateEngine()
    {
        StopAudio();
        engine?.Dispose(); engine = null;
        try
        {
            engine = settings.Voicemeeter
                ? new Engine(FMOD.OUTPUTTYPE.NOSOUND_NRT, (uint)settings.BufferLength, 2, settings.GapMs) // Voicemeeter's thread mixes
                : new Engine(FMOD.OUTPUTTYPE.AUTODETECT, (uint)settings.BufferLength, settings.BufferCount, settings.GapMs);
        }
        catch (Exception e) { Status("Audio engine failed: " + e.Message + " (is fmod.dll next to ClubEQ.exe?)"); }
        InputBox.IsEnabled = OutputBox.IsEnabled = !settings.Voicemeeter; // the insert uses neither
        FillDevices();
    }

    void ApplySettings()
    {
        bool was = engine?.Running == true;
        SaveSettings();
        CreateEngine();
        if (was) Start();
        UpdatePower();
    }

    void StopAudio()
    {
        vm?.Dispose(); vm = null; // first: its thread uses the engine
        engine?.Stop();
    }

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
            SaveSettings(); // now, not only on close: a stopped process never closes
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
        if (settings.Voicemeeter)
        {
            StopAudio();
            var strips = Voicemeeter.Strips();
            int s = settings.VoicemeeterStrip >= 0 ? settings.VoicemeeterStrip : Voicemeeter.DefaultStrip(strips);
            try { vm = new Voicemeeter(engine, s, s < strips.Count ? strips[s] : $"{s + 1}"); }
            catch (Exception e) { engine.Stop(); Status("Voicemeeter: " + e.Message); }
            UpdatePower();
            return;
        }
        if (InputBox.SelectedIndex < 0) { Status("Pick an input device first."); return; }
        // Recording a device's loopback while playing into that same device records ClubEQ's own output again and
        // again: every sound smeared with repeats, no direction left. Refuse it.
        string i = (InputBox.SelectedItem as string ?? "").Replace("[loopback]", "").Trim(), o = OutputBox.SelectedItem as string ?? "";
        if (i.Length > 0 && i.Equals(o.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            if (engine.Running) engine.Stop();
            Status($"Output is the same device ClubEQ records ({o}): it would hear itself. Pick your headphones or another Voicemeeter input as Output.");
            UpdatePower();
            return;
        }
        try
        {
            SetOutput();
            engine.Start(InputBox.SelectedIndex);
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
        MasterSlider.Value = ToDb(p.Master);
        MasterLabel.Text = MasterText(MasterSlider.Value);
        LimiterBox.IsChecked = p.Limiter;
        p.Eq ??= new MasterEq();
        if (p.Eq.Gains == null || p.Eq.Gains.Length != MasterEq.Freqs.Length) p.Eq.Gains = new float[MasterEq.Freqs.Length];
        EqOn.IsChecked = p.Eq.On;
        for (int i = 0; i < eqSliders.Count; i++) eqSliders[i].Value = p.Eq.Gains[i];
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
            case "save":
                try { Store.SavePreset(cur); Flash($"Saved \"{cur.Name}\"."); } catch (Exception e) { Flash("Saving failed: " + e.Message); }
                break;
            case "save as":
                {
                    var name = Prompt.Ask(this, "Save as a preset", cur.Name + " 2")?.Trim();
                    if (string.IsNullOrWhiteSpace(name)) return;
                    // file names ignore case on Windows, so "garage" would overwrite Garage.json
                    var old = presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (ReferenceEquals(old, cur)) { PresetAction("save"); return; }
                    if (old != null)
                    {
                        if (MessageBox.Show(this, $"A preset \"{old.Name}\" exists. Overwrite it?", "ClubEQ", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                        Store.DeletePreset(old.Name);
                    }
                    var copy = Store.FromJson<Preset>(Store.ToJson(cur));
                    copy.Name = name;
                    int at = old != null ? presets.IndexOf(old) : presets.Count;
                    if (old != null) presets[at] = copy; else presets.Add(copy);
                    Store.SavePreset(copy); LoadPreset(copy);
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
            case "catcar":
                {
                    string dir = RadioImport.Normalize(settings.CatcarBepInEx) ?? RadioImport.FindBepInEx();
                    if (dir == null)
                    {
                        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Pick the CATCAR Pit Crew Playtest game folder" };
                        if (d.ShowDialog(this) != true) return;
                        dir = RadioImport.Normalize(d.FolderName);
                        if (dir == null) { Flash($"No Radio settings (BepInEx\\config\\catcar.radio.state.txt) under {d.FolderName}."); return; }
                    }
                    try
                    {
                        Flash(RadioImport.Apply(cur, dir));
                        settings.CatcarBepInEx = dir; SaveSettings();
                        Reshaped(); Select(null);
                    }
                    catch (Exception e) { Flash("Import failed: " + e.Message); }
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
        while (presets.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase) && !ReferenceEquals(p, cur))) n = $"{name} {i++}";
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
        try { Store.SaveSettings(settings); }
        catch (Exception e) { Status($"Saving settings to {Store.Home} failed: {e.Message}"); } // next change retries
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
        Meters(engine.OutputLR, dt);
        Room.InvalidateVisual();
        StatusText.Text = t < noteUntil ? note : vm?.Note ?? engine.Status;
    }

    // ---------- master EQ ----------
    readonly List<Slider> eqSliders = new();

    void BuildEq()
    {
        for (int i = 0; i < MasterEq.Freqs.Length; i++)
        {
            int band = i;
            float f = MasterEq.Freqs[i];
            var value = new TextBlock { Text = "0", Style = Res<Style>("Small"), HorizontalAlignment = HorizontalAlignment.Center };
            var sl = new Slider { Style = Res<Style>("VSlider"), Minimum = -12, Maximum = 12, Value = 0, Height = 110, SmallChange = .5, LargeChange = 3, ToolTip = $"{FreqText(f)}Hz" };
            sl.ValueChanged += (_, _) =>
            {
                value.Text = sl.Value.ToString("+0.0;-0.0;0");
                if (loading) return;
                cur.Eq.Gains[band] = (float)Math.Round(sl.Value, 1);
                Changed();
            };
            sl.MouseDoubleClick += (_, _) => sl.Value = 0;
            eqSliders.Add(sl);
            var col = new StackPanel { Width = 46 };
            col.Children.Add(value);
            col.Children.Add(sl);
            col.Children.Add(new TextBlock { Text = FreqText(f), Style = Res<Style>("Label"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            EqBands.Children.Add(col);
        }
    }

    static string FreqText(float f) => f >= 1000 ? $"{f / 1000:0.#}k" : $"{f:0}";

    // ---------- master and output meters ----------
    const double MinDb = -40, MeterLo = -48, MeterHi = 6, MeterW = 150;
    static double ToDb(float m) => m <= 0 ? MinDb : Math.Max(MinDb, 20 * Math.Log10(m));
    static float FromDb(double d) => d <= MinDb ? 0 : (float)Math.Pow(10, d / 20);
    static string MasterText(double d) => d <= MinDb ? "MASTER OFF" : $"MASTER {d:+0.0;-0.0;0.0} dB";
    static double Db(float lin) => lin > 0 ? 20 * Math.Log10(lin) : -200;
    static double MeterX(double db) => MeterW * Math.Clamp((db - MeterLo) / (MeterHi - MeterLo), 0, 1);

    readonly double[] hold = { -200, -200 }, holdAge = { 0, 0 };
    bool clipped, limited;
    static readonly Brush ChipOff = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x26, 0x5C)));
    static Brush Freeze(Brush b) { b.Freeze(); return b; }

    void Meters((float L, float R) lr, float dt)
    {
        double[] now = { Db(lr.L), Db(lr.R) };
        var bars = new[] { OutL, OutR }; var ticks = new[] { HoldL, HoldR };
        for (int c = 0; c < 2; c++)
        {
            // peak hold: 1.5 s, then falls at 20 dB/s
            holdAge[c] += dt;
            if (now[c] >= hold[c]) { hold[c] = now[c]; holdAge[c] = 0; }
            else if (holdAge[c] > 1.5) hold[c] = Math.Max(now[c], hold[c] - 20 * dt);
            bars[c].Width = MeterX(now[c]);
            bars[c].Background = Res<Brush>(now[c] > 0 ? "LiveBrush" : now[c] > -1 ? "WarnBrush" : "NeonBrush");
            ticks[c].Margin = new Thickness(Math.Max(0, MeterX(hold[c]) - 2), 0, 0, 0);
            ticks[c].Visibility = hold[c] > MeterLo ? Visibility.Visible : Visibility.Collapsed;
        }
        double peak = Math.Max(hold[0], hold[1]);
        PeakText.Text = peak > -100 ? $"PEAK {peak:+0.0;-0.0;0.0} dB" : "IN / OUT";
        // before the limiter: over 0 dBFS clips without it; with it, over its -1 dB ceiling it is turning the sound down
        if (Math.Max(now[0], now[1]) > (cur.Limiter ? -1 : 0)) { if (cur.Limiter) limited = true; else clipped = true; }
        ClipText.Text = clipped ? "CLIP" : limited ? "LIMIT" : "CLEAN";
        ClipChip.Background = clipped ? Res<Brush>("LiveBrush") : limited ? Res<Brush>("WarnBrush") : ChipOff;
        ClipText.Foreground = Res<Brush>(clipped || limited ? "TextBrush" : "MutedBrush");
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
        StopAudio();
        engine?.Dispose();
    }
}
