using System;
using System.Collections.Generic;
using System.Linq;

namespace ClubEQ.Model;

public sealed record ParamDef(string Name, string Label, float Min, float Max, float Default, string Unit = "", bool Log = false, string[] Choices = null)
{
    public bool IsChoice => Choices != null;
}

public sealed record EffectDef(string Type, string Label, ParamDef[] Params)
{
    public ParamDef Param(string name) => Params.First(p => p.Name == name);
}

// Every effect type and its parameters. The UI builds its controls from this list and the engine maps each type to one
// FMOD DSP (Audio/Dsp.cs). A new effect type = one entry here + one case there.
public static class Effects
{
    // FMOD's reverb presets (fmod_common.h FMOD_PRESET_*), which Unity's AudioReverbFilter presets use. Fields:
    // decay ms, early delay, late delay, HF reference, HF decay ratio, diffusion, density, low shelf Hz, low shelf dB,
    // high cut, early/late mix, wet dB.
    public static readonly (string Name, float[] V)[] ReverbPresets =
    {
        ("Off",           new float[] { 1000,   7,  11, 5000, 100, 100, 100, 250, 0,    20,  96, -80f }),
        ("Generic",       new float[] { 1500,   7,  11, 5000,  83, 100, 100, 250, 0, 14500,  96,  -8f }),
        ("PaddedCell",    new float[] {  170,   1,   2, 5000,  10, 100, 100, 250, 0,   160,  84, -7.8f }),
        ("Room",          new float[] {  400,   2,   3, 5000,  83, 100, 100, 250, 0,  6050,  88, -9.4f }),
        ("Bathroom",      new float[] { 1500,   7,  11, 5000,  54, 100,  60, 250, 0,  2900,  83,  .5f }),
        ("LivingRoom",    new float[] {  500,   3,   4, 5000,  10, 100, 100, 250, 0,   160,  58, -19f }),
        ("StoneRoom",     new float[] { 2300,  12,  17, 5000,  64, 100, 100, 250, 0,  7800,  71, -8.5f }),
        ("Auditorium",    new float[] { 4300,  20,  30, 5000,  59, 100, 100, 250, 0,  5850,  64, -11.7f }),
        ("ConcertHall",   new float[] { 3900,  20,  29, 5000,  70, 100, 100, 250, 0,  5650,  80, -9.8f }),
        ("Cave",          new float[] { 2900,  15,  22, 5000, 100, 100, 100, 250, 0, 20000,  59, -11.3f }),
        ("Arena",         new float[] { 7200,  20,  30, 5000,  33, 100, 100, 250, 0,  4500,  80, -9.6f }),
        ("Hangar",        new float[] {10000,  20,  30, 5000,  23, 100, 100, 250, 0,  3400,  72, -7.4f }),
        ("CarpetedHall",  new float[] {  300,   2,  30, 5000,  10, 100, 100, 250, 0,   500,  56, -24f }),
        ("Hallway",       new float[] { 1500,   7,  11, 5000,  59, 100, 100, 250, 0,  7800,  87, -5.5f }),
        ("StoneCorridor", new float[] {  270,  13,  20, 5000,  79, 100, 100, 250, 0,  9000,  86, -6f }),
        ("Alley",         new float[] { 1500,   7,  11, 5000,  86, 100, 100, 250, 0,  8300,  80, -9.8f }),
        ("Forest",        new float[] { 1500, 162,  88, 5000,  54,  79, 100, 250, 0,   760,  94, -12.3f }),
        ("City",          new float[] { 1500,   7,  11, 5000,  67,  50, 100, 250, 0,  4050,  66, -26f }),
        ("Mountains",     new float[] { 1500, 300, 100, 5000,  21,  27, 100, 250, 0,  1220,  82, -24f }),
        ("Quarry",        new float[] { 1500,  61,  25, 5000,  83, 100, 100, 250, 0,  3400, 100, -5f }),
        ("Plain",         new float[] { 1500, 179, 100, 5000,  50,  21, 100, 250, 0,  1670,  65, -28f }),
        ("ParkingLot",    new float[] { 1700,   8,  12, 5000, 100, 100, 100, 250, 0, 20000,  56, -19.5f }),
        ("SewerPipe",     new float[] { 2800,  14,  21, 5000,  14,  80,  60, 250, 0,  3400,  66, 1.2f }),
        ("Underwater",    new float[] { 1500,   7,  11, 5000,  10, 100, 100, 250, 0,   500,  92, 7f }),
    };
    public static readonly string[] ReverbNames = ReverbPresets.Select(p => p.Name).ToArray();
    public static int ReverbIndex(string name) => Array.IndexOf(ReverbNames, name);

    static ParamDef Cutoff(float def) => new("Cutoff", "Cutoff", 10, 22000, def, "Hz", true);
    static ParamDef Resonance() => new("Resonance", "Resonance", 1, 10, 1);

    public static readonly EffectDef[] All =
    {
        new("HighPass", "High-pass", new[] { Cutoff(90), Resonance() }),
        new("LowPass", "Low-pass", new[] { Cutoff(5000), Resonance() }),
        new("Reverb", "Reverb", new ParamDef[] {
            new("Preset", "Preset", 0, ReverbNames.Length - 1, ReverbIndex("Room"), Choices: ReverbNames),
            new("Wet", "Wet adjust", -30, 10, 0, "dB"),
            new("Dry", "Dry", -80, 10, 0, "dB") }),
        new("Distortion", "Distortion", new[] { new ParamDef("Level", "Level", 0, 1, .2f) }),
        new("Eq", "3-band EQ", new ParamDef[] {
            new("Low", "Low", -30, 12, 0, "dB"), new("Mid", "Mid", -30, 12, 0, "dB"), new("High", "High", -30, 12, 0, "dB"),
            new("LowX", "Low / mid", 20, 2000, 400, "Hz", true), new("HighX", "Mid / high", 1000, 20000, 4000, "Hz", true) }),
        new("Gain", "Gain", new[] { new ParamDef("Gain", "Gain", -30, 20, 0, "dB") }),
        new("Echo", "Echo / slap", new ParamDef[] {
            new("Delay", "Delay", 10, 1500, 260, "ms"), new("Feedback", "Feedback", 0, 100, 0, "%"),
            new("Dry", "Dry", -80, 10, 0, "dB"), new("Wet", "Wet", -80, 10, -15, "dB") }),
    };

    public static EffectDef Get(string type) => All.FirstOrDefault(e => e.Type == type) ?? throw new ArgumentException("Unknown effect " + type);

    public static readonly ParamDef[] SpeakerParams =
    {
        new("Volume", "Volume", 0, 2, .8f),
        new("MinDistance", "Full volume within", .3f, 40, 5, "m", true),
        new("MaxDistance", "Max distance", 5, 500, 150, "m", true),
        new("Spread", "Spread", 0, 360, 60, "°"),
        new("Delay", "Time offset", 0, 60, 0, "ms"),
        new("Level3D", "3D amount", 0, 1, 1),
    };
    public static ParamDef SpeakerParam(string name) => SpeakerParams.First(p => p.Name == name);

    // "Volume" or "LowPass.Cutoff" -> its definition, or null.
    public static ParamDef Find(string address)
    {
        int dot = address.IndexOf('.');
        if (dot < 0) return SpeakerParams.FirstOrDefault(p => p.Name == address);
        var e = All.FirstOrDefault(x => x.Type == address[..dot]);
        return e?.Params.FirstOrDefault(p => p.Name == address[(dot + 1)..]);
    }

    public static IEnumerable<string> Addresses() =>
        SpeakerParams.Select(p => p.Name).Concat(All.SelectMany(e => e.Params.Select(p => e.Type + "." + p.Name)));
}
