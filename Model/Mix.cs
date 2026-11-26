using System;
using System.Collections.Generic;

namespace ClubEQ.Model;

// What a speaker should sound like right now: its own values, then the macros that are on, then its inside profile
// if the listener is inside. Pure; the engine smooths the result towards these targets.
public static class Mix
{
    public static Dictionary<string, float> Targets(Preset p, Speaker s, bool inside)
    {
        var v = new Dictionary<string, float>();
        foreach (var d in Effects.SpeakerParams) v[d.Name] = s.Get(d.Name);
        var seen = new HashSet<string>();
        foreach (var e in s.Chain)
        {
            if (!seen.Add(e.Type)) continue; // an address names the first effect of its type
            foreach (var d in Effects.Get(e.Type).Params) v[e.Type + "." + d.Name] = e.Get(d.Name);
        }
        foreach (var m in p.Macros)
        {
            var (on, amount) = s.Use(m);
            if (!on) continue;
            foreach (var t in m.Targets)
            {
                if (!t.Matches(s) || !v.TryGetValue(t.Param, out var cur)) continue;
                float x = t.At(amount);
                v[t.Param] = t.Multiply ? cur * x : x;
            }
        }
        if (inside && s.Inside != null)
            foreach (var (k, x) in s.Inside.Values)
                if (v.ContainsKey(k)) v[k] = x;
        return v;
    }

    public static (float X, float Z) ZonePoint(Speaker s)
    {
        var o = s.Inside;
        float a = s.Yaw * MathF.PI / 180, c = MathF.Cos(a), sn = MathF.Sin(a);
        return (s.X + o.OffsetX * c + o.OffsetZ * sn, s.Z - o.OffsetX * sn + o.OffsetZ * c); // Unity yaw: clockwise seen from above
    }

    public static bool IsInside(Speaker s, Room r)
    {
        if (s.Inside == null) return false;
        var (x, z) = ZonePoint(s);
        return (r.ListenerX - x) * (r.ListenerX - x) + (r.ListenerZ - z) * (r.ListenerZ - z) <= s.Inside.Radius * s.Inside.Radius;
    }
}
