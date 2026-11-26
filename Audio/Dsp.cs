using System;
using System.Collections.Generic;
using ClubEQ.Model;

namespace ClubEQ.Audio;

// One effect of a speaker's chain on its FMOD DSP. A new effect type: an entry in Model/Effects.cs plus a case here.
sealed class Dsp
{
    public readonly string Type;
    public readonly FMOD.DSP Handle;
    readonly Dictionary<string, float> last = new();

    readonly bool multiband; // high/low-pass on the multiband EQ (fallback): band A's params start at 1, not 0

    Dsp(string type, FMOD.DSP h, bool mb = false) { Type = type; Handle = h; multiband = mb; }

    public static Dsp Create(FMOD.System sys, string type)
    {
        // Unity's AudioLowPassFilter / AudioHighPassFilter are FMOD's classic resonant LOWPASS / HIGHPASS (cutoff,
        // resonance Q), so use those; if this FMOD refuses them, band A of the multiband EQ (12 dB) stands in.
        if (type is "HighPass" or "LowPass")
        {
            if (sys.createDSPByType(type == "LowPass" ? FMOD.DSP_TYPE.LOWPASS : FMOD.DSP_TYPE.HIGHPASS, out var f) == FMOD.RESULT.OK) return new Dsp(type, f);
            Engine.Ok(sys.createDSPByType(FMOD.DSP_TYPE.MULTIBAND_EQ, out var mb), "create " + type);
            Engine.Ok(mb.setParameterInt(0, type == "LowPass" ? 1 : 4), "filter type"); // LOWPASS_12DB / HIGHPASS_12DB
            return new Dsp(type, mb, true);
        }
        var kind = type switch
        {
            "Reverb" => FMOD.DSP_TYPE.SFXREVERB,
            "Distortion" => FMOD.DSP_TYPE.DISTORTION,
            "Eq" => FMOD.DSP_TYPE.THREE_EQ,
            "Gain" => FMOD.DSP_TYPE.FADER,
            "Echo" => FMOD.DSP_TYPE.ECHO,
            _ => throw new ArgumentException("No DSP for effect " + type),
        };
        Engine.Ok(sys.createDSPByType(kind, out var h), "create " + type);
        return new Dsp(type, h);
    }

    public bool Multiband => multiband;

    // values: the effect's parameters by name (already smoothed). Only changes go to FMOD.
    public void Apply(Func<string, float> value, bool bypass)
    {
        var def = Effects.Get(Type);
        bool changed = false;
        foreach (var p in def.Params)
        {
            float v = value(p.Name);
            if (last.TryGetValue(p.Name, out var old) && MathF.Abs(old - v) <= MathF.Abs(old) * 1e-4f + 1e-6f) continue;
            last[p.Name] = v; changed = true;
        }
        if (Type == "Distortion") bypass |= last["Level"] <= 0; // as the mod: level 0 = filter off
        if (!last.TryGetValue("_bypass", out var b) || (b > 0) != bypass) { Handle.setBypass(bypass); last["_bypass"] = bypass ? 1 : 0; }
        if (!changed) return;
        float L(string n) => last[n];
        switch (Type)
        {
            case "HighPass":
            case "LowPass":
                {
                    int o = multiband ? 1 : 0;
                    Set(o, Math.Clamp(L("Cutoff"), multiband ? 20 : 10, 22000)); Set(o + 1, Math.Clamp(L("Resonance"), multiband ? .1f : 1, 10));
                    break;
                }
            case "Reverb":
                {
                    int i = Math.Clamp((int)MathF.Round(L("Preset")), 0, Effects.ReverbPresets.Length - 1);
                    var v = Effects.ReverbPresets[i].V;
                    for (int k = 0; k < 11; k++) Set(k, v[k]);
                    Set(11, Math.Clamp(v[11] + L("Wet"), -80, 20));
                    Set(12, Math.Clamp(L("Dry"), -80, 20));
                    break;
                }
            case "Distortion": Set(0, Math.Clamp(L("Level"), 0, 1)); break;
            case "Eq":
                Set(0, L("Low")); Set(1, L("Mid")); Set(2, L("High")); Set(3, L("LowX")); Set(4, Math.Max(L("HighX"), L("LowX") + 10)); break;
            case "Gain": Set(0, L("Gain")); break;
            case "Echo": Set(0, L("Delay")); Set(1, L("Feedback")); Set(2, L("Dry")); Set(3, L("Wet")); break;
        }
    }

    void Set(int i, float v) => Handle.setParameterFloat(i, v);

    public void Release() => Handle.release();
}
