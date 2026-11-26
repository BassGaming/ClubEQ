using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ClubEQ.Model;

namespace ClubEQ.Audio;

public sealed class FmodException(string msg) : Exception(msg);

// The sound: FMOD records the input device into a looping 1 s buffer and every speaker is a 3D channel playing that
// buffer ~50 ms behind the record cursor, with its own DSP chain (the mod's AudioSource + filters). All channels start
// together and always get the same frequency, so they stay sample-aligned.
public sealed class Engine : IDisposable
{
    const int LatencyMs = 50;

    FMOD.System sys;
    FMOD.Sound rec;
    FMOD.ChannelGroup master, group;
    FMOD.DSP meter;
    int recDriver = -1, rate, recChannels;
    uint soundLen; // in samples (frames)

    sealed class Voice
    {
        public FMOD.Channel Ch;
        public List<Dsp> Chain = new();
        public FMOD.DSP Meter;
        public Dictionary<string, float> Cur = new();
        public bool Inside;
    }
    readonly List<Voice> voices = new();
    Preset playing;
    string shape = "";
    bool started;

    // drift control (FMOD's record example, but proportional so the pitch never audibly wobbles)
    uint lastRec, lastPlay;
    long recorded, played;
    double latencyAvg;
    uint target;

    public string Status { get; private set; } = "Stopped";
    public bool Running => recDriver >= 0;
    public float InputLevel { get; private set; }
    public float OutputLevel { get; private set; }
    public float[] Levels { get; private set; } = Array.Empty<float>();

    public static void Ok(FMOD.RESULT r, string what)
    {
        if (r != FMOD.RESULT.OK) throw new FmodException($"{what}: {FMOD.Error.String(r)} ({r})");
    }

    public Engine()
    {
        Ok(FMOD.Factory.System_Create(out sys), "System_Create");
        Ok(sys.setDSPBufferSize(512, 4), "setDSPBufferSize");
        Ok(sys.init(64, FMOD.INITFLAGS.NORMAL, IntPtr.Zero), "init");
        Ok(sys.set3DSettings(0, 1, 1), "set3DSettings"); // no doppler, metres, normal roll-off
        Ok(sys.getMasterChannelGroup(out master), "master group");
        // Seven speakers playing the same song add up to ~4x the input (~11x with Underground): a limiter keeps the
        // output from clipping. The meter reads before it, so the UI shows when it has to work.
        Ok(master.getDSP(FMOD.CHANNELCONTROL_DSP_INDEX.FADER, out meter), "master fader");
        meter.setMeteringEnabled(false, true);
        Ok(sys.createDSPByType(FMOD.DSP_TYPE.LIMITER, out var limiter), "limiter");
        limiter.setParameterFloat(1, -1); // ceiling dB
        Ok(master.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD, limiter), "add limiter");
        Ok(sys.createChannelGroup("speakers", out group), "speakers group");
    }

    public List<string> Inputs()
    {
        var list = new List<string>();
        Ok(sys.getRecordNumDrivers(out int n, out _), "record drivers");
        for (int i = 0; i < n; i++)
        {
            sys.getRecordDriverInfo(i, out string name, 256, out _, out _, out _, out _, out _);
            list.Add(name);
        }
        return list;
    }

    public List<string> Outputs()
    {
        var list = new List<string>();
        Ok(sys.getNumDrivers(out int n), "output drivers");
        for (int i = 0; i < n; i++)
        {
            sys.getDriverInfo(i, out string name, 256, out _, out _, out _, out _);
            list.Add(name);
        }
        return list;
    }

    public void SetOutput(int index)
    {
        if (index >= 0) Ok(sys.setDriver(index), "set output");
    }

    public void Start(int input)
    {
        Stop();
        Ok(sys.getRecordDriverInfo(input, out string name, 256, out _, out rate, out _, out recChannels, out var state), "input info");
        if ((state & FMOD.DRIVER_STATE.CONNECTED) == 0) throw new FmodException($"{name} is not connected");
        soundLen = (uint)rate; // 1 s
        var ex = new FMOD.CREATESOUNDEXINFO
        {
            cbsize = Marshal.SizeOf<FMOD.CREATESOUNDEXINFO>(),
            numchannels = recChannels,
            format = FMOD.SOUND_FORMAT.PCM16,
            defaultfrequency = rate,
            length = soundLen * (uint)recChannels * 2,
        };
        Ok(sys.createSound("", FMOD.MODE.LOOP_NORMAL | FMOD.MODE.OPENUSER | FMOD.MODE._3D, ref ex, out rec), "record buffer");
        Ok(sys.recordStart(input, rec, true), "record start");
        recDriver = input;
        target = (uint)(rate * LatencyMs / 1000);
        recorded = played = 0; lastRec = lastPlay = 0; latencyAvg = target; started = false;
        Status = $"Listening to {name} ({rate} Hz, {recChannels} ch)";
    }

    public void Stop()
    {
        StopVoices();
        if (recDriver >= 0) { sys.recordStop(recDriver); recDriver = -1; }
        if (rec.hasHandle()) { rec.release(); rec.clearHandle(); }
        InputLevel = OutputLevel = 0;
        Status = "Stopped";
    }

    void StopVoices()
    {
        foreach (var v in voices)
        {
            v.Ch.stop();
            foreach (var d in v.Chain) d.Release();
        }
        voices.Clear();
        started = false;
    }

    // The chain layout; when it changes (speaker added, effect moved) every channel restarts, aligned again.
    static string Shape(Preset p) => string.Join("|", p.Speakers.Select(s => string.Join(",", s.Chain.Select(e => e.Type))));

    public void Update(Preset p, float dt)
    {
        try { Tick(p, dt); }
        catch (Exception e)
        {
            Stop();
            Status = "Stopped: " + e.Message;
        }
        sys.update();
    }

    void Tick(Preset p, float dt)
    {
        var r = p.Room;
        var pos = new FMOD.VECTOR { x = r.ListenerX, y = r.ListenerY, z = r.ListenerZ };
        float yaw = r.ListenerYaw * MathF.PI / 180;
        var fwd = new FMOD.VECTOR { x = MathF.Sin(yaw), y = 0, z = MathF.Cos(yaw) };
        var up = new FMOD.VECTOR { x = 0, y = 1, z = 0 };
        var zero = new FMOD.VECTOR();
        sys.set3DListenerAttributes(0, ref pos, ref zero, ref fwd, ref up);
        ApplyRoomReverb(p.Reverb);
        master.setVolume(Math.Max(0, p.Master));
        if (!Running) return;

        Ok(sys.getRecordPosition(recDriver, out uint rp), "record position");
        uint d = rp >= lastRec ? rp - lastRec : rp + soundLen - lastRec;
        lastRec = rp; recorded += d;
        InputLevel = Peak(rp);

        var s = Shape(p);
        if (!ReferenceEquals(p, playing) || s != shape) { StopVoices(); playing = p; shape = s; }
        if (!started)
        {
            if (recorded < target * 2) return; // let the buffer fill first
            StartVoices(p, rp);
        }

        // drift: keep the play head `target` samples behind the record head
        voices[0].Ch.getPosition(out uint pp, FMOD.TIMEUNIT.PCM);
        uint pd = pp >= lastPlay ? pp - lastPlay : pp + soundLen - lastPlay;
        lastPlay = pp; played += pd;
        long lat = recorded - played;
        if (lat < 0 || lat > soundLen / 2) { StopVoices(); return; } // lost (device hiccup): restart aligned
        latencyAvg = latencyAvg * .97 + lat * .03;
        float freq = rate * (float)(1 + Math.Clamp((latencyAvg - target) / target * .01, -.005, .005));
        foreach (var v in voices) v.Ch.setFrequency(freq);

        bool anySolo = p.Speakers.Any(x => x.Solo);
        float k = Math.Min(1, dt * 6); // the mod's smoothing rate (Fx.Update)
        if (Levels.Length != voices.Count) Levels = new float[voices.Count];
        for (int i = 0; i < voices.Count; i++) ApplySpeaker(p, p.Speakers[i], voices[i], k, anySolo, i);

        if (meter.getMeteringInfo(IntPtr.Zero, out var mi) == FMOD.RESULT.OK)
            OutputLevel = MaxPeak(mi);
    }

    void StartVoices(Preset p, uint rp)
    {
        group.setPaused(true);
        uint start = (rp + soundLen - target) % soundLen;
        foreach (var s in p.Speakers)
        {
            var v = new Voice();
            Ok(sys.playSound(rec, group, true, out v.Ch), "play");
            v.Ch.setMode(FMOD.MODE._3D_INVERSEROLLOFF);
            v.Ch.setPosition(start, FMOD.TIMEUNIT.PCM);
            v.Ch.setReverbProperties(0, 1);
            v.Ch.setPaused(false); // still held by the paused group
            // Added at the tail one by one from the last effect, so the signal runs first effect -> last -> 3D panner, as in Unity.
            v.Chain = s.Chain.Select(e => Dsp.Create(sys, e.Type)).ToList();
            foreach (var dsp in Enumerable.Reverse(v.Chain)) Ok(v.Ch.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, dsp.Handle), "add DSP");
            if (v.Ch.getDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD, out v.Meter) == FMOD.RESULT.OK) v.Meter.setMeteringEnabled(false, true);
            voices.Add(v);
        }
        lastPlay = start; played = recorded - target; latencyAvg = target;
        started = true;
        group.setPaused(false);
    }

    void ApplySpeaker(Preset p, Speaker s, Voice v, float k, bool anySolo, int i)
    {
        v.Inside = Mix.IsInside(s, p.Room);
        var t = Mix.Targets(p, s, v.Inside);
        foreach (var (key, goal) in t)
        {
            var def = Effects.Find(key);
            if (!v.Cur.TryGetValue(key, out var c) || def == null || def.IsChoice) { v.Cur[key] = goal; continue; }
            v.Cur[key] = def.Log && c > 0 && goal > 0 ? MathF.Exp(MathF.Log(c) + (MathF.Log(goal) - MathF.Log(c)) * k) : c + (goal - c) * k;
        }
        var cur = v.Cur;
        v.Ch.setMute(s.Mute || (anySolo && !s.Solo));
        v.Ch.setVolume(Math.Max(0, cur["Volume"]));
        var sp = new FMOD.VECTOR { x = s.X, y = s.Y, z = s.Z };
        var zero = new FMOD.VECTOR();
        v.Ch.set3DAttributes(ref sp, ref zero);
        v.Ch.set3DMinMaxDistance(Math.Max(.01f, cur["MinDistance"]), Math.Max(cur["MinDistance"] + .01f, cur["MaxDistance"]));
        v.Ch.set3DSpread(Math.Clamp(cur["Spread"], 0, 360));
        v.Ch.set3DLevel(Math.Clamp(cur["Level3D"], 0, 1));
        var seen = new HashSet<string>();
        for (int j = 0; j < s.Chain.Count; j++)
        {
            var e = s.Chain[j];
            bool first = seen.Add(e.Type); // later effects of the same type use their own values, untouched by macros
            v.Chain[j].Apply(n => first && cur.TryGetValue(e.Type + "." + n, out var x) ? x : e.Get(n), e.Bypass);
        }
        if (v.Meter.hasHandle() && v.Meter.getMeteringInfo(IntPtr.Zero, out var mi) == FMOD.RESULT.OK) Levels[i] = MaxPeak(mi);
    }

    public bool IsInside(int speaker) => speaker < voices.Count && voices[speaker].Inside;

    string reverbKey = "";
    void ApplyRoomReverb(RoomReverb r)
    {
        string key = Store.ToJson(r);
        if (key == reverbKey) return;
        reverbKey = key;
        var props = r.On
            ? new FMOD.REVERB_PROPERTIES(r.DecayTime, r.EarlyDelay, r.LateDelay, r.HFReference, r.HFDecayRatio, r.Diffusion, r.Density,
                r.LowShelfFrequency, r.LowShelfGain, r.HighCut, r.EarlyLateMix, r.WetLevel)
            : FMOD.PRESET.OFF();
        Ok(sys.setReverbProperties(0, ref props), "room reverb");
    }

    static float MaxPeak(FMOD.DSP_METERING_INFO mi)
    {
        float m = 0;
        for (int c = 0; c < mi.numchannels && c < mi.peaklevel.Length; c++) m = Math.Max(m, mi.peaklevel[c]);
        return m;
    }

    // Peak of the last ~20 ms recorded, 0..1.
    float Peak(uint rp)
    {
        uint frames = (uint)(rate / 50), from = (rp + soundLen - frames) % soundLen;
        int bpf = recChannels * 2;
        if (rec.@lock(from * (uint)bpf, frames * (uint)bpf, out var p1, out var p2, out var l1, out var l2) != FMOD.RESULT.OK) return 0;
        int peak = 0;
        try
        {
            foreach (var (ptr, len) in new[] { (p1, l1), (p2, l2) })
                for (int o = 0; ptr != IntPtr.Zero && o + 1 < len; o += 2) peak = Math.Max(peak, Math.Abs((int)Marshal.ReadInt16(ptr, o)));
        }
        finally { rec.unlock(p1, p2, l1, l2); }
        return peak / 32768f;
    }

    public void Dispose()
    {
        Stop();
        group.release();
        sys.close();
        sys.release();
    }
}
