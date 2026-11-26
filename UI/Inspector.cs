using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClubEQ.Model;
using static ClubEQ.UI.Ui;

namespace ClubEQ.UI;

// The right-hand panel: settings of whatever is selected (a speaker, a macro) or, with nothing selected, you, the room,
// its reverb and the preset. Built in code from the model and the effect registry, so new effects show up by themselves.
sealed class Inspector
{
    readonly StackPanel host;
    readonly Action changed;   // a value changed: save soon, nothing to rebuild
    readonly Action reshaped;  // speakers or effects added/removed/moved: rebuild the inspector and the room
    public Func<Preset> Preset;
    public Action<object> Select;
    public Action<string> PresetAction; // "save as", "rename", "delete", "export", "import", "reset"

    public Inspector(StackPanel host, Action changed, Action reshaped)
    {
        this.host = host; this.changed = changed; this.reshaped = reshaped;
    }

    void Add(UIElement e) => host.Children.Add(e);

    public void Show(object sel)
    {
        host.Children.Clear();
        switch (sel)
        {
            case Speaker s: ShowSpeaker(s); break;
            case Macro m: ShowMacro(m); break;
            default: ShowRoom(); break;
        }
    }

    // ---------- speaker ----------
    void ShowSpeaker(Speaker s)
    {
        var p = Preset();
        Add(new TextBlock { Text = s.Name, Style = Res<Style>("H3") });
        Add(new TextBlock { Text = s.Kind + (s.Inside != null ? " · has an inside zone" : ""), Style = Res<Style>("Small") });
        Add(Wrap(
            Pill("Mute", s.Mute, v => { s.Mute = v; changed(); }),
            Pill("Solo", s.Solo, v => { s.Solo = v; changed(); }),
            Button("Duplicate", () =>
            {
                var c = Store.FromJson<Speaker>(Store.ToJson(s));
                c.Name = s.Name + " copy"; c.X += .8f; c.Z -= .8f; c.Solo = false;
                p.Speakers.Insert(p.Speakers.IndexOf(s) + 1, c);
                reshaped(); Select(c);
            }),
            Button("Remove", () => { p.Speakers.Remove(s); reshaped(); Select(null); }, "BtnDanger")));

        foreach (var m in p.Macros.Where(m => m.Targets.Any(t => t.Matches(s))))
            ShowSpeakerMacro(s, m);

        Add(Heading("Speaker"));
        Add(Text("Name", () => s.Name, v => s.Name = v.Trim().Length > 0 ? v.Trim() : s.Name, changed));
        Add(Choice("Kind", Enum.GetNames<SpeakerKind>(), () => (int)s.Kind, v => s.Kind = (SpeakerKind)v, () => { changed(); reshaped(); }));
        foreach (var d in Effects.SpeakerParams)
            Add(Slider(d.Label, d, () => s.Get(d.Name), v => s.P[d.Name] = v, changed));

        Add(Heading("Position"));
        var r = p.Room;
        Add(Slider("X (right)", new ParamDef("X", "X", r.MinX - 2, r.MaxX + 2, 0, "m"), () => s.X, v => s.X = v, changed));
        Add(Slider("Z (forward)", new ParamDef("Z", "Z", r.MinZ - 2, r.MaxZ + 2, 0, "m"), () => s.Z, v => s.Z = v, changed));
        Add(Slider("Height", new ParamDef("Y", "Height", 0, Math.Max(4, r.Height), 1, "m"), () => s.Y, v => s.Y = v, changed));
        Add(Slider("Facing", new ParamDef("Yaw", "Facing", -180, 180, 0, "°"), () => s.Yaw, v => s.Yaw = v, changed));
        Add(Note("Drag it in the room; the mouse wheel turns it (Shift = finer)."));

        Add(Heading("Effects (in signal order)"));
        for (int i = 0; i < s.Chain.Count; i++) Add(EffectCard(s, i));
        var add = new ComboBox { ItemsSource = Effects.All.Select(e => "+ " + e.Label).ToList(), Text = "+ Add effect", IsEditable = false };
        add.SelectionChanged += (_, _) =>
        {
            if (add.SelectedIndex < 0) return;
            s.Chain.Add(new Effect { Type = Effects.All[add.SelectedIndex].Type });
            reshaped(); Select(s);
        };
        Add(Note("Add an effect:"));
        Add(add);

        Add(Heading("Inside zone"));
        if (s.Inside == null)
        {
            Add(Note("While you stand inside the zone, chosen values replace the normal ones (the car: muffled from outside, open inside)."));
            Add(Button("Add inside zone", () => { s.Inside = new InsideProfile { Values = { ["Level3D"] = .25f } }; reshaped(); Select(s); }));
        }
        else ShowInside(s);
    }

    // Follow the strip, or this speaker's own on/off and amount (as each speaker in the mod has its own Underground).
    void ShowSpeakerMacro(Speaker s, Macro m)
    {
        Add(Heading(m.Name));
        s.Macros.TryGetValue(m.Name, out var u);
        int mode = u == null || !u.Own ? 0 : u.On ? 1 : 2;
        var seg = new StackPanel { Orientation = Orientation.Horizontal };
        string[] labels = { $"Follow strip ({(m.On ? $"on {m.Amount * 100:0} %" : "off")})", "On", "Off" };
        for (int k = 0; k < 3; k++)
        {
            int kk = k;
            var rb = new RadioButton { Content = labels[k], Style = Res<Style>("SegItem"), IsChecked = k == mode, GroupName = "m" + m.GetHashCode() + s.GetHashCode() };
            rb.Checked += (_, _) =>
            {
                if (kk == mode) return;
                if (!s.Macros.TryGetValue(m.Name, out var x)) s.Macros[m.Name] = x = new SpeakerMacro { Amount = m.Amount };
                x.Own = kk != 0; x.On = kk == 1;
                changed(); Select(s);
            };
            seg.Children.Add(rb);
        }
        Add(new Border { Style = Res<Style>("SegBox"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8), Child = seg });
        if (u is { Own: true, On: true })
            Add(Slider("Amount", new ParamDef("Amount", "Amount", 0, 1, .5f), () => u.Amount, v => u.Amount = v, changed));
    }

    FrameworkElement EffectCard(Speaker s, int i)
    {
        var e = s.Chain[i];
        var def = Effects.Get(e.Type);
        var panel = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var title = new TextBlock { Text = def.Label, Style = Res<Style>("H2"), VerticalAlignment = VerticalAlignment.Center };
        var tools = Wrap(
            Pill(e.Bypass ? "Off" : "On", !e.Bypass, v => { e.Bypass = !v; changed(); Select(s); }),
            Button("▲", () => { if (i > 0) { (s.Chain[i - 1], s.Chain[i]) = (s.Chain[i], s.Chain[i - 1]); reshaped(); Select(s); } }, tip: "Earlier in the chain"),
            Button("▼", () => { if (i < s.Chain.Count - 1) { (s.Chain[i + 1], s.Chain[i]) = (s.Chain[i], s.Chain[i + 1]); reshaped(); Select(s); } }, tip: "Later in the chain"),
            Button("✕", () => { s.Chain.RemoveAt(i); reshaped(); Select(s); }, tip: "Remove"));
        DockPanel.SetDock(tools, Dock.Right);
        head.Children.Add(tools); head.Children.Add(title);
        panel.Children.Add(head);
        foreach (var d in def.Params)
            panel.Children.Add(d.IsChoice
                ? Choice(d.Label, d.Choices, () => (int)MathF.Round(e.Get(d.Name)), v => e.P[d.Name] = v, changed)
                : Slider(d.Label, d, () => e.Get(d.Name), v => e.P[d.Name] = v, changed));
        return Card(panel);
    }

    void ShowInside(Speaker s)
    {
        var z = s.Inside;
        Add(Slider("Radius", new ParamDef("R", "Radius", .2f, 6, 1.5f, "m"), () => z.Radius, v => z.Radius = v, changed));
        Add(Slider("Zone right", new ParamDef("OX", "Offset right", -3, 3, 0, "m"), () => z.OffsetX, v => z.OffsetX = v, changed));
        Add(Slider("Zone forward", new ParamDef("OZ", "Offset forward", -3, 3, 0, "m"), () => z.OffsetZ, v => z.OffsetZ = v, changed));
        Add(Note("Values while inside:"));
        foreach (var (key, _) in z.Values.ToList())
        {
            var d = Effects.Find(key);
            if (d == null) continue;
            var row = d.IsChoice
                ? Choice(key, d.Choices, () => (int)z.Values[key], v => z.Values[key] = v, changed)
                : Slider(key, d, () => z.Values[key], v => z.Values[key] = v, changed);
            Add(Card(new StackPanel { Children = { row, Button("Remove", () => { z.Values.Remove(key); reshaped(); Select(s); }) } }));
        }
        var pick = new ComboBox { ItemsSource = Addresses(s).Where(a => !z.Values.ContainsKey(a)).ToList() };
        pick.SelectionChanged += (_, _) =>
        {
            if (pick.SelectedItem is not string a) return;
            z.Values[a] = Mix.Targets(Preset(), s, false).TryGetValue(a, out var v) ? v : Effects.Find(a).Default;
            reshaped(); Select(s);
        };
        Add(Note("Add a value to change inside:"));
        Add(pick);
        Add(Button("Remove inside zone", () => { s.Inside = null; reshaped(); Select(s); }, "BtnDanger"));
    }

    // the parameters this speaker has: its own plus those of the effects in its chain
    static IEnumerable<string> Addresses(Speaker s) =>
        Effects.SpeakerParams.Select(p => p.Name).Concat(s.Chain.Select(e => e.Type).Distinct().SelectMany(t => Effects.Get(t).Params.Select(p => t + "." + p.Name)));

    // ---------- macro ----------
    void ShowMacro(Macro m)
    {
        var p = Preset();
        Add(new TextBlock { Text = m.Name, Style = Res<Style>("H3") });
        Add(Note("One slider driving many settings. Each line: which speakers, which setting, the value at 0 % and at 100 % of the slider."));
        Add(Wrap(Button("Done", () => Select(null), "BtnPrimary"), Button("Delete macro", () => { p.Macros.Remove(m); reshaped(); Select(null); }, "BtnDanger")));
        Add(Text("Name", () => m.Name, v =>
        {
            string old = m.Name, name = v.Trim().Length > 0 ? v.Trim() : m.Name;
            foreach (var sp in p.Speakers)
                if (sp.Macros.Remove(old, out var u)) sp.Macros[name] = u; // speakers keep their own setting
            m.Name = name;
        }, () => { changed(); reshaped(); }));
        Add(Heading("Targets"));
        var addresses = Effects.Addresses().ToArray();
        foreach (var t in m.Targets.ToList())
        {
            var d = Effects.Find(t.Param) ?? Effects.SpeakerParams[0];
            var st = new StackPanel();
            st.Children.Add(Text("Speakers", () => t.Speakers, v => t.Speakers = v, changed));
            st.Children.Add(Choice("Setting", addresses, () => Array.IndexOf(addresses, t.Param), v => t.Param = addresses[v], () => { changed(); reshaped(); Select(m); }));
            st.Children.Add(Choice("Mode", new[] { "Set the value", "Multiply its value" }, () => t.Multiply ? 1 : 0, v => t.Multiply = v == 1, () => { changed(); reshaped(); Select(m); }));
            var range = t.Multiply ? new ParamDef("x", "x", 0, 4, 1) : d;
            if (range.IsChoice)
            {
                st.Children.Add(Choice("At 0 %", range.Choices, () => (int)t.From, v => t.From = v, changed));
                st.Children.Add(Choice("At 100 %", range.Choices, () => (int)t.To, v => t.To = v, changed));
            }
            else
            {
                st.Children.Add(Slider("At 0 %", range, () => t.From, v => t.From = v, changed));
                st.Children.Add(Slider("At 100 %", range, () => t.To, v => t.To = v, changed));
                st.Children.Add(Choice("Curve", Enum.GetNames<Curve>(), () => (int)t.Curve, v => t.Curve = (Curve)v, changed));
            }
            st.Children.Add(Button("Remove target", () => { m.Targets.Remove(t); reshaped(); Select(m); }));
            Add(Card(st));
        }
        Add(Button("+ Add target", () => { m.Targets.Add(new MacroTarget { Param = "Volume", From = 1, To = 1, Multiply = true }); reshaped(); Select(m); }));
        Add(Note("Speakers: * = all, or names and kinds separated by commas, e.g. \"Car 1, kind:Cabinet\". Kinds: " + string.Join(", ", Enum.GetNames<SpeakerKind>()) + "."));
    }

    // ---------- room, you, preset ----------
    void ShowRoom()
    {
        var p = Preset();
        var r = p.Room;
        Add(new TextBlock { Text = p.Name, Style = Res<Style>("H3") });
        Add(Note("Click a speaker in the room to edit it. Drag \"YOU\" to move around, the wheel turns you."));
        Add(Wrap(
            Button("Save", () => PresetAction("save")), Button("Save as…", () => PresetAction("save as")), Button("Rename", () => PresetAction("rename")),
            Button("Export", () => PresetAction("export")), Button("Import", () => PresetAction("import")),
            Button("Reset", () => PresetAction("reset"), tip: "Back to the built-in version (built-in presets only)"),
            Button("Delete", () => PresetAction("delete"), "BtnDanger")));
        Add(Button("Import from CATCAR Radio", () => PresetAction("catcar"), "BtnPrimary",
            "Copy the Radio mod's current garage setup: each radio's volume and Underground, the corner speakers, portables, where the garage radio stands"));

        Add(Heading("Add a speaker"));
        Add(Wrap(Enum.GetValues<SpeakerKind>().Select(k => (UIElement)Button("+ " + k, () => AddSpeaker(k))).ToArray()));

        Add(Heading("You"));
        Add(Slider("X (right)", new ParamDef("X", "X", r.MinX, r.MaxX, 0, "m"), () => r.ListenerX, v => r.ListenerX = v, changed));
        Add(Slider("Z (forward)", new ParamDef("Z", "Z", r.MinZ, r.MaxZ, 0, "m"), () => r.ListenerZ, v => r.ListenerZ = v, changed));
        Add(Slider("Ear height", new ParamDef("Y", "Ear height", .3f, 3, 1.6f, "m"), () => r.ListenerY, v => r.ListenerY = v, changed));
        Add(Slider("Facing", new ParamDef("Yaw", "Facing", -180, 180, 0, "°"), () => r.ListenerYaw, v => r.ListenerYaw = v, changed));
        Add(Slider("Front/back cue", new ParamDef("RearShade", "Front/back cue", 0, 12, 6, "dB"), () => r.RearShade, v => r.RearShade = v, changed));
        Add(Note("Speakers behind you lose some treble, as your ears do in a real room. The game has no such cue: 0 dB sounds exactly like CATCAR."));

        Add(Heading("Room reverb"));
        var rv = p.Reverb;
        Add(Switch("On", () => rv.On, v => rv.On = v, changed));
        Add(Choice("Load preset", Effects.ReverbNames, () => -1, i =>
        {
            var v = Effects.ReverbPresets[i].V;
            (rv.DecayTime, rv.EarlyDelay, rv.LateDelay, rv.HFReference, rv.HFDecayRatio, rv.Diffusion, rv.Density, rv.LowShelfFrequency, rv.LowShelfGain, rv.HighCut, rv.EarlyLateMix, rv.WetLevel)
                = (v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11]);
        }, () => { changed(); Select(null); }));
        void R(string label, float min, float max, string unit, Func<float> g, Action<float> s, bool log = false) =>
            Add(Slider(label, new ParamDef(label, label, min, max, g(), unit, log), g, s, changed));
        R("Decay", 100, 20000, "ms", () => rv.DecayTime, v => rv.DecayTime = v, true);
        R("Early delay", 0, 300, "ms", () => rv.EarlyDelay, v => rv.EarlyDelay = v);
        R("Late delay", 0, 100, "ms", () => rv.LateDelay, v => rv.LateDelay = v);
        R("HF reference", 20, 20000, "Hz", () => rv.HFReference, v => rv.HFReference = v, true);
        R("HF decay", 10, 100, "%", () => rv.HFDecayRatio, v => rv.HFDecayRatio = v);
        R("Diffusion", 0, 100, "%", () => rv.Diffusion, v => rv.Diffusion = v);
        R("Density", 0, 100, "%", () => rv.Density, v => rv.Density = v);
        R("Low shelf", 20, 1000, "Hz", () => rv.LowShelfFrequency, v => rv.LowShelfFrequency = v, true);
        R("Low shelf gain", -36, 12, "dB", () => rv.LowShelfGain, v => rv.LowShelfGain = v);
        R("High cut", 20, 20000, "Hz", () => rv.HighCut, v => rv.HighCut = v, true);
        R("Early / late", 0, 100, "%", () => rv.EarlyLateMix, v => rv.EarlyLateMix = v);
        R("Wet level", -80, 20, "dB", () => rv.WetLevel, v => rv.WetLevel = v);

        Add(Heading("Room size"));
        Add(Slider("Left wall", new ParamDef("a", "", -30, 0, -5, "m"), () => r.MinX, v => r.MinX = Math.Min(v, r.MaxX - 1), changed));
        Add(Slider("Right wall", new ParamDef("b", "", 0, 30, 5, "m"), () => r.MaxX, v => r.MaxX = Math.Max(v, r.MinX + 1), changed));
        Add(Slider("Back wall", new ParamDef("c", "", -30, 0, -5, "m"), () => r.MinZ, v => r.MinZ = Math.Min(v, r.MaxZ - 1), changed));
        Add(Slider("Front wall", new ParamDef("d", "", 0, 30, 5, "m"), () => r.MaxZ, v => r.MaxZ = Math.Max(v, r.MinZ + 1), changed));
        Add(Slider("Ceiling", new ParamDef("e", "", 2, 20, 3, "m"), () => r.Height, v => r.Height = v, changed));
    }

    void AddSpeaker(SpeakerKind k)
    {
        var p = Preset();
        var r = p.Room;
        var s = new Speaker
        {
            Name = k + " " + (p.Speakers.Count(x => x.Kind == k) + 1), Kind = k,
            X = (r.MinX + r.MaxX) / 2, Z = (r.MinZ + r.MaxZ) / 2, Y = k == SpeakerKind.Cabinet ? 2.5f : k == SpeakerKind.Car ? .9f : 1,
        };
        s.Chain.Add(new Effect { Type = "HighPass", P = { ["Cutoff"] = k == SpeakerKind.Car ? 20 : 60 } });
        s.Chain.Add(new Effect { Type = "LowPass", P = { ["Cutoff"] = k == SpeakerKind.Car ? 500 : 14000 } });
        if (k == SpeakerKind.Car)
            s.Inside = new InsideProfile { OffsetX = -.35f, OffsetZ = -.2f, Values = { ["LowPass.Cutoff"] = 22000, ["Level3D"] = .25f } };
        else s.Chain.Add(new Effect { Type = "Reverb" });
        p.Speakers.Add(s);
        reshaped(); Select(s);
    }
}
