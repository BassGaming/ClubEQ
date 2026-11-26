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
}
