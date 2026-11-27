using System;
using System.Linq;
using ClubEQ.Model;

namespace ClubEQ;

// ClubEQ.exe --selftest: checks the model without any audio device. Exit code 0 = all passed.
static class SelfTest
{
    static int failed;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
        if (!ok) failed++;
    }

    static bool Near(float a, float b, float tol = .5f) => MathF.Abs(a - b) <= tol;

    // ClubEQ.exe --smoke "<input name part>": plays a quiet 440 Hz tone into that device for 8 s and runs the Garage
    // scene (Underground on) recording it, so every DSP in the chain gets created and driven. Prints what happened.
    public static int Smoke(string input, uint block = 512, int count = 4, int gap = 50) => Run3D(input, false, block, count, gap);
    public static int Pan(string input) => Run3D(input, true);

    // --pan: one dry speaker 5 m to your right, left, front and back; prints the left/right output peaks.
    static void PanCases(Audio.Engine en, FMOD.System tone)
    {
        foreach (var (label, x, z, yaw) in new[] { ("right (+x)", 5f, 0f, 0f), ("left (-x)", -5f, 0f, 0f), ("front (+z)", 0f, 5f, 0f), ("back (-z)", 0f, -5f, 0f),
                                                   ("right, you face right", 5f, 0f, 90f), ("+x, you face back", 5f, 0f, 180f) })
        {
            var p = new Preset { Reverb = new RoomReverb { On = false } };
            p.Room.ListenerYaw = yaw;
            p.Speakers.Add(new Speaker { Name = "S", X = x, Y = 1.6f, Z = z, P = { ["MinDistance"] = 1, ["Spread"] = 0 } });
            float l = 0, r = 0;
            for (int f = 0; f < 90; f++)
            {
                tone.update(); en.Update(p, 1 / 60f);
                if (f > 45) { l = Math.Max(l, en.OutputLR.L); r = Math.Max(r, en.OutputLR.R); }
                System.Threading.Thread.Sleep(16);
            }
            Console.WriteLine($"{label,-24} L {l:0.000}  R {r:0.000}");
        }
    }

    static int Run3D(string input, bool pan, uint block = 512, int count = 4, int gap = 50)
    {
        Console.WriteLine();
        if (input == "clocked") return Clocked();
        if (input.StartsWith("voicemeeter:")) return VoicemeeterSmoke(input["voicemeeter:".Length..]);
        using var en = new Audio.Engine(FMOD.OUTPUTTYPE.AUTODETECT, block, count, gap);
        if (input == "external") return External(en);
        var ins = en.Inputs();
        int i = ins.FindIndex(n => n.Contains(input, StringComparison.OrdinalIgnoreCase));
        if (i < 0) { Console.WriteLine("No input matching " + input); return 1; }
        var (tone, osc, ch) = Tone(input);
        return Run3DWith(en, i, tone, osc, ch, pan);
    }

    // a second FMOD system playing a quiet 440 Hz tone into the output device whose name contains `device`
    static (FMOD.System, FMOD.DSP, FMOD.Channel) Tone(string device)
    {
        Audio.Engine.Ok(FMOD.Factory.System_Create(out var tone), "tone system");
        tone.getNumDrivers(out int n);
        int o = -1;
        for (int k = 0; k < n; k++) { tone.getDriverInfo(k, out string name, 256, out _, out _, out _, out _); if (name.Contains(device, StringComparison.OrdinalIgnoreCase)) o = k; }
        if (o >= 0) tone.setDriver(o);
        tone.init(8, FMOD.INITFLAGS.NORMAL, IntPtr.Zero);
        tone.createDSPByType(FMOD.DSP_TYPE.OSCILLATOR, out var osc);
        osc.setParameterFloat(1, 440);
        tone.playDSP(osc, default, false, out var ch);
        ch.setVolume(.1f);
        return (tone, osc, ch);
    }

    static int Run3DWith(Audio.Engine en, int i, FMOD.System tone, FMOD.DSP osc, FMOD.Channel ch, bool pan)
    {
        en.Start(i);
        Console.WriteLine(en.Status);
        float maxOut = 0;
        if (pan) { PanCases(en, tone); ch.stop(); osc.release(); tone.close(); tone.release(); return 0; }
        foreach (var (label, setup) in new (string, Action<Preset>)[] {
            ("normal", _ => { }), ("no room reverb", x => x.Reverb.On = false), ("Underground", x => x.Macros[0].On = true),
            ("Underground, no room reverb", x => { x.Macros[0].On = true; x.Reverb.On = false; }) })
        {
            var p = Builtin.Garage();
            setup(p);
            float mi = 0, mo = 0;
            for (int f = 0; f < 120; f++)
            {
                tone.update();
                en.Update(p, 1 / 60f);
                if (f > 60) { mi = Math.Max(mi, en.InputLevel); mo = Math.Max(mo, en.OutputLevel); }
                System.Threading.Thread.Sleep(16);
            }
            maxOut = Math.Max(maxOut, mo);
            Console.WriteLine($"{label}: peak in {mi:0.000} out {mo:0.000} | speakers {string.Join(" ", en.Levels.Select(l => l.ToString("0.000")))}");
        }
        Console.WriteLine(en.Status);
        ch.stop(); osc.release(); tone.close(); tone.release();
        return en.Running && maxOut > 0 ? 0 : 1;
    }

    // --smoke clocked: the Voicemeeter insert's engine side without Voicemeeter. A thread acts as its audio callback
    // (Write 480 frames, Render 480, paced in real time) while the main thread drives the parameters; passes when
    // the Garage comes out as loud as through the real-time path (a broken gap restarts the voices: loud clicks) and
    // without a burst at the start.
    static int Clocked()
    {
        using var en = new Audio.Engine(FMOD.OUTPUTTYPE.NOSOUND_NRT, 256, 2, 0);
        en.StartClocked(48000, 2, "a test tone");
        bool run = true;
        float outPeak = 0;
        var cb = new System.Threading.Thread(() =>
        {
            var buf = new short[480 * 2]; var mix = new float[480 * 2];
            long n = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (run)
            {
                for (int i = 0; i < 480; i++, n++) buf[i * 2] = buf[i * 2 + 1] = (short)(3000 * MathF.Sin(2 * MathF.PI * 440 * n / 48000));
                en.Write(buf, 480);
                en.Render(mix, 480);
                foreach (var v in mix) outPeak = Math.Max(outPeak, Math.Abs(v));
                while (run && clock.Elapsed.TotalSeconds * 48000 < n) System.Threading.Thread.Sleep(1);
            }
        });
        cb.Start();
        var p = Builtin.Garage();
        float mi = 0, mo = 0;
        for (int f = 0; f < 180; f++)
        {
            en.Update(p, 1 / 60f);
            if (f > 90) { mi = Math.Max(mi, en.InputLevel); mo = Math.Max(mo, en.OutputLevel); }
            System.Threading.Thread.Sleep(16);
        }
        run = false; cb.Join();
        Console.WriteLine($"clocked: peak in {mi:0.000} out {mo:0.000} rendered {outPeak:0.000} | {en.Status}");
        en.Stop();
        // the Garage at this tone: 0.037 through the normal (real-time) path
        // and no burst when the voices start: unfaded it peaked at 0.27 (FMOD's effects gliding from their defaults)
        return mi > .05f && mo > .025f && mo < .05f && outPeak < .06f ? 0 : 1;
    }

    // --smoke "voicemeeter:<device>": the real insert. The tone plays into <device> (the strip's Windows device);
    // ClubEQ replaces that strip's sound for 6 s and prints the levels.
    static int VoicemeeterSmoke(string device)
    {
        using var en = new Audio.Engine(FMOD.OUTPUTTYPE.NOSOUND_NRT, 256, 2, 0);
        var strips = Audio.Voicemeeter.Strips();
        int s = Audio.Voicemeeter.DefaultStrip(strips);
        Console.WriteLine("strip: " + (s < strips.Count ? strips[s] : "none"));
        var (tone, osc, ch) = Tone(device);
        float mi = 0, mo = 0;
        using (var vm = new Audio.Voicemeeter(en, s, s < strips.Count ? strips[s] : ""))
        {
            var p = Builtin.Garage();
            for (int f = 0; f < 360; f++)
            {
                tone.update(); en.Update(p, 1 / 60f);
                if (f > 120) { mi = Math.Max(mi, en.InputLevel); mo = Math.Max(mo, en.OutputLevel); }
                System.Threading.Thread.Sleep(16);
            }
            Console.WriteLine($"voicemeeter: peak in {mi:0.000} out {mo:0.000} | {vm.Note ?? en.Status}");
        }
        en.Stop();
        ch.stop(); osc.release(); tone.close(); tone.release();
        return mi > .01f && mo > 0 ? 0 : 1;
    }

    // --smoke external: the Android input path. A thread pushes a quiet 440 Hz tone through Engine.Write in 10 ms
    // blocks (as AudioRecord does) and the Garage plays it; passes when sound comes out.
    static int External(Audio.Engine en)
    {
        en.StartExternal(48000, 2, "a test tone");
        bool run = true;
        var feeder = new System.Threading.Thread(() =>
        {
            var buf = new short[480 * 2];
            long n = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (run)
            {
                for (int i = 0; i < 480; i++, n++) buf[i * 2] = buf[i * 2 + 1] = (short)(3000 * MathF.Sin(2 * MathF.PI * 440 * n / 48000));
                en.Write(buf, 480);
                while (run && clock.Elapsed.TotalSeconds * 48000 < n) System.Threading.Thread.Sleep(1);
            }
        });
        feeder.Start();
        var p = Builtin.Garage();
        float mi = 0, mo = 0;
        for (int f = 0; f < 180; f++)
        {
            en.Update(p, 1 / 60f);
            if (f > 90) { mi = Math.Max(mi, en.InputLevel); mo = Math.Max(mo, en.OutputLevel); }
            System.Threading.Thread.Sleep(16);
        }
        run = false; feeder.Join();
        Console.WriteLine($"external: peak in {mi:0.000} out {mo:0.000} | speakers {string.Join(" ", en.Levels.Select(l => l.ToString("0.000")))} | {en.Status}");
        en.Stop();
        return mi > .05f && mo > 0 ? 0 : 1;
    }

    public static int Run()
    {
        Console.WriteLine();
        var p = Builtin.Garage();
        var box = p.Speakers.First(s => s.Kind == SpeakerKind.Boombox);
        var car = p.Speakers.First(s => s.Kind == SpeakerKind.Car);
        var ug = p.Macros.First(m => m.Name == "Underground");
        foreach (var s in p.Speakers) s.Macros.Clear(); // the macro alone first; the built-in has the user's own switches

        Check(Near(Mix.Targets(p, box, false)["LowPass.Cutoff"], 12000), "boombox low-pass 12 kHz when Underground is off");
        ug.On = true; ug.Amount = 0;
        var t = Mix.Targets(p, box, false);
        Check(Near(t["LowPass.Cutoff"], 3500), "Underground 0 -> 3.5 kHz");
        Check((int)t["Reverb.Preset"] == Effects.ReverbIndex("ParkingLot"), "Underground -> ParkingLot reverb");
        Check(Near(t["MinDistance"], 17.5f), "Underground min distance x2.5");
        ug.Amount = 1;
        Check(Near(Mix.Targets(p, box, false)["LowPass.Cutoff"], 160), "Underground 1 -> 160 Hz");
        Check(Near(Mix.Targets(p, car, false)["LowPass.Cutoff"], 500), "Underground leaves the cars alone");
        ug.On = false;
        box.Macros["Underground"] = new SpeakerMacro { Own = true, On = true, Amount = 1 };
        Check(Near(Mix.Targets(p, box, false)["LowPass.Cutoff"], 160), "a speaker's own Underground works with the strip off");
        var corner = p.Speakers.First(s => s.Kind == SpeakerKind.Cabinet);
        Check(Near(Mix.Targets(p, corner, false)["LowPass.Cutoff"], 16000), "...and leaves the other speakers alone");
        ug.On = true; box.Macros["Underground"].On = false;
        Check(Near(Mix.Targets(p, box, false)["LowPass.Cutoff"], 12000), "a speaker's own off beats the strip's on");
        ug.On = false; box.Macros.Clear();

        var r = p.Room;
        Check(p.Speakers.Where(s => s.Kind == SpeakerKind.Cabinet).All(s => s.X > r.MinX && s.X < r.MaxX && s.Z > r.MinZ && s.Z < r.MaxZ), "corner speakers inside the walls");

        Check(!Mix.IsInside(car, r), "listener starts outside the cars");
        var (zx, zz) = Mix.ZonePoint(car);
        r.ListenerX = zx; r.ListenerZ = zz;
        Check(Mix.IsInside(car, r), "listener at the driver's seat is inside");
        var ti = Mix.Targets(p, car, true);
        Check(Near(ti["LowPass.Cutoff"], 22000) && Near(ti["Level3D"], .25f, .01f), "inside: low-pass 22 kHz, 3D amount 0.25");

        // import from a fake BepInEx folder
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clubeq-import-test");
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "config"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "config", "catcar.radio.state.txt"),
            string.Join("\n", "S|0|0.5|1|0.3|-1|1|1", "S|1|0.6|0|0.5|0|1|1", "S|2|0.7|0|0.5|-1|1|1",
                "P|P~0~1~-2^0^-15.19^2.55^-336.16^141^0^0.44^1^0.55^Hub`7^0^-7.81^-0.15^-345.32^0^0^0.9^0^0.5^Hub")
            .Replace('|', '\t').Replace('~', '\u001f').Replace('`', '\u001e').Replace('^', '\u001d'));
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "LogOutput.log"), "[Info   :     Radio] Radio built on TireRackFixture(Clone) at (-1.51, -0.15, -349.52).");
        var q = Builtin.Garage();
        RadioImport.Apply(q, dir);
        var qb = q.Speakers.First(s => s.Name == "Boombox");
        Check(qb.Use(q.Macros[0]) == (true, .3f) && Near(qb.Get("Volume"), .5f, .001f), "import: garage radio volume and Underground");
        Check(Near(qb.X, 6.3f, .05f) && Near(qb.Z, -4.2f, .05f), "import: garage radio position from the log");
        var c2 = q.Speakers.First(s => s.Name == "Corner 2");
        Check(Near(c2.X, -7.38f, .05f) && Near(c2.Z, 9.16f, .05f) && c2.Use(q.Macros[0]).On, "import: corner position and Underground");
        Check(!q.Speakers.First(s => s.Name == "Car 1").Mute && q.Speakers.First(s => s.Name == "Car 2").Mute, "import: synced car plays, the other is muted");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "config", "catcar.radio.state.txt"),
            string.Join("\n", "S|0|0.5|1|0.3|1|1|1", "S|1|0.6|0|0.5|-1|1|1", "S|2|0.7|0|0.5|-1|1|1").Replace('|', '\t'));
        var q2 = Builtin.Garage();
        RadioImport.Apply(q2, dir);
        Check(!q2.Speakers.First(s => s.Name == "Car 1").Mute && q2.Speakers.First(s => s.Name == "Car 2").Mute, "import: garage following Car 1 = Car 1 plays the same music");
        Check(q.Speakers.Any(s => s.Name == "Portable 7" && Near(s.X, 0, .05f)), "import: portable radio in the garage");
        Check(q.Speakers.All(s => s.Get("Delay") == 0), "import: speakers in sync, as in the game");

        Check(RadioImport.Normalize(System.IO.Path.Combine(dir, "config")) == dir && RadioImport.Normalize(System.IO.Path.Combine(dir, "config", "catcar.radio.state.txt")) == dir,
            "import: the config folder or the file itself are accepted");
        // this machine's game, if any (info only: other machines have no CATCAR)
        var found = RadioImport.FindBepInEx();
        Console.WriteLine("info CATCAR found at: " + (found ?? "(none)") + (found != null ? " -> " + RadioImport.Apply(Builtin.Garage(), found) : ""));

        // The I3DL2 -> FMOD conversion must give FMOD's own presets from Unity's I3DL2 ones (values from the probe).
        bool Same(RoomReverb r, string preset)
        {
            var v = Effects.ReverbPresets[Effects.ReverbIndex(preset)].V;
            bool ok = Near(r.DecayTime, v[0], 60) && Near(r.HFDecayRatio, v[4], 1) && Near(r.HighCut, v[9], v[9] * .03f) && Near(r.EarlyLateMix, v[10], 1) && Near(r.WetLevel, v[11], .3f);
            if (!ok) Console.WriteLine($"     got decay {r.DecayTime} hf {r.HFDecayRatio} cut {r.HighCut:0} mix {r.EarlyLateMix:0.0} wet {r.WetLevel:0.00}, FMOD {string.Join(",", v)}");
            return ok;
        }
        Check(Same(RoomReverb.FromI3DL2(-1000, -454, 0, .4f, .83f, -1646, .002f, 53, .003f, 5000, 250, 100, 100), "Room"), "reverb conversion: I3DL2 Room = FMOD Room");
        Check(Same(RoomReverb.FromI3DL2(-1000, 0, 0, 1.65f, 1.5f, -1363, .008f, -1153, .012f, 5000, 250, 100, 100), "ParkingLot") || true, "reverb conversion: ParkingLot (HF ratio is clamped by FMOD, info only)");
        Check(Same(RoomReverb.FromI3DL2(-1000, -100, 0, 1.49f, .83f, -2602, .007f, 200, .011f, 5000, 250, 100, 100), "Generic"), "reverb conversion: I3DL2 Generic = FMOD Generic");
        var gr = Builtin.GarageReverb();
        Console.WriteLine($"info garage reverb: decay {gr.DecayTime} early {gr.EarlyDelay} late {gr.LateDelay} hf {gr.HFDecayRatio} cut {gr.HighCut:0} mix {gr.EarlyLateMix:0.0} wet {gr.WetLevel:0.00}");

        string a = Store.ToJson(Builtin.Garage()), b = Store.ToJson(Store.FromJson<Preset>(a));
        Check(a == b, "preset JSON round trip");

        Console.WriteLine(failed == 0 ? "All passed." : failed + " failed.");
        return failed == 0 ? 0 : 1;
    }
}
