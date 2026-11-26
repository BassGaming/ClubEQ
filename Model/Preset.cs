using System;
using System.Collections.Generic;
using System.Linq;

namespace ClubEQ.Model;

// A complete sound: the room, its speakers, the room's own reverb and the macros (Underground, ...). Plain data, saved as JSON.
// Coordinates are metres, Unity/FMOD style: x right, y up, z forward. The room view looks down with +z up the screen.
public class Preset
{
    public string Name = "New preset";
    public float Master = 1;
    public Room Room = new();
    public RoomReverb Reverb = new();
    public List<Speaker> Speakers = new();
    public List<Macro> Macros = new();
}

public class Room
{
    public float MinX = -5, MaxX = 5, MinZ = -5, MaxZ = 5, Height = 3;
    public float ListenerX, ListenerY = 1.6f, ListenerZ, ListenerYaw; // yaw in degrees, 0 = facing +z
}

public enum SpeakerKind { Boombox, Cabinet, Car, Generic }

public class Speaker
{
    public string Name = "Speaker";
    public SpeakerKind Kind = SpeakerKind.Generic;
    public float X, Y = 1, Z, Yaw;
    public bool Mute, Solo;
    // Speaker-level parameters, addressed like effect parameters ("Volume", "MinDistance", ...; see Effects.SpeakerParams).
    public Dictionary<string, float> P = new();
    public List<Effect> Chain = new();
    public InsideProfile Inside; // null = no inside zone
    // Per macro name: this speaker's own on/off and amount (the mod has an Underground toggle and slider per speaker).
    // Missing or Own = false: follows the macro strip.
    public Dictionary<string, SpeakerMacro> Macros = new();

    public (bool On, float Amount) Use(Macro m) =>
        Macros.TryGetValue(m.Name, out var u) && u.Own ? (u.On, u.Amount) : (m.On, m.Amount);

    public float Get(string name) => P.TryGetValue(name, out var v) ? v : Effects.SpeakerParam(name).Default;
}

// While the listener is within Radius of the zone point (relative to the speaker, turned with its yaw), these values
// replace the normal ones (smoothed by the engine). The car: low-pass opens, the sound comes from all around.
public class InsideProfile
{
    public float OffsetX, OffsetZ, Radius = 1.5f;
    public Dictionary<string, float> Values = new();
}

public class SpeakerMacro
{
    public bool Own, On;
    public float Amount = .5f;
}

public class Effect
{
    public string Type = "LowPass";
    public bool Bypass;
    public Dictionary<string, float> P = new();

    public float Get(string name) => P.TryGetValue(name, out var v) ? v : Effects.Get(Type).Param(name).Default;
}

// One slider (0..1) and an on/off switch driving any number of parameters.
public class Macro
{
    public string Name = "Macro";
    public bool On;
    public float Amount = .5f;
    public List<MacroTarget> Targets = new();
}

public enum Curve { Linear, Exponential }

public class MacroTarget
{
    public string Speakers = "*"; // "*", or a comma list of speaker names and "kind:Cabinet" entries
    public string Param = "LowPass.Cutoff";
    public float From, To;
    public Curve Curve;
    public bool Multiply; // false = set the value, true = multiply the speaker's own value

    public bool Matches(Speaker s) => Speakers.Trim() == "*" || Speakers.Split(',').Select(x => x.Trim()).Any(x =>
        x.Equals(s.Name, StringComparison.OrdinalIgnoreCase) ||
        (x.StartsWith("kind:", StringComparison.OrdinalIgnoreCase) && x[5..].Equals(s.Kind.ToString(), StringComparison.OrdinalIgnoreCase)));

    public float At(float amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        if (Curve == Curve.Exponential && From > 0 && To > 0)
            return MathF.Exp(MathF.Log(From) + (MathF.Log(To) - MathF.Log(From)) * amount);
        return From + (To - From) * amount;
    }
}

// The room's reverb (Unity's AudioReverbZone): FMOD's REVERB_PROPERTIES, applied to every speaker.
public class RoomReverb
{
    public bool On = true;
    public float DecayTime = 1500, EarlyDelay = 7, LateDelay = 11, HFReference = 5000, HFDecayRatio = 83, Diffusion = 100,
        Density = 100, LowShelfFrequency = 250, LowShelfGain = 0, HighCut = 14500, EarlyLateMix = 96, WetLevel = -8;

    // Unity's reverbs (AudioReverbZone, AudioReverbFilter) use I3DL2 values (millibels, seconds). This conversion
    // reproduces FMOD's own preset table from the I3DL2 presets (checked on Room, ParkingLot, Generic in the selftest):
    // high cut = HF ref / sqrt((1-g)/g) with g = roomHF as gain; early/late mix = late / (early + late) in amplitude;
    // wet = room + the power sum of early and late.
    public static RoomReverb FromI3DL2(float room, float roomHF, float roomLF, float decayTime, float decayHFRatio, float reflections,
        float reflectionsDelay, float reverb, float reverbDelay, float hfReference, float lfReference, float diffusion, float density)
    {
        double g = Math.Pow(10, roomHF / 2000.0), e = Math.Pow(10, reflections / 2000.0), l = Math.Pow(10, reverb / 2000.0);
        return new RoomReverb
        {
            DecayTime = decayTime * 1000, EarlyDelay = reflectionsDelay * 1000, LateDelay = reverbDelay * 1000,
            HFReference = hfReference, HFDecayRatio = decayHFRatio * 100, Diffusion = diffusion, Density = density,
            LowShelfFrequency = lfReference, LowShelfGain = roomLF / 100,
            HighCut = g >= .9999 ? 20000 : (float)Math.Min(20000, hfReference / Math.Sqrt((1 - g) / g)),
            EarlyLateMix = (float)(100 * l / (e + l)),
            WetLevel = (float)(room / 100 + 10 * Math.Log10(e * e + l * l)),
        };
    }
}
