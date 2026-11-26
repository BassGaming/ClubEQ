using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ClubEQ.Model;

namespace ClubEQ;

// Measuring CATCAR against ClubEQ with exponential sine sweeps (Farina): play the same sweep through both, record what
// comes out, deconvolve into impulse responses, compare EQ, reverb and arrivals. Command-line only:
//   --serve [port]                       serve sweep.wav at http://127.0.0.1:<port>/sweep.wav (for the Radio mod)
//   --record "<device part>" <s> <out>   record a device's loopback to a WAV
//   --measure-clubeq <out> <x> <z> <yaw> [preset] [y]   run the sweep through ClubEQ and record its output
//   --analyze <a.wav> [b.wav]            impulse responses + comparison (writes <a>.ir.wav)
static class Measure
{
    public const int Rate = 48000;
    const double F1 = 20, F2 = 20000, T = 6;     // sweep: 20 Hz .. 20 kHz in 6 s
    const double Lead = 1, Gap = 2.5, Tail = 3;   // silence before, between and after (reverb tails die out)
    const float Amp = .3f;                         // -10 dBFS

    static string Dir => Path.Combine(Store.Home, "Measure");
    public static string SweepPath => Path.Combine(Dir, "sweep.wav");

    static double[] SweepMono()
    {
        int n = (int)(T * Rate);
        var s = new double[n];
        double k = Math.Log(F2 / F1);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double fade = Math.Min(1, Math.Min(i, n - 1 - i) / (0.05 * Rate)); // 50 ms fades
            s[i] = Math.Sin(2 * Math.PI * F1 * T / k * (Math.Exp(t * k / T) - 1)) * fade;
        }
        return s;
    }

    // Stereo file: lead silence, sweep on the left only, gap, sweep on the right only, tail.
    static float[,] SweepStereo()
    {
        var m = SweepMono();
        int lead = (int)(Lead * Rate), gap = (int)(Gap * Rate), tail = (int)(Tail * Rate);
        int total = lead + m.Length + gap + m.Length + tail;
        var x = new float[total, 2];
        for (int i = 0; i < m.Length; i++)
        {
            x[lead + i, 0] = (float)(m[i] * Amp);
            x[lead + m.Length + gap + i, 1] = (float)(m[i] * Amp);
        }
        return x;
    }

    public static string EnsureSweep()
    {
        Directory.CreateDirectory(Dir);
        if (!File.Exists(SweepPath)) WriteWav(SweepPath, SweepStereo(), 16);
        return SweepPath;
    }

    // ---------- WAV ----------
    public static void WriteWav(string path, float[,] x, int bits)
    {
        int n = x.GetLength(0), ch = x.GetLength(1), bps = bits / 8;
        using var w = new BinaryWriter(File.Create(path));
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * ch * bps); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)(bits == 32 ? 3 : 1)); w.Write((short)ch); w.Write(Rate); w.Write(Rate * ch * bps); w.Write((short)(ch * bps)); w.Write((short)bits);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * ch * bps);
        for (int i = 0; i < n; i++)
            for (int c = 0; c < ch; c++)
                if (bits == 32) w.Write(x[i, c]);
                else w.Write((short)Math.Clamp(Math.Round(x[i, c] * 32767), -32768, 32767));
    }

    public static float[,] ReadWav(string path)
    {
        var b = File.ReadAllBytes(path);
        int p = 12, ch = 2, bits = 16, fmt = 1;
        while (p + 8 <= b.Length)
        {
            string id = Encoding.ASCII.GetString(b, p, 4);
            int len = BitConverter.ToInt32(b, p + 4);
            if (id == "fmt ") { fmt = BitConverter.ToInt16(b, p + 8); ch = BitConverter.ToInt16(b, p + 10); bits = BitConverter.ToInt16(b, p + 22); }
            if (id == "data")
            {
                int bps = bits / 8, n = Math.Min(len, b.Length - p - 8) / (bps * ch);
                var x = new float[n, ch];
                for (int i = 0; i < n; i++)
                    for (int c = 0; c < ch; c++)
                    {
                        int o = p + 8 + (i * ch + c) * bps;
                        x[i, c] = fmt == 3 ? BitConverter.ToSingle(b, o) : BitConverter.ToInt16(b, o) / 32768f;
                    }
                return x;
            }
            p += 8 + len + (len & 1);
        }
        throw new InvalidDataException("no data chunk in " + path);
    }

    // ---------- serve ----------
    public static int Serve(int port)
    {
        var file = File.ReadAllBytes(EnsureSweep());
        var l = new TcpListener(IPAddress.Loopback, port);
        l.Start();
        Console.WriteLine($"Serving the sweep at http://127.0.0.1:{port}/sweep.wav ({file.Length / 1024} KB). Ctrl+C stops.");
        while (true)
        {
            using var c = l.AcceptTcpClient();
            try
            {
                var s = c.GetStream();
                var buf = new byte[4096];
                int n = s.Read(buf, 0, buf.Length);
                string req = Encoding.ASCII.GetString(buf, 0, n).Split('\n')[0].Trim();
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} {req}");
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: audio/wav\r\nContent-Length: {file.Length}\r\nConnection: close\r\n\r\n");
                s.Write(head);
                if (!req.StartsWith("HEAD")) s.Write(file);
            }
            catch (Exception e) { Console.WriteLine("client: " + e.Message); }
        }
    }

    // ---------- record ----------
    public static float[,] Record(string devicePart, double seconds, Func<bool> tick = null)
    {
        Audio.Engine.Ok(FMOD.Factory.System_Create(out var sys), "record system");
        sys.init(8, FMOD.INITFLAGS.NORMAL, IntPtr.Zero);
        try
        {
            sys.getRecordNumDrivers(out int nd, out _);
            int id = -1;
            for (int i = 0; i < nd; i++)
            {
                sys.getRecordDriverInfo(i, out string name, 256, out _, out _, out _, out _, out _);
                if (name.Contains(devicePart, StringComparison.OrdinalIgnoreCase) && (id < 0 || name.Contains("loopback"))) id = i;
            }
            if (id < 0) throw new ArgumentException("No recording device matching " + devicePart);
            sys.getRecordDriverInfo(id, out string dn, 256, out _, out int rate, out _, out int chs, out _);
            if (rate != Rate) Console.WriteLine($"warning: {dn} runs at {rate} Hz, not {Rate}");
            uint len = (uint)rate;
            var ex = new FMOD.CREATESOUNDEXINFO { cbsize = Marshal.SizeOf<FMOD.CREATESOUNDEXINFO>(), numchannels = chs, format = FMOD.SOUND_FORMAT.PCMFLOAT, defaultfrequency = rate, length = len * (uint)chs * 4 };
            Audio.Engine.Ok(sys.createSound("", FMOD.MODE.LOOP_NORMAL | FMOD.MODE.OPENUSER, ref ex, out var snd), "record buffer");
            Audio.Engine.Ok(sys.recordStart(id, snd, true), "record start");
            Console.WriteLine($"Recording {dn} ({chs} ch) for {seconds:0} s...");
            var data = new List<float>();
            uint last = 0;
            long want = (long)(seconds * rate);
            var start = DateTime.Now;
            // loopback delivers nothing while the device is silent: run on wall time, padding silence
            while ((DateTime.Now - start).TotalSeconds < seconds)
            {
                Thread.Sleep(10);
                sys.update();
                tick?.Invoke();
                sys.getRecordPosition(id, out uint pos);
                if (pos == last) continue;
                uint count = pos > last ? pos - last : pos + len - last;
                if (snd.@lock(last * (uint)chs * 4, count * (uint)chs * 4, out var p1, out var p2, out var l1, out var l2) == FMOD.RESULT.OK)
                {
                    foreach (var (ptr, l) in new[] { (p1, l1), (p2, l2) })
                    {
                        if (ptr == IntPtr.Zero) continue;
                        var tmp = new float[l / 4];
                        Marshal.Copy(ptr, tmp, 0, tmp.Length);
                        data.AddRange(tmp);
                    }
                    snd.unlock(p1, p2, l1, l2);
                }
                last = pos;
            }
            sys.recordStop(id);
            snd.release();
            int frames = data.Count / chs;
            var x = new float[frames, 2];
            for (int i = 0; i < frames; i++) { x[i, 0] = data[i * chs]; x[i, 1] = data[i * chs + Math.Min(1, chs - 1)]; }
            return x;
        }
        finally { sys.close(); sys.release(); }
    }

    public static int RecordCmd(string dev, double seconds, string outPath)
    {
        var x = Record(dev, seconds);
        WriteWav(outPath, x, 32);
        Console.WriteLine($"Wrote {outPath} ({x.GetLength(0) / (double)Rate:0.0} s).");
        return 0;
    }

    // ---------- ClubEQ side ----------
    // Plays the sweep into vm5 Browser, runs the engine (saved preset, listener at x/z/yaw) and records its output
    // from the output device's loopback, so both measurements go through the same Voicemeeter path.
    public static int MeasureClubEQ(string outPath, float x, float z, float yaw, string presetName, float y, string input = "vm5 Browser", string output = "Voicemeeter Def")
    {
        var preset = Store.LoadPresets().FirstOrDefault(p => p.Name == presetName) ?? throw new ArgumentException("No preset " + presetName);
        preset.Room.ListenerX = x; preset.Room.ListenerZ = z; preset.Room.ListenerYaw = yaw; preset.Room.ListenerY = y;
        using var en = new Audio.Engine();
        int ii = en.Inputs().FindIndex(n => n.Contains(input, StringComparison.OrdinalIgnoreCase));
        int oi = en.Outputs().FindIndex(n => n.Contains(output, StringComparison.OrdinalIgnoreCase));
        if (ii < 0 || oi < 0) { Console.WriteLine("devices not found"); return 1; }
        en.SetOutput(oi);
        en.Start(ii);

        // the sweep, played into the input device by a second FMOD system
        Audio.Engine.Ok(FMOD.Factory.System_Create(out var ps), "player");
        ps.getNumDrivers(out int nd);
        for (int k = 0; k < nd; k++) { ps.getDriverInfo(k, out string name, 256, out _, out _, out _, out _); if (name.Contains(input, StringComparison.OrdinalIgnoreCase)) ps.setDriver(k); }
        ps.init(8, FMOD.INITFLAGS.NORMAL, IntPtr.Zero);
        Audio.Engine.Ok(ps.createSound(EnsureSweep(), FMOD.MODE.DEFAULT | FMOD.MODE.CREATESAMPLE, out var snd), "load sweep");

        // warm up 2 s so the engine runs, then record while the sweep plays
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double lastT = 0;
        bool Tick()
        {
            double t = sw.Elapsed.TotalSeconds;
            en.Update(preset, (float)Math.Min(.1, t - lastT)); lastT = t;
            ps.update();
            return true;
        }
        while (sw.Elapsed.TotalSeconds < 2) { Tick(); Thread.Sleep(10); }
        Console.WriteLine(en.Status);
        double dur = (double)SweepStereo().GetLength(0) / Rate + 1.5;
        FMOD.Channel ch = default;
        bool started = false;
        var rec = Record(output, dur + 1, () => { if (!started) { ps.playSound(snd, default, false, out ch); started = true; } return Tick(); });
        snd.release(); ps.close(); ps.release();
        WriteWav(outPath, rec, 32);
        Console.WriteLine($"Wrote {outPath}.");
        return 0;
    }

    // ---------- analysis ----------
    static void Fft(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double a = 2 * Math.PI / len * (inverse ? 1 : -1);
            double wr = Math.Cos(a), wi = Math.Sin(a);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int u = i + k, v = i + k + len / 2;
                    double tr = re[v] * cr - im[v] * ci, ti = re[v] * ci + im[v] * cr;
                    re[v] = re[u] - tr; im[v] = im[u] - ti; re[u] += tr; im[u] += ti;
                    double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                }
            }
        }
        if (inverse) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
    }

    // Inverse filter of the sweep: time-reversed with a -6 dB/octave envelope (Farina), scaled so sweep * inv = impulse.
    static double[] Inverse()
    {
        var s = SweepMono();
        int n = s.Length;
        double k = Math.Log(F2 / F1);
        var inv = new double[n];
        for (int i = 0; i < n; i++) inv[i] = s[n - 1 - i] * Math.Exp(-((double)i / Rate) * k / T);
        // normalise at 1 kHz: the sweep convolved with its inverse must give gain 1 there
        var c = Convolve(s, inv);
        double peak = c.Max(Math.Abs);
        for (int i = 0; i < n; i++) inv[i] /= peak;
        return inv;
    }

    static double[] Convolve(double[] a, double[] b)
    {
        int n = 1;
        while (n < a.Length + b.Length) n <<= 1;
        double[] ar = new double[n], ai = new double[n], br = new double[n], bi = new double[n];
        Array.Copy(a, ar, a.Length); Array.Copy(b, br, b.Length);
        Fft(ar, ai, false); Fft(br, bi, false);
        for (int i = 0; i < n; i++) { double r = ar[i] * br[i] - ai[i] * bi[i]; ai[i] = ar[i] * bi[i] + ai[i] * br[i]; ar[i] = r; }
        Fft(ar, ai, true);
        var o = new double[a.Length + b.Length - 1];
        Array.Copy(ar, o, o.Length);
        return o;
    }

    // The four impulse responses (input L/R -> output L/R), each 1.5 s from 10 ms before its direct sound.
    public sealed class Irs { public double[] LL, LR, RL, RR; }

    public static Irs Responses(float[,] rec)
    {
        int n = rec.GetLength(0);
        var inv = Inverse();
        double[] l = new double[n], r = new double[n];
        for (int i = 0; i < n; i++) { l[i] = rec[i, 0]; r[i] = rec[i, 1]; }
        var dl = Convolve(l, inv);
        var dr = Convolve(r, inv);
        // the left sweep's response peaks first; the right sweep's comes (sweep + gap) later
        int offset = (int)((T + Gap) * Rate), win = (int)(1.5 * Rate), pre = (int)(.01 * Rate);
        int best = 0; double bestE = -1;
        for (int i = 0; i + offset + win < dl.Length; i += 16)
        {
            double e = dl[i] * dl[i] + dr[i] * dr[i] + dl[i + offset] * dl[i + offset] + dr[i + offset] * dr[i + offset];
            if (e > bestE) { bestE = e; best = i; }
        }
        // refine to the sample
        for (int i = Math.Max(0, best - 16); i < best + 16; i++)
        {
            double e = dl[i] * dl[i] + dr[i] * dr[i] + dl[i + offset] * dl[i + offset] + dr[i + offset] * dr[i + offset];
            if (e > bestE) { bestE = e; best = i; }
        }
        double[] Cut(double[] d, int at) { var o = new double[win]; for (int i = 0; i < win; i++) { int j = at - pre + i; o[i] = j >= 0 && j < d.Length ? d[j] : 0; } return o; }
        // the earliest arrival can come before the strongest: start the window at the first sample within -30 dB of the peak
        double pk = Math.Sqrt(bestE);
        int first = best;
        for (int i = Math.Max(0, best - (int)(.06 * Rate)); i < best; i++)
            if (Math.Sqrt(dl[i] * dl[i] + dr[i] * dr[i]) > pk * .0316) { first = i; break; }
        return new Irs { LL = Cut(dl, first), LR = Cut(dr, first), RL = Cut(dl, first + offset), RR = Cut(dr, first + offset) };
    }

    static readonly double[] Bands = { 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000 };

    // Third-octave band levels (dB) of an impulse response.
    static double[] BandLevels(double[] ir)
    {
        int n = 1;
        while (n < ir.Length) n <<= 1;
        double[] re = new double[n], im = new double[n];
        Array.Copy(ir, re, ir.Length);
        Fft(re, im, false);
        var o = new double[Bands.Length];
        for (int b = 0; b < Bands.Length; b++)
        {
            double lo = Bands[b] / Math.Pow(2, 1 / 6.0), hi = Bands[b] * Math.Pow(2, 1 / 6.0), e = 0;
            int i0 = (int)(lo * n / Rate), i1 = Math.Max(i0 + 1, (int)(hi * n / Rate));
            for (int i = i0; i < i1 && i < n / 2; i++) e += re[i] * re[i] + im[i] * im[i];
            o[b] = 10 * Math.Log10(e / (i1 - i0) + 1e-20);
        }
        return o;
    }

    // Early decay time and T20 (s) from the Schroeder backward integral, broadband.
    static (double edt, double t20) Decay(double[] a, double[] b)
    {
        int n = a.Length;
        var e = new double[n];
        double sum = 0;
        for (int i = n - 1; i >= 0; i--) { sum += a[i] * a[i] + b[i] * b[i]; e[i] = sum; }
        double Db(int i) => 10 * Math.Log10(e[i] / e[0] + 1e-20);
        int At(double db) { for (int i = 0; i < n; i++) if (Db(i) <= db) return i; return n - 1; }
        double edt = (At(-10) - At(0)) / (double)Rate * 6, t20 = (At(-25) - At(-5)) / (double)Rate * 3;
        return (edt, t20);
    }

    // Arrivals in the first 80 ms: local peaks of the envelope within 20 dB of the strongest.
    static List<(double ms, double db)> Arrivals(double[] a, double[] b)
    {
        int n = (int)(.09 * Rate);
        var env = new double[n];
        for (int i = 0; i < n; i++) env[i] = Math.Sqrt(a[i] * a[i] + b[i] * b[i]);
        double max = env.Max();
        var o = new List<(double, double)>();
        int w = (int)(.0015 * Rate);
        for (int i = w; i < n - w; i++)
        {
            if (env[i] < max * .1) continue;
            bool peak = true;
            for (int j = i - w; j <= i + w && peak; j++) if (env[j] > env[i]) peak = false;
            if (peak) o.Add(((i - .01 * Rate) * 1000.0 / Rate, 20 * Math.Log10(env[i] / max)));
        }
        return o;
    }

    public static int Analyze(string a, string b)
    {
        var A = Responses(ReadWav(a));
        SaveIr(a, A);
        Irs B = null;
        if (b != null) { B = Responses(ReadWav(b)); SaveIr(b, B); }

        // levels relative to each measurement's own broadband level, so a gain difference doesn't hide the shape
        var la = new[] { A.LL, A.LR, A.RL, A.RR }.Select(BandLevels).ToArray();
        var lb = B == null ? null : new[] { B.LL, B.LR, B.RL, B.RR }.Select(BandLevels).ToArray();
        // left ear = LL + RL, right ear = LR + RR (power), for the EQ; crosstalk shown separately
        double[] Ear(double[][] l, int x, int y) => l[x].Zip(l[y], (p, q) => 10 * Math.Log10(Math.Pow(10, p / 10) + Math.Pow(10, q / 10))).ToArray();
        var aL = Ear(la, 0, 2); var aR = Ear(la, 1, 3);
        double refA = (aL.Skip(9).Take(16).Average() + aR.Skip(9).Take(16).Average()) / 2;
        Console.WriteLine();
        Console.WriteLine(b == null ? $"{Path.GetFileName(a)}" : $"A = {Path.GetFileName(a)}   B = {Path.GetFileName(b)}   (dB, each relative to its own 250 Hz-8 kHz average)");
        if (lb == null)
        {
            Console.WriteLine("  band     left   right");
            for (int i = 0; i < Bands.Length; i++) Console.WriteLine($"{Bands[i],7:0}  {aL[i] - refA,6:0.0}  {aR[i] - refA,6:0.0}");
        }
        else
        {
            var bL = Ear(lb, 0, 2); var bR = Ear(lb, 1, 3);
            double refB = (bL.Skip(9).Take(16).Average() + bR.Skip(9).Take(16).Average()) / 2;
            Console.WriteLine("  band    A left  B left   diff  |  A right B right   diff");
            for (int i = 0; i < Bands.Length; i++)
                Console.WriteLine($"{Bands[i],7:0}  {aL[i] - refA,6:0.0}  {bL[i] - refB,6:0.0}  {(aL[i] - refA) - (bL[i] - refB),6:+0.0;-0.0}  |  {aR[i] - refA,6:0.0}  {bR[i] - refB,6:0.0}  {(aR[i] - refA) - (bR[i] - refB),6:+0.0;-0.0}");
            Console.WriteLine($"level: B is {refB - refA:+0.0;-0.0} dB vs A");
        }
        void Shape(string name, Irs x)
        {
            var (edt, t20) = Decay(x.LL.Zip(x.RL, (p, q) => p + q).ToArray(), x.LR.Zip(x.RR, (p, q) => p + q).ToArray());
            // crosstalk: how much of the left input reaches the right ear and back (0 dB = no separation)
            double El(double[] v) => v.Sum(s => s * s);
            double xL = 10 * Math.Log10(El(x.LR) / El(x.LL)), xR = 10 * Math.Log10(El(x.RL) / El(x.RR));
            Console.WriteLine($"{name}: EDT {edt:0.00} s  T20 {t20:0.00} s  | left input reaches the right ear at {xL:0.0} dB, right input the left ear at {xR:0.0} dB");
            Console.WriteLine($"   arrivals (L in): {string.Join("  ", Arrivals(x.LL, x.LR).Select(p => $"{p.ms:0.0}ms {p.db:0}dB"))}");
            Console.WriteLine($"   arrivals (R in): {string.Join("  ", Arrivals(x.RL, x.RR).Select(p => $"{p.ms:0.0}ms {p.db:0}dB"))}");
        }
        Shape("A", A);
        if (B != null) Shape("B", B);
        return 0;
    }

    static void SaveIr(string wav, Irs x)
    {
        int n = x.LL.Length;
        var o = new float[n, 4];
        double max = new[] { x.LL, x.LR, x.RL, x.RR }.Max(v => v.Max(Math.Abs));
        for (int i = 0; i < n; i++) { o[i, 0] = (float)(x.LL[i] / max); o[i, 1] = (float)(x.LR[i] / max); o[i, 2] = (float)(x.RL[i] / max); o[i, 3] = (float)(x.RR[i] / max); }
        WriteWav(Path.ChangeExtension(wav, ".ir.wav"), o, 32);
    }
}
