using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ClubEQ.Audio;

// Voicemeeter's Remote API as an insert on one input strip: Voicemeeter calls us on its own audio thread with every
// strip's samples, we take the strip's L/R, mix the room from it (Engine clocked, FMOD offline) and write the mix
// back in its place. The strip then carries ClubEQ instead of the dry sound (no doubled sound, no loopback, no Output
// device), and the delay is Voicemeeter's buffer plus about one FMOD block.
public sealed class Voicemeeter : IDisposable
{
    const string Dll = "VoicemeeterRemote64.dll";
    [DllImport(Dll)] static extern int VBVMR_Login();
    [DllImport(Dll)] static extern int VBVMR_Logout();
    [DllImport(Dll)] static extern int VBVMR_GetVoicemeeterType(out int type);
    [DllImport(Dll)] static extern int VBVMR_IsParametersDirty();
    [DllImport(Dll, CharSet = CharSet.Ansi)] static extern int VBVMR_GetParameterStringW(string name, [Out] byte[] value); // 512 UTF-16 chars
    [DllImport(Dll, CharSet = CharSet.Ansi)] static extern int VBVMR_AudioCallbackRegister(int mode, Callback cb, IntPtr user, string clientName);
    [DllImport(Dll)] static extern int VBVMR_AudioCallbackStart();
    [DllImport(Dll)] static extern int VBVMR_AudioCallbackStop();
    [DllImport(Dll)] static extern int VBVMR_AudioCallbackUnregister();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Callback(IntPtr user, int command, IntPtr data, int unused);
    const int ModeInput = 1, ModeOutput = 2, BufferIn = 10, BufferOut = 11;

    static bool loggedIn;

    // Loads the DLL from Voicemeeter's folder and logs in once per process (logging out ends every session).
    static void Login()
    {
        if (loggedIn) return;
        string dir = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}",
            "UninstallString", null) is string u ? Path.GetDirectoryName(u.Trim('"')) ?? "" : "";
        if (!File.Exists(Path.Combine(dir, Dll))) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VB", "Voicemeeter");
        if (!NativeLibrary.TryLoad(Path.Combine(dir, Dll), out _)) throw new FmodException($"Voicemeeter's {Dll} not found (is Voicemeeter installed?)");
        int r = VBVMR_Login();
        if (r < 0) throw new FmodException($"Voicemeeter login failed ({r})");
        loggedIn = true;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => VBVMR_Logout();
        if (r == 1) throw new FmodException("Voicemeeter isn't running");
    }

    // Input strips in the callback's channel order: hardware strips 2 channels each, then virtual strips 8 each.
    static (int Hw, int Virt) Layout()
    {
        if (VBVMR_GetVoicemeeterType(out int t) != 0) throw new FmodException("Voicemeeter isn't running");
        return t switch { 1 => (2, 1), 2 => (3, 2), _ => (5, 3) }; // Basic, Banana, Potato
    }

    static int FirstChannel(int strip, int hw) => strip < hw ? strip * 2 : hw * 2 + (strip - hw) * 8;

    static readonly string[] VirtNames = { "Voicemeeter Input", "Voicemeeter AUX", "Voicemeeter VAIO3" };

    // "6: vm5 Browser (Voicemeeter Input)" per strip; empty when Voicemeeter isn't there.
    public static List<string> Strips()
    {
        var list = new List<string>();
        try
        {
            Login();
            var (hw, virt) = Layout();
            VBVMR_IsParametersDirty(); // the API wants this once before reading parameters
            var buf = new byte[1024];
            for (int i = 0; i < hw + virt; i++)
            {
                Array.Clear(buf);
                string label = VBVMR_GetParameterStringW($"Strip[{i}].Label", buf) == 0 ? Encoding.Unicode.GetString(buf).Split('\0')[0].Trim() : "";
                string kind = i < hw ? $"hardware input {i + 1}" : VirtNames[i - hw];
                list.Add(label.Length > 0 ? $"{i + 1}: {label} ({kind})" : $"{i + 1}: {kind}");
            }
        }
        catch (Exception) { /* not installed or not running: no strips to offer */ }
        return list;
    }

    // The strip to use when none was picked: the one the browser plays into, else the first virtual one.
    public static int DefaultStrip(List<string> strips)
    {
        int i = strips.FindIndex(s => s.Contains("Browser", StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : Math.Max(0, strips.FindIndex(s => s.Contains("Voicemeeter Input")));
    }

    readonly Engine en;
    readonly Callback cb; // held so the GC can't collect the delegate Voicemeeter calls
    readonly int ch;
    short[] pcm = new short[2048];
    float[] mix = new float[2048];
    bool registered;
    public volatile string Note; // shown instead of the engine status while set

    // Measuring (--vm-latency): the strip's input and bus A1's left channel, sample by sample on Voicemeeter's clock.
    public sealed class Probe(int frames)
    {
        public readonly float[] In = new float[frames], Out = new float[frames];
        public long InAt, OutAt; // frames seen so far
    }
    readonly Probe probe;

    public Voicemeeter(Engine engine, int strip, string name, Probe probe = null)
    {
        this.probe = probe;
        en = engine;
        Login();
        var (hw, virt) = Layout();
        if (strip < 0 || strip >= hw + virt) throw new FmodException($"strip {strip + 1} doesn't exist (this Voicemeeter has {hw + virt})");
        ch = FirstChannel(strip, hw);
        en.StartClocked(48000, 2, "Voicemeeter strip " + name);
        cb = OnAudio;
        int r = VBVMR_AudioCallbackRegister(probe == null ? ModeInput : ModeInput | ModeOutput, cb, IntPtr.Zero, "ClubEQ");
        if (r == 1) throw new FmodException("another app already uses Voicemeeter's input insert");
        if (r != 0) throw new FmodException($"Voicemeeter refused the audio callback ({r})");
        registered = true;
        if ((r = VBVMR_AudioCallbackStart()) != 0) { Dispose(); throw new FmodException($"Voicemeeter didn't start the audio callback ({r})"); }
    }

    unsafe int OnAudio(IntPtr user, int cmd, IntPtr data, int unused)
    {
        if (cmd != BufferIn && cmd != BufferOut) return 0;
        try
        {
            // VBVMR_T_AUDIOBUFFER: sr, nbs (frames), nbi, nbo, float* r[128], float* w[128]
            int sr = *(int*)data, nbs = *(int*)(data + 4), nbi = *(int*)(data + 8);
            var r = (float**)(data + 16);
            var w = (float**)(data + 16 + 128 * IntPtr.Size);
            for (int i = 0; i < nbi; i++)
                if (r[i] != w[i]) Buffer.MemoryCopy(r[i], w[i], nbs * 4L, nbs * 4L); // the other strips (buses) pass untouched
            if (probe != null)
            {
                bool input = cmd == BufferIn;
                var buf = input ? probe.In : probe.Out;
                long at = input ? probe.InAt : probe.OutAt;
                float* src = r[input ? ch : 0]; // the strip's left / bus A1 left
                for (int i = 0; i < nbs && at + i < buf.Length; i++) buf[at + i] = src[i];
                if (input) probe.InAt += nbs; else probe.OutAt += nbs;
            }
            if (cmd != BufferIn) return 0;
            // shortcut: the mix runs at 48 kHz only; supporting others means an Engine at Voicemeeter's rate
            if (sr != 48000) { Note = $"Voicemeeter runs at {sr} Hz; ClubEQ's insert needs 48000 (the strip plays dry)."; return 0; }
            if (ch + 1 >= nbi || !en.Running) return 0;
            Note = null;
            if (pcm.Length < nbs * 2) { pcm = new short[nbs * 2]; mix = new float[nbs * 2]; }
            float* l = r[ch], rr = r[ch + 1];
            for (int i = 0; i < nbs; i++)
            {
                pcm[i * 2] = (short)Math.Clamp(l[i] * 32767f, -32768f, 32767f);
                pcm[i * 2 + 1] = (short)Math.Clamp(rr[i] * 32767f, -32768f, 32767f);
            }
            en.Write(pcm, nbs);
            en.Render(mix, nbs);
            float* wl = w[ch], wr = w[ch + 1];
            for (int i = 0; i < nbs; i++) { wl[i] = mix[i * 2]; wr[i] = mix[i * 2 + 1]; }
        }
        catch (Exception e) { Note = "Voicemeeter insert: " + e.Message; } // never throw into Voicemeeter's thread
        return 0;
    }

    public void Dispose()
    {
        if (!registered) return;
        VBVMR_AudioCallbackStop();
        VBVMR_AudioCallbackUnregister();
        registered = false;
    }
}
