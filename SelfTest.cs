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
    public static int Smoke(string input)
    {
        Console.WriteLine();
        using var en = new Audio.Engine();
        var ins = en.Inputs();
        int i = ins.FindIndex(n => n.Contains(input, StringComparison.OrdinalIgnoreCase));
        if (i < 0) { Console.WriteLine("No input matching " + input); return 1; }
        // the tone: a second FMOD system playing into the matching output device
        Audio.Engine.Ok(FMOD.Factory.System_Create(out var tone), "tone system");
        tone.getNumDrivers(out int n);
        int o = -1;
        for (int k = 0; k < n; k++) { tone.getDriverInfo(k, out string name, 256, out _, out _, out _, out _); if (name.Contains(input, StringComparison.OrdinalIgnoreCase)) o = k; }
        if (o >= 0) tone.setDriver(o);
        tone.init(8, FMOD.INITFLAGS.NORMAL, IntPtr.Zero);
        tone.createDSPByType(FMOD.DSP_TYPE.OSCILLATOR, out var osc);
        osc.setParameterFloat(1, 440);
        tone.playDSP(osc, default, false, out var ch);
        ch.setVolume(.1f);

        en.Start(i);
        Console.WriteLine(en.Status);
        float maxOut = 0;
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

    public static int Run()
    {
        Console.WriteLine();
        var p = Builtin.Garage();
        var box = p.Speakers.First(s => s.Kind == SpeakerKind.Boombox);
        var car = p.Speakers.First(s => s.Kind == SpeakerKind.Car);
        var ug = p.Macros.First(m => m.Name == "Underground");

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

        var r = p.Room;
        Check(p.Speakers.Where(s => s.Kind == SpeakerKind.Cabinet).All(s => s.X > r.MinX && s.X < r.MaxX && s.Z > r.MinZ && s.Z < r.MaxZ), "corner speakers inside the walls");

        Check(!Mix.IsInside(car, r), "listener starts outside the cars");
        var (zx, zz) = Mix.ZonePoint(car);
        r.ListenerX = zx; r.ListenerZ = zz;
        Check(Mix.IsInside(car, r), "listener at the driver's seat is inside");
        var ti = Mix.Targets(p, car, true);
        Check(Near(ti["LowPass.Cutoff"], 22000) && Near(ti["Level3D"], .25f, .01f), "inside: low-pass 22 kHz, 3D amount 0.25");

        string a = Store.ToJson(Builtin.Garage()), b = Store.ToJson(Store.FromJson<Preset>(a));
        Check(a == b, "preset JSON round trip");

        Console.WriteLine(failed == 0 ? "All passed." : failed + " failed.");
        return failed == 0 ? 0 : 1;
    }
}
