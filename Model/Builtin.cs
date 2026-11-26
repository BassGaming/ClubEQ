using System;
using System.Collections.Generic;
using System.Linq;

namespace ClubEQ.Model;

// The built-in presets. Every number comes from the CATCAR Radio mod (Radio/Speakers.cs Fx, Radio/RadioPlugin.cs) and
// the game's garage, see CLAUDE.md "Where the numbers come from".
public static class Builtin
{
    public static Preset[] All() => new[] { Garage() };

    static Effect Fx(string type, params (string, float)[] p)
    {
        var e = new Effect { Type = type };
        foreach (var (k, v) in p) e.P[k] = v;
        return e;
    }

    static Dictionary<string, float> P(params (string, float)[] p)
    {
        var d = new Dictionary<string, float>();
        foreach (var (k, v) in p) d[k] = v;
        return d;
    }

    static readonly float Room = Effects.ReverbIndex("Room");


    // A corner PA cabinet as the mod builds it: high-pass 35, low-pass 16 k, reverb Room, fully positional from 6 m.
    public static Speaker Corner(string name, float x, float z) => new()
    {
        Name = name, Kind = SpeakerKind.Cabinet, X = x, Y = 2.7f, Z = z, Yaw = MathF.Atan2(-x, -z) * 180 / MathF.PI,
        P = P(("Volume", .6f), ("MinDistance", 6), ("MaxDistance", 150), ("Spread", 60), ("Level3D", 1)),
        Chain = { Fx("HighPass", ("Cutoff", 35)), Fx("LowPass", ("Cutoff", 16000)), Fx("Reverb", ("Preset", Room)), Fx("Distortion", ("Level", 0)) },
    };

    // A portable radio as the mod builds it: high-pass 90, low-pass 11 k, no reverb, from 4 m.
    public static Speaker Portable(string name, float x, float z) => new()
    {
        Name = name, Kind = SpeakerKind.Boombox, X = x, Y = .3f, Z = z,
        P = P(("Volume", .7f), ("MinDistance", 4), ("MaxDistance", 150), ("Spread", 60), ("Level3D", 1)),
        Chain = { Fx("HighPass", ("Cutoff", 90)), Fx("LowPass", ("Cutoff", 11000)), Fx("Reverb", ("Preset", 0)), Fx("Distortion", ("Level", 0)) },
    };

    // The garage's AudioReverbZone "GarageReverb" (preset User) as the game reports it (probe/ReverbDump): room -900,
    // roomHF -400, roomLF 0, decay 1.2 s, HF ratio 0.55, reflections -500 @ 8 ms, reverb +100 @ 18 ms, HF ref 5000,
    // LF ref 250, diffusion 100, density 100. Converted: high cut ~6.5 kHz, wet ~-7 dB, early/late 67 %.
    // In the game it plays 10.2 dB louder than that (sweep measurements against CATCAR at the user's spot, 2026-11-26:
    // with it, early decay 0.62 s vs 0.63, T20 0.78 vs 0.82, crosstalk and third-octave EQ within 1.6 dB).
    public const float UnityZoneGain = 10.2f;
    public static RoomReverb GarageReverb()
    {
        var r = RoomReverb.FromI3DL2(-900, -400, 0, 1.2f, .55f, -500, .008f, 100, .018f, 5000, 250, 100, 100);
        r.WetLevel += UnityZoneGain;
        return r;
    }

    // The two crew cars, parked behind the middle facing the back wall (probe: HubTeamCar / HubTeamCar2 CarRadio at
    // (-5.81, 1.39, -338.44) / (-9.21, 1.39, -338.44), cockpit camera right under the radio). Outside: through the
    // body, mostly bass. Inside (within 1.5 m of the driver's head): open, and from all around. Volume = knob x 1.3.
    public static Speaker[] Cars() => new[] { ("Car 1", 2.0f, false), ("Car 2", -1.4f, true) }.Select(c => new Speaker
    {
        Name = c.Item1, Kind = SpeakerKind.Car, X = c.Item2, Y = 1.54f, Z = 6.88f, Yaw = 180, Mute = c.Item3,
        P = P(("Volume", .7f * 1.3f), ("MinDistance", 4), ("MaxDistance", 150), ("Spread", 60), ("Level3D", 1)),
        Chain = { Fx("LowPass", ("Cutoff", 500)) },
        Inside = new InsideProfile { OffsetX = 0, OffsetZ = 0, Radius = 1.5f, Values = P(("LowPass.Cutoff", 22000), ("Level3D", .25f)) },
    }).ToArray();

    public static Preset Garage()
    {
        var p = new Preset { Name = "Garage" };
        // Walls measured by the mod (log "Corner speakers: walls at +x 7.4 -x 7.8 +z 9.6 -z 5.5 m from the middle").
        // Ears at 1.0 m: you are a cat (probe: camera 1.0 m above the garage floor).
        p.Room = new Room { MinX = -7.8f, MaxX = 7.4f, MinZ = -5.5f, MaxZ = 9.6f, Height = 3, ListenerX = 0, ListenerY = 1, ListenerZ = -1.5f };
        p.Reverb = GarageReverb();

        // The user's own setup in the mod on 2026-11-26 (catcar.radio.state.txt; "Import from CATCAR" re-reads it):
        // garage radio 58 % with Underground at 39 %, corners 41-47 % with Underground on corners 2 and 4, Car 1 synced
        // to the garage at 70 %, Car 2 on its own playlist (muted here).
        // Garage radio: log "Radio built on TireRackFixture(Clone) at (-1.51, -0.15, -349.52)", on its crate.
        var box = new Speaker
        {
            Name = "Boombox", Kind = SpeakerKind.Boombox, X = 6.3f, Y = .52f, Z = -4.2f, Yaw = 0, // probe: source at y 0.37 world
            P = P(("Volume", .585f), ("MinDistance", 7), ("MaxDistance", 150), ("Spread", 60), ("Level3D", 1)),
            Chain = { Fx("HighPass", ("Cutoff", 90)), Fx("LowPass", ("Cutoff", 12000)), Fx("Reverb", ("Preset", Room)), Fx("Distortion", ("Level", 0)) },
        };
        box.Macros["Underground"] = new SpeakerMacro { Own = true, On = true, Amount = .389f };
        p.Speakers.Add(box);

        // Four PA cabinets 0.4 m off both walls of each corner, 2.7 m up, facing the middle (positions as the mod measured them).
        var r = p.Room;
        var corners = new[] { (6.98f, 9.16f, .409f, false, .5f), (-7.38f, 9.16f, .4375f, true, .557f), (-7.38f, -5.07f, .447f, false, .5f), (6.98f, -5.07f, .466f, true, .504f) };
        for (int i = 0; i < 4; i++)
        {
            var (x, z, vol, muffle, amt) = corners[i];
            var c = Corner("Corner " + (i + 1), x, z);
            c.P["Volume"] = vol;
            c.Macros["Underground"] = new SpeakerMacro { Own = true, On = muffle, Amount = amt };
            p.Speakers.Add(c);
        }

        foreach (var c in Cars()) p.Speakers.Add(c);

        // Underground: the bass through a concrete wall at a rave (Fx.Update). Boombox and corners only, not the cars.
        p.Macros.Add(new Macro
        {
            Name = "Underground", On = false, Amount = .5f, Targets =
            {
                new MacroTarget { Speakers = "kind:Boombox, kind:Cabinet", Param = "LowPass.Cutoff", From = 3500, To = 160, Curve = Curve.Exponential },
                new MacroTarget { Speakers = "kind:Boombox, kind:Cabinet", Param = "HighPass.Cutoff", From = 10, To = 10 },
                new MacroTarget { Speakers = "kind:Boombox, kind:Cabinet", Param = "Reverb.Preset", From = Effects.ReverbIndex("ParkingLot"), To = Effects.ReverbIndex("ParkingLot") },
                new MacroTarget { Speakers = "kind:Boombox, kind:Cabinet", Param = "Distortion.Level", From = .12f, To = .4f },
                new MacroTarget { Speakers = "kind:Boombox, kind:Cabinet", Param = "MinDistance", From = 2.5f, To = 2.5f, Multiply = true },
                new MacroTarget { Speakers = "kind:Boombox, kind:Cabinet", Param = "Volume", From = 1.5f, To = 1.5f, Multiply = true },
            },
        });
        return p;
    }
}
