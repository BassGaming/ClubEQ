using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ClubEQ.Model;

// Copies the CATCAR Radio mod's current garage setup into a preset: per radio its volume knob and Underground
// switch + amount, the corner speakers' measured positions, portable radios in the garage, and where the garage radio
// stands (from the game log). Files: BepInEx\config\catcar.radio.state.txt (the host's saved state, written by the mod's
// RadioState) and BepInEx\LogOutput.log. The player's own Volume setting is not copied: it scales every radio alike and
// ClubEQ's master does that job.
public static class RadioImport
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const char US = '\u001f', RS = '\u001e', GS = '\u001d';
    // Garage middle in world space when the log doesn't say (mod log "Corner speakers: ... from the middle (...)").
    static readonly (float X, float Y, float Z) DefaultMid = (-7.81f, -.15f, -345.32f);

    // ...\CATCAR Pit Crew Playtest\BepInEx from the Steam libraries, or null.
    public static string FindBepInEx()
    {
        var roots = new List<string> { @"C:\Program Files (x86)\Steam" };
        try
        {
            var vdf = Path.Combine(roots[0], "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    roots.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        catch { /* fall through to the default library */ }
        foreach (var r in roots)
        {
            var d = Path.Combine(r, "steamapps", "common", "CATCAR Pit Crew Playtest", "BepInEx");
            if (File.Exists(Path.Combine(d, "config", "catcar.radio.state.txt"))) return d;
        }
        return null;
    }

    static float F(string s) => float.Parse(s, Inv);

    static (float X, float Y, float Z)? LastVector(string log, string pattern)
    {
        var ms = Regex.Matches(log, pattern + @"\((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)");
        if (ms.Count == 0) return null;
        var g = ms[^1].Groups;
        return (F(g[1].Value), F(g[2].Value), F(g[3].Value));
    }

    // Returns a one-line summary of what changed. Throws if the state file is missing or unreadable.
    public static string Apply(Preset p, string bepinex)
    {
        var lines = File.ReadAllLines(Path.Combine(bepinex, "config", "catcar.radio.state.txt"));
        string log = "";
        foreach (var name in new[] { "LogOutput.log", "LogOutput.1.log" })
            try { log += File.ReadAllText(Path.Combine(bepinex, name)); } catch { /* the game may hold it open or it may not exist */ }
        var mid = LastVector(log, @"Corner speakers: walls at [^\n]* from the middle ") ?? DefaultMid;
        var ug = p.Macros.FirstOrDefault(m => m.Name == "Underground");
        var done = new List<string>();

        void Use(Speaker s, bool muffle, float amt)
        {
            if (ug != null) s.Macros[ug.Name] = new SpeakerMacro { Own = true, On = muffle, Amount = Math.Clamp(amt, 0, 1) };
        }

        // S<tab>index<tab>vol<tab>muffle<tab>amt<tab>link<tab>repeat<tab>shuffle; station 0 = garage radio, 1/2 = the cars
        var cars = p.Speakers.Where(s => s.Kind == SpeakerKind.Car).OrderBy(s => s.Name).ToList();
        foreach (var f in lines.Select(l => l.Split('\t')).Where(f => f[0] == "S" && f.Length >= 6))
        {
            int st = int.Parse(f[1]);
            float vol = F(f[2]), amt = F(f[4]);
            bool muffle = f[3] == "1";
            if (st == 0 && p.Speakers.FirstOrDefault(s => s.Kind == SpeakerKind.Boombox && !s.Name.StartsWith("Portable")) is { } box)
            {
                box.P["Volume"] = vol; Use(box, muffle, amt);
                if (LastVector(log, @"Radio built on [^\n]* at ") is { } at) { box.X = at.X - mid.X; box.Z = at.Z - mid.Z; box.Y = .75f; }
                done.Add($"garage radio {vol * 100:0} %{(muffle ? $", Underground {amt * 100:0} %" : "")}");
            }
            else if (st is 1 or 2 && st - 1 < cars.Count)
            {
                var car = cars[st - 1];
                car.P["Volume"] = vol * 1.3f; // the mod plays car radios 1.3x (the body's low-pass eats loudness)
                car.Mute = f[5] != "0";       // only a car synced to the garage plays the same music; its own playlist isn't ClubEQ's input
                done.Add($"{car.Name} {(car.Mute ? "muted (own playlist)" : $"{vol * 100:0} %")}");
            }
        }

        // P<tab> + the mod's P message: P US spotStation US spotVol US portables (RS between, GS between fields:
        // id, owner, x, y, z, yaw, station, vol, muffle, amt, scene). Ids -1..-4 = the corner speakers.
        var pl = lines.FirstOrDefault(l => l.StartsWith("P\t"));
        if (pl != null)
        {
            var parts = pl[2..].Split(US);
            p.Speakers.RemoveAll(s => s.Name.StartsWith("Portable "));
            int corners = 0, portables = 0;
            foreach (var e in (parts.Length > 3 ? parts[3] : "").Split(RS, StringSplitOptions.RemoveEmptyEntries))
            {
                var x = e.Split(GS);
                if (x.Length < 11 || x[10] != "Hub") continue; // garage only
                int id = int.Parse(x[0]);
                float px = F(x[2]) - mid.X, py = F(x[3]) - mid.Y, pz = F(x[4]) - mid.Z, yaw = F(x[5]), vol = F(x[7]), amt = F(x[9]);
                bool muffle = x[8] == "1";
                Speaker s;
                if (id < 0)
                {
                    string name = "Corner " + -id;
                    s = p.Speakers.FirstOrDefault(q => q.Name == name);
                    if (s == null) p.Speakers.Add(s = Builtin.Corner(name, px, pz));
                    corners++;
                }
                else
                {
                    if (int.Parse(x[6]) != 0) continue; // a portable on a car's playlist plays other music
                    p.Speakers.Add(s = Builtin.Portable("Portable " + id, px, pz));
                    portables++;
                }
                s.X = px; s.Y = id < 0 ? py : Math.Max(py, 0) + .3f; s.Z = pz; s.Yaw = ((yaw + 180) % 360) - 180;
                s.P["Volume"] = vol;
                Use(s, muffle, amt);
            }
            done.Add($"{corners} corner speakers" + (portables > 0 ? $", {portables} portables" : ""));
        }
        if (ug != null) ug.On = false; // every speaker has its own switch now, as in the mod
        return "Imported from CATCAR: " + string.Join("; ", done) + ".";
    }
}
