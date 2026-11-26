using System;
using System.Collections.Generic;

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

    static float Room = Effects.ReverbIndex("Room");

    public static Preset Garage()
    {
        var p = new Preset { Name = "Garage", Master = .3f }; // 7 speakers add up; leaves headroom before the limiter
        // Walls measured by the mod (log "Corner speakers: walls at +x 7.4 -x 7.8 +z 9.6 -z 5.5 m from the middle").
        p.Room = new Room { MinX = -7.8f, MaxX = 7.4f, MinZ = -5.5f, MaxZ = 9.6f, Height = 3, ListenerX = 0, ListenerY = 1.6f, ListenerZ = -1.5f };
        // The garage's AudioReverbZone "GarageReverb" (preset User), converted from the values the game reports (CLAUDE.md).
        p.Reverb = new RoomReverb();

        // Boombox on its crate, back-row tile 9 (tile grid 10 x 7 at 1.4 m). Station knob 0.8 x Volume 0.8.
        p.Speakers.Add(new Speaker
        {
            Name = "Boombox", Kind = SpeakerKind.Boombox, X = 6.3f, Y = .75f, Z = 4.2f, Yaw = 180,
            P = P(("Volume", .64f), ("MinDistance", 7), ("MaxDistance", 150), ("Spread", 60), ("Level3D", 1)),
            Chain = { Fx("HighPass", ("Cutoff", 90)), Fx("LowPass", ("Cutoff", 12000)), Fx("Reverb", ("Preset", Room)), Fx("Distortion", ("Level", 0)) },
        });

        // Four PA cabinets 0.4 m off both walls of each corner, 2.7 m up, facing the middle. Knob 0.6 x 0.8.
        var r = p.Room;
        int n = 1;
        foreach (var (x, z) in new[] { (r.MaxX - .4f, r.MaxZ - .4f), (r.MinX + .4f, r.MaxZ - .4f), (r.MinX + .4f, r.MinZ + .4f), (r.MaxX - .4f, r.MinZ + .4f) })
            p.Speakers.Add(new Speaker
            {
                Name = "Corner " + n++, Kind = SpeakerKind.Cabinet, X = x, Y = 2.7f, Z = z, Yaw = MathF.Atan2(-x, -z) * 180 / MathF.PI,
                P = P(("Volume", .48f), ("MinDistance", 6), ("MaxDistance", 150), ("Spread", 60), ("Level3D", 1)),
                Chain = { Fx("HighPass", ("Cutoff", 35)), Fx("LowPass", ("Cutoff", 16000)), Fx("Reverb", ("Preset", Room)), Fx("Distortion", ("Level", 0)) },
            });

        // The two crew cars, radio 0.9 m above the car. Outside: through the body, mostly bass. Inside (within 1.5 m of
        // the driver's head): open, and from all around. Knob 0.8 x 0.8 x 1.3. Parked spots are a guess: drag them.
        foreach (var (name, x) in new[] { ("Car 1", -2.6f), ("Car 2", 2.6f) })
            p.Speakers.Add(new Speaker
            {
                Name = name, Kind = SpeakerKind.Car, X = x, Y = .9f, Z = 1.5f, Yaw = 0,
                P = P(("Volume", .83f), ("MinDistance", 4), ("MaxDistance", 150), ("Spread", 60), ("Level3D", 1)),
                Chain = { Fx("LowPass", ("Cutoff", 500)) },
                Inside = new InsideProfile { OffsetX = -.35f, OffsetZ = -.2f, Radius = 1.5f, Values = P(("LowPass.Cutoff", 22000), ("Level3D", .25f)) },
            });

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
