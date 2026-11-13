// Simon Flow for Windows — dictation with AI grammar cleanup.
// Press the shortcut (default Ctrl+Alt+Space) in any app, talk, press it again:
// Claude cleans up what you said and it is pasted where your cursor is.
//
// Built by "Build Simon Flow.bat" with the C# compiler that ships with Windows (C# 5).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SimonFlow
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new System.Threading.Mutex(true, "SimonFlow.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("Simon Flow is already running. Look for its icon near the clock.", "Simon Flow");
                    return;
                }
                try { if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware(); } catch { }
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) Dpi.Scale = Math.Max(1f, g.DpiX / 96f);
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool background = args.Contains("--background");
                Application.Run(new MainForm(background));
            }
        }
    }

    // Everything is laid out at 100% and multiplied by the screen's scale (125%, 150%, ...).
    static class Dpi
    {
        public static float Scale = 1f;
        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static float F(float v) { return v * Scale; }
        public static Point P(int x, int y) { return new Point(S(x), S(y)); }
        public static Size Z(int w, int h) { return new Size(S(w), S(h)); }
    }

    // ------------------------------------------------------------------ themes

    class Theme
    {
        public string Key, Name;
        public Color A, B, C, Accent;
        public Theme(string key, string name, Color a, Color b, Color c, Color accent)
        { Key = key; Name = name; A = a; B = b; C = c; Accent = accent; }

        public static readonly Theme[] All = new Theme[]
        {
            new Theme("purple", "Purple", Color.FromArgb(79, 69, 230), Color.FromArgb(140, 92, 245), Color.FromArgb(237, 71, 153), Color.FromArgb(107, 77, 237)),
            new Theme("ocean", "Ocean", Color.FromArgb(13, 84, 217), Color.FromArgb(26, 140, 230), Color.FromArgb(26, 199, 191), Color.FromArgb(18, 107, 217)),
            new Theme("sunset", "Sunset", Color.FromArgb(250, 128, 51), Color.FromArgb(245, 82, 102), Color.FromArgb(184, 51, 140), Color.FromArgb(235, 84, 77)),
            new Theme("forest", "Forest", Color.FromArgb(13, 107, 84), Color.FromArgb(33, 148, 102), Color.FromArgb(133, 194, 77), Color.FromArgb(20, 128, 92)),
            new Theme("graphite", "Graphite", Color.FromArgb(30, 30, 35), Color.FromArgb(18, 18, 21), Color.FromArgb(10, 10, 12), Color.FromArgb(70, 70, 80)),
            new Theme("midnight", "Midnight", Color.FromArgb(15, 18, 41), Color.FromArgb(36, 38, 82), Color.FromArgb(71, 56, 128), Color.FromArgb(64, 61, 140)),
        };

        public static Theme Find(string key)
        {
            foreach (var t in All) if (t.Key == key) return t;
            return All[0];
        }
    }

    // ------------------------------------------------------------------ settings

    public class Settings
    {
        public string ApiKey { get; set; }
        public string Style { get; set; }
        public int HotKeyMods { get; set; }
        public int HotKeyCode { get; set; }
        public string HotKeyLabel { get; set; }
        public bool Sounds { get; set; }
        public string ThemeKey { get; set; }     // a theme key, or "custom"
        public int CustomColor { get; set; }
        public string Culture { get; set; }
        public string StartSound { get; set; }
        public string StopSound { get; set; }
        public bool AlwaysShowPill { get; set; }
        public int SoundVolume { get; set; }
        public bool HoldToTalk { get; set; }
        public long WpmWords { get; set; }
        public double WpmSeconds { get; set; }
        public string MicName { get; set; }      // "" = Windows default
        public string TranslateTo { get; set; }  // "" = off

        public Settings()
        {
            ApiKey = "";
            Style = "light";
            HotKeyMods = (int)(Native.MOD_CONTROL | Native.MOD_ALT);
            HotKeyCode = (int)Keys.Space;
            HotKeyLabel = "Ctrl+Alt+Space";
            Sounds = true;
            SoundVolume = 70;
            MicName = "";
            TranslateTo = "";
            ThemeKey = "purple";
            CustomColor = Color.FromArgb(40, 48, 80).ToArgb();
            Culture = "en-US";
            StartSound = BaseSounds.PathFor("Chime Up");
            StopSound = BaseSounds.PathFor("Chime Down");
        }

        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimonFlow"); }
        }

        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        public static Settings Load()
        {
            try
            {
                var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    if (string.IsNullOrEmpty(s.ThemeKey)) s.ThemeKey = "purple";
                    return s;
                }
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this));
            }
            catch { }
        }

        // Colours derived from the chosen theme / custom colour
        public Color[] Gradient
        {
            get
            {
                if (ThemeKey == "custom")
                {
                    var c = Color.FromArgb(CustomColor);
                    return new[] { c, Draw.Darken(c, 0.15), Draw.Darken(c, 0.35) };
                }
                var t = Theme.Find(ThemeKey);
                return new[] { t.A, t.B, t.C };
            }
        }

        public Color Accent
        {
            get
            {
                if (ThemeKey == "custom")
                {
                    var c = Color.FromArgb(CustomColor);
                    return Draw.Luminance(c) > 0.7 ? Color.FromArgb(64, 64, 64) : c;
                }
                return Theme.Find(ThemeKey).Accent;
            }
        }
    }

    static class Log
    {
        public static void Write(string s)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.AppendAllText(Path.Combine(Settings.Dir, "log.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + s + Environment.NewLine);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ Win32

    static class Native
    {
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
        public const uint KEYEVENTF_KEYUP = 0x2;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern int mciSendString(string command, StringBuilder ret, int retLength, IntPtr hwnd);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        static extern bool mciGetErrorString(int error, StringBuilder text, int length);

        public static void Mci(string command)
        {
            int r = mciSendString(command, null, 0, IntPtr.Zero);
            if (r != 0)
            {
                var sb = new StringBuilder(256);
                mciGetErrorString(r, sb, 256);
                throw new Exception("Recording error (" + command.Split(' ')[0] + "): " + sb);
            }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Colours the window's title bar (Windows 11). Older Windows just ignores this.
        public static void TitleBar(IntPtr hwnd, Color caption, Color text)
        {
            try
            {
                int dark = Draw.Luminance(caption) < 0.6 ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4);                 // dark-mode buttons (Windows 10 too)
                int c = caption.R | (caption.G << 8) | (caption.B << 16);
                int t = text.R | (text.G << 8) | (text.B << 16);
                DwmSetWindowAttribute(hwnd, 35, ref c, 4);                    // caption colour
                DwmSetWindowAttribute(hwnd, 34, ref c, 4);                    // border colour
                DwmSetWindowAttribute(hwnd, 36, ref t, 4);                    // title text colour
            }
            catch { }
        }

        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        public static bool KeyDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        public static string MciStatus(string command)
        {
            var sb = new StringBuilder(64);
            int r = mciSendString(command, sb, 64, IntPtr.Zero);
            return r == 0 ? sb.ToString() : null;
        }

        [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct SZ { public int W, H; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLEND { public byte Op, Flags, Alpha, Format; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref PT pptDst, ref SZ psize,
            IntPtr hdcSrc, ref PT pprSrc, int crKey, ref BLEND pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);

        // Draws a bitmap with per-pixel transparency (smooth edges, soft shadow) onto a layered window.
        public static void PushLayered(Form f, Bitmap bmp, byte opacity)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr hbmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hbmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(mem, hbmp);
                var size = new SZ { W = bmp.Width, H = bmp.Height };
                var src = new PT { X = 0, Y = 0 };
                var dst = new PT { X = f.Left, Y = f.Top };
                var blend = new BLEND { Op = 0, Flags = 0, Alpha = opacity, Format = 1 };
                UpdateLayeredWindow(f.Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
                if (hbmp != IntPtr.Zero) { SelectObject(mem, old); DeleteObject(hbmp); }
                DeleteDC(mem);
            }
        }

        public static void MciQuiet(string command)
        {
            mciSendString(command, null, 0, IntPtr.Zero);
        }
    }

    // ------------------------------------------------------------------ built-in sounds
    // Simon Flow's own start/stop sounds, synthesized into %APPDATA%\SimonFlow\sounds the first time they're needed.

    static class BaseSounds
    {
        const int RATE = 44100;
        const string VERSION = "v1";

        public static string Dir { get { return Path.Combine(Settings.Dir, "sounds", "builtin-" + VERSION); } }

        // name -> list of notes: { startHz, endHz, seconds, startTime }
        static readonly string[] Names = { "Pop", "Chime Up", "Chime Down", "Bubble", "Blip", "Soft Tap" };

        static double[][] Notes(string name)
        {
            switch (name)
            {
                case "Pop":        return new[] { new[] { 660.0, 1100.0, 0.07, 0.0 } };
                case "Chime Up":   return new[] { new[] { 1046.5, 1046.5, 0.16, 0.0 }, new[] { 1568.0, 1568.0, 0.22, 0.08 } };
                case "Chime Down": return new[] { new[] { 1568.0, 1568.0, 0.16, 0.0 }, new[] { 1046.5, 1046.5, 0.22, 0.08 } };
                case "Bubble":     return new[] { new[] { 380.0, 950.0, 0.12, 0.0 } };
                case "Blip":       return new[] { new[] { 1200.0, 1200.0, 0.05, 0.0 }, new[] { 1800.0, 1800.0, 0.05, 0.06 } };
                default:           return new[] { new[] { 520.0, 300.0, 0.06, 0.0 } };   // Soft Tap
            }
        }

        public static string PathFor(string name) { return System.IO.Path.Combine(Dir, name + ".wav"); }

        // Makes sure every built-in sound exists and returns (name, path) pairs.
        public static List<KeyValuePair<string, string>> All()
        {
            var list = new List<KeyValuePair<string, string>>();
            try { Directory.CreateDirectory(Dir); } catch { }
            foreach (var n in Names)
            {
                string path = PathFor(n);
                try { if (!File.Exists(path)) Write(path, Notes(n)); }
                catch (Exception ex) { Log.Write("sound make failed: " + ex.Message); continue; }
                list.Add(new KeyValuePair<string, string>(n, path));
            }
            return list;
        }

        static void Write(string path, double[][] notes)
        {
            double total = 0;
            foreach (var nt in notes) total = Math.Max(total, nt[3] + nt[2]);
            int len = (int)((total + 0.02) * RATE);
            var mix = new double[len];
            foreach (var nt in notes)
            {
                int start = (int)(nt[3] * RATE), n = (int)(nt[2] * RATE);
                double phase = 0;
                for (int i = 0; i < n && start + i < len; i++)
                {
                    double x = (double)i / n;
                    double hz = nt[0] + (nt[1] - nt[0]) * x;
                    phase += 2 * Math.PI * hz / RATE;
                    double attack = Math.Min(1, i / (RATE * 0.004));          // 4 ms fade in (no click)
                    double env = attack * Math.Exp(-5.5 * x) * (1 - x);       // smooth decay
                    double tone = Math.Sin(phase) + 0.18 * Math.Sin(phase * 2) + 0.06 * Math.Sin(phase * 3);
                    mix[start + i] += tone * env;
                }
            }
            double peak = 0;
            foreach (var v in mix) peak = Math.Max(peak, Math.Abs(v));
            double gain = peak > 0 ? 0.45 / peak : 0;   // comfortable volume

            using (var f = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(f))
            {
                int bytes = len * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(RATE); w.Write(RATE * 2);
                w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                foreach (var v in mix) w.Write((short)Math.Max(-32767, Math.Min(32767, v * gain * 32767)));
            }
        }

        public static bool IsBuiltIn(string path)
        {
            return !string.IsNullOrEmpty(path) && path.StartsWith(Dir, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ------------------------------------------------------------------ microphone recorder (waveIn)
    // Records 16 kHz mono from the default mic, keeps a live level for the pill, saves a .wav for Whisper.

    class WaveRecorder : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, int deviceId, ref WAVEFORMATEX fmt, IntPtr cb, IntPtr inst, int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInStop(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int waveInGetDevCapsW(IntPtr id, ref WAVEINCAPS caps, int size);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WAVEINCAPS
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels, wReserved1;
        }

        // Names of the microphones Windows knows about, in device order.
        public static List<string> DeviceNames()
        {
            var list = new List<string>();
            try
            {
                int n = waveInGetNumDevs();
                for (int i = 0; i < n; i++)
                {
                    var caps = new WAVEINCAPS();
                    if (waveInGetDevCapsW(new IntPtr(i), ref caps, Marshal.SizeOf(typeof(WAVEINCAPS))) == 0)
                        list.Add(caps.szPname ?? ("Microphone " + (i + 1)));
                }
            }
            catch (Exception ex) { Log.Write("mic list failed: " + ex.Message); }
            return list;
        }

        // Device number for a saved mic name, or -1 (Windows default) if it isn't plugged in.
        public static int FindDevice(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            var names = DeviceNames();
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        const int RATE = 16000, BUF_BYTES = 1280, BUFS = 8;   // 40 ms per buffer
        const uint WHDR_DONE = 1;
        static readonly int HDR = Marshal.SizeOf(typeof(WAVEHDR));

        IntPtr wave = IntPtr.Zero;
        readonly IntPtr[] hdrs = new IntPtr[BUFS];
        readonly IntPtr[] datas = new IntPtr[BUFS];
        readonly MemoryStream pcm = new MemoryStream();
        readonly object gate = new object();
        System.Threading.Thread pump;
        volatile bool running;
        volatile int level;
        int peakAll;

        public int Level { get { return level; } }      // 0-100
        public int PeakAll { get { return peakAll; } }  // loudest sample of the whole take

        public void Start(int deviceId)
        {
            var fmt = new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = RATE,
                nAvgBytesPerSec = RATE * 2, nBlockAlign = 2, wBitsPerSample = 16, cbSize = 0 };
            int r = waveInOpen(out wave, deviceId, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (r != 0) { wave = IntPtr.Zero; throw new Exception("Couldn't open the microphone (code " + r + ")."); }
            for (int i = 0; i < BUFS; i++)
            {
                datas[i] = Marshal.AllocHGlobal(BUF_BYTES);
                hdrs[i] = Marshal.AllocHGlobal(HDR);
                var h = new WAVEHDR { lpData = datas[i], dwBufferLength = BUF_BYTES };
                Marshal.StructureToPtr(h, hdrs[i], false);
                waveInPrepareHeader(wave, hdrs[i], HDR);
                waveInAddBuffer(wave, hdrs[i], HDR);
            }
            r = waveInStart(wave);
            if (r != 0) { Dispose(); throw new Exception("Couldn't start the microphone (code " + r + ")."); }
            running = true;
            pump = new System.Threading.Thread(Pump) { IsBackground = true };
            pump.Start();
        }

        void Pump()
        {
            while (running)
            {
                Collect(true);
                System.Threading.Thread.Sleep(8);
            }
        }

        // Take every finished buffer, keep its audio, and (optionally) hand it back to the mic.
        void Collect(bool requeue)
        {
            lock (gate)
            {
                if (wave == IntPtr.Zero) return;
                for (int i = 0; i < BUFS; i++)
                {
                    var h = (WAVEHDR)Marshal.PtrToStructure(hdrs[i], typeof(WAVEHDR));
                    if ((h.dwFlags & WHDR_DONE) == 0) continue;
                    int n = (int)h.dwBytesRecorded;
                    if (n > 0)
                    {
                        var bytes = new byte[n];
                        Marshal.Copy(h.lpData, bytes, 0, n);
                        pcm.Write(bytes, 0, n);
                        int peak = 0;
                        for (int k = 0; k + 1 < n; k += 2)
                        {
                            int v = Math.Abs((int)(short)(bytes[k] | (bytes[k + 1] << 8)));
                            if (v > peak) peak = v;
                        }
                        if (peak > peakAll) peakAll = peak;
                        level = ToLevel(peak);
                    }
                    if (requeue && running)
                    {
                        h.dwFlags &= ~WHDR_DONE;
                        h.dwBytesRecorded = 0;
                        Marshal.StructureToPtr(h, hdrs[i], false);
                        waveInAddBuffer(wave, hdrs[i], HDR);
                    }
                }
            }
        }

        static int ToLevel(int peak)
        {
            if (peak < 1) return 0;
            double db = 20 * Math.Log10(Math.Min(32767, peak) / 32767.0);   // about -60 (quiet) to 0 (loud)
            return (int)Math.Max(0, Math.Min(100, (db + 50) * 2.5));
        }

        public void StopAndSave(string path)
        {
            running = false;
            if (pump != null) pump.Join(500);
            lock (gate)
            {
                if (wave != IntPtr.Zero) { waveInStop(wave); waveInReset(wave); }
            }
            Collect(false);
            level = 0;
            WriteWav(path, pcm.ToArray());
            Dispose();
        }

        static void WriteWav(string path, byte[] data)
        {
            using (var f = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(f))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(RATE); w.Write(RATE * 2);
                w.Write((short)2); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data.Length);
                w.Write(data);
            }
        }

        public void Dispose()
        {
            running = false;
            lock (gate)
            {
                if (wave != IntPtr.Zero)
                {
                    waveInReset(wave);
                    for (int i = 0; i < BUFS; i++)
                        if (hdrs[i] != IntPtr.Zero) waveInUnprepareHeader(wave, hdrs[i], HDR);
                    waveInClose(wave);
                    wave = IntPtr.Zero;
                }
                for (int i = 0; i < BUFS; i++)
                {
                    if (hdrs[i] != IntPtr.Zero) { Marshal.FreeHGlobal(hdrs[i]); hdrs[i] = IntPtr.Zero; }
                    if (datas[i] != IntPtr.Zero) { Marshal.FreeHGlobal(datas[i]); datas[i] = IntPtr.Zero; }
                }
            }
        }
    }

    // ------------------------------------------------------------------ Whisper (local, high-accuracy speech-to-text)

    static class Whisper
    {
        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimonFlow", "whisper"); }
        }

        public static string ModelPath { get { return Path.Combine(Dir, "ggml-base.en.bin"); } }

        public static string FindCli()
        {
            try
            {
                if (!Directory.Exists(Dir)) return null;
                foreach (var name in new[] { "whisper-cli.exe", "main.exe" })
                {
                    var hits = Directory.GetFiles(Dir, name, SearchOption.AllDirectories);
                    if (hits.Length > 0) return hits[0];
                }
            }
            catch { }
            return null;
        }

        public static bool Available
        {
            get { return FindCli() != null && File.Exists(ModelPath) && new FileInfo(ModelPath).Length > 50000000; }
        }

        public static string Transcribe(string wavPath)
        {
            string cli = FindCli();
            string outBase = Path.Combine(Path.GetTempPath(), "simonflow_out");
            try { File.Delete(outBase + ".txt"); } catch { }
            int threads = Math.Max(2, Math.Min(8, Environment.ProcessorCount));
            var psi = new ProcessStartInfo(cli, string.Format(
                "-m \"{0}\" -f \"{1}\" -l en -nt -np -otxt -of \"{2}\" -t {3}", ModelPath, wavPath, outBase, threads));
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = Path.GetDirectoryName(cli);
            string stdout, stderr;
            int code;
            using (var p = Process.Start(psi))
            {
                var outTask = p.StandardOutput.ReadToEndAsync();
                stderr = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } throw new Exception("Whisper took too long."); }
                stdout = outTask.Result;
                code = p.ExitCode;
            }
            if (code == -1073741515)
                throw new Exception("Whisper needs the Microsoft Visual C++ Redistributable (x64). Install it from aka.ms/vs/17/release/vc_redist.x64.exe, then try again.");
            if (code != 0)
            {
                string tail = stderr.Length > 300 ? stderr.Substring(stderr.Length - 300) : stderr;
                throw new Exception("Whisper failed (code " + code + "): " + tail.Trim());
            }
            string text = File.Exists(outBase + ".txt") ? File.ReadAllText(outBase + ".txt") : stdout;
            text = Regex.Replace(text, @"\[[^\]]*\]|\([^)]*\)", " ");   // [BLANK_AUDIO], (music) ...
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }
    }


    static class Draw
    {
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Darken(Color c, double f)
        {
            return Color.FromArgb(255, (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));
        }

        // The theme colour lifted a little, used for filled buttons so they match the background.
        public static Color Tint(Color accent) { return Blend(accent, Color.White, 0.28); }

        public static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(255,
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static double Luminance(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        public static Color GradientAt(Color[] g, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return t < 0.6 ? Blend(g[0], g[1], t / 0.6) : Blend(g[1], g[2], (t - 0.6) / 0.4);
        }

        public static Font Ui(float size, bool bold)
        {
            return new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size);
        }

        public static Font Icons(float size)
        {
            return new Font("Segoe MDL2 Assets", size);
        }
    }

    // ------------------------------------------------------------------ custom controls

    /// The whole window background: gradient + darker sidebar strip.
    class RootPanel : Panel
    {
        public Color[] Colors = new[] { Theme.All[0].A, Theme.All[0].B, Theme.All[0].C };
        public int SidebarWidth = 160;

        public RootPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var rc = ClientRectangle;
            if (rc.Width == 0 || rc.Height == 0) return;
            using (var b = new LinearGradientBrush(rc, Colors[0], Colors[2], 90f))
            {
                var blend = new ColorBlend(3);
                blend.Colors = new[] { Colors[0], Colors[1], Colors[2] };
                blend.Positions = new[] { 0f, 0.6f, 1f };
                b.InterpolationColors = blend;
                e.Graphics.FillRectangle(b, rc);
            }
            int sw = Dpi.S(SidebarWidth);
            using (var side = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                e.Graphics.FillRectangle(side, 0, 0, sw, rc.Height);
            using (var line = new Pen(Color.FromArgb(35, 255, 255, 255)))
                e.Graphics.DrawLine(line, sw, 0, sw, rc.Height);
        }
    }

    enum FlowState { Idle, Recording, Transcribing, Cleaning }

    class MicButton : Control
    {
        FlowState state = FlowState.Idle;
        public Color Accent = Theme.All[0].Accent;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
        double t;

        public MicButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            anim.Interval = 33;
            anim.Tick += (s, e) => { t += 0.033; Invalidate(); };
        }

        public FlowState State
        {
            get { return state; }
            set
            {
                state = value;
                if (state == FlowState.Idle) anim.Stop(); else anim.Start();
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = Width / 2f, cy = Height / 2f;
            float r = Math.Min(Width, Height) * 0.34f;
            float k = r / 62f;

            if (state == FlowState.Recording)
            {
                for (int i = 0; i < 2; i++)
                {
                    double p = ((t / 1.4) + i * 0.5) % 1.0;
                    float rr = r + (float)(p * 26 * k);
                    int alpha = (int)((1 - p) * 170);
                    using (var pen = new Pen(Color.FromArgb(alpha, 255, 255, 255), 3 * k))
                        g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2, rr * 2);
                }
            }

            for (int i = 3; i >= 1; i--)
                using (var sh = new SolidBrush(Color.FromArgb(18, 0, 0, 0)))
                    g.FillEllipse(sh, cx - r - i * k, cy - r + (4 + i * 2) * k, (r + i * k) * 2, (r + i * k) * 2);

            var circle = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            if (state == FlowState.Recording)
            {
                using (var b = new LinearGradientBrush(circle, Color.FromArgb(255, 115, 122), Color.FromArgb(224, 38, 82), 90f))
                    g.FillEllipse(b, circle);
            }
            else
            {
                var tint = Draw.Tint(Accent);
                using (var b = new LinearGradientBrush(circle, Draw.Blend(tint, Color.White, 0.10), Draw.Blend(Accent, Color.Black, 0.05), 90f))
                    g.FillEllipse(b, circle);
                using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), 1.5f * k))
                    g.DrawEllipse(pen, circle);
            }

            if (state == FlowState.Idle) DrawMic(g, cx, cy, k, Color.White);
            else if (state == FlowState.Recording)
            {
                using (var p = Draw.Rounded(new RectangleF(cx - 18 * k, cy - 18 * k, 36 * k, 36 * k), 7 * k))
                    g.FillPath(Brushes.White, p);
            }
            else
            {
                for (int i = 0; i < 3; i++)
                {
                    double bounce = Math.Sin(t * 8 - i * 0.9);
                    float y = cy - (float)(Math.Max(0, bounce) * 10 * k);
                    g.FillEllipse(Brushes.White, cx + (-26 + i * 20 - 6) * k, y - 6 * k, 12 * k, 12 * k);
                }
            }
        }

        static void DrawMic(Graphics g, float cx, float cy, float k, Color c)
        {
            using (var b = new SolidBrush(c))
            using (var pen = new Pen(c, 5 * k))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                using (var cap = Draw.Rounded(new RectangleF(cx - 11 * k, cy - 30 * k, 22 * k, 38 * k), 11 * k))
                    g.FillPath(b, cap);
                g.DrawArc(pen, cx - 20 * k, cy - 16 * k, 40 * k, 34 * k, 0, 180);
                g.DrawLine(pen, cx, cy + 18 * k, cx, cy + 28 * k);
                g.DrawLine(pen, cx - 10 * k, cy + 29 * k, cx + 10 * k, cy + 29 * k);
            }
        }
    }

    /// Rounded "pill" button: white when selected, translucent otherwise.
    class ChipButton : Control
    {
        public bool Selected;
        public bool Solid;          // always white (primary action)
        public Color Accent = Theme.All[0].Accent;
        public string Glyph;        // optional Segoe MDL2 icon
        bool hover;

        public ChipButton(string text)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Text = text;
            Cursor = Cursors.Hand;
            Font = Draw.Ui(9.5f, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            bool filled = Selected || Solid;
            using (var p = Draw.Rounded(r, Height / 2f))
            {
                if (filled)
                {
                    var tint = Draw.Tint(Accent);
                    if (hover) tint = Draw.Blend(tint, Color.White, 0.12);
                    using (var b = new SolidBrush(tint)) g.FillPath(b, p);
                    using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255), Dpi.F(1))) g.DrawPath(pen, p);
                }
                else
                    using (var b = new SolidBrush(Color.FromArgb(hover ? 70 : 34, 255, 255, 255))) g.FillPath(b, p);
            }
            var fg = Color.White;
            if (!Enabled) fg = Color.FromArgb(140, fg);
            string text = Text;
            if (!string.IsNullOrEmpty(Glyph))
            {
                using (var icon = Draw.Icons(Font.Size * 0.95f))
                {
                    var ts = TextRenderer.MeasureText(text, Font);
                    var isz = TextRenderer.MeasureText(Glyph, icon);
                    int total = isz.Width + ts.Width - Dpi.S(6);
                    int x = (Width - total) / 2;
                    TextRenderer.DrawText(g, Glyph, icon, new Rectangle(x, 0, isz.Width, Height), fg,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                    TextRenderer.DrawText(g, text, Font, new Rectangle(x + isz.Width - Dpi.S(6), 0, ts.Width + 4, Height), fg,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                }
                return;
            }
            TextRenderer.DrawText(g, text, Font, ClientRectangle, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// Non-clickable rounded label (key caps, badges).
    class Pill : Control
    {
        public int Alpha = 55;

        public Pill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Color.White;
            Font = Draw.Ui(9f, true);
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Draw.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Dpi.F(6)))
            using (var b = new SolidBrush(Color.FromArgb(Alpha, 255, 255, 255)))
                e.Graphics.FillPath(b, p);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// Frosted card: a solid tint of the background behind it, rounded, with a soft border.
    class Card : Panel
    {
        public string Title = "";

        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using (var p = Draw.Rounded(new RectangleF(0, 0, Width, Height), Dpi.F(16)))
                Region = new Region(p);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Color.FromArgb(38, 255, 255, 255), Dpi.F(1f)))
            using (var p = Draw.Rounded(new RectangleF(0.6f, 0.6f, Width - 1.6f, Height - 1.6f), Dpi.F(16)))
                g.DrawPath(pen, p);
            if (Title.Length > 0)
                using (var f = Draw.Ui(7.5f, true))
                    TextRenderer.DrawText(g, Title, f, new Rectangle(Dpi.S(14), Dpi.S(8), Width - Dpi.S(28), Dpi.S(18)),
                        Color.FromArgb(150, 255, 255, 255), TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    /// Rounded input well (darker than its card) that hosts a TextBox.
    class Field : Panel
    {
        public readonly TextBox Box = new TextBox();

        public Field()
        {
            Box.BorderStyle = BorderStyle.None;
            Box.ForeColor = Color.White;
            Box.Font = Draw.Ui(10f, false);
            Controls.Add(Box);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using (var p = Draw.Rounded(new RectangleF(0, 0, Width, Height), Dpi.F(8)))
                Region = new Region(p);
            Box.Location = new Point(Dpi.S(10), (Height - Box.PreferredHeight) / 2 + 1);
            Box.Width = Width - Dpi.S(20);
        }

        public void SetColor(Color c)
        {
            BackColor = c;
            Box.BackColor = c;
        }
    }

    class ToggleSwitch : Control
    {
        bool on;
        public Color Accent = Theme.All[0].Accent;
        public event EventHandler CheckedChanged;

        public ToggleSwitch(string text)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Text = text;
            Cursor = Cursors.Hand;
            Font = Draw.Ui(9.5f, false);
        }

        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float tw = Dpi.F(38), th = Dpi.F(22), y = (Height - th) / 2f;
            var track = new RectangleF(0, y, tw, th);
            using (var p = Draw.Rounded(track, th / 2))
            using (var b = new SolidBrush(on ? Color.FromArgb(255, 52, 199, 89) : Color.FromArgb(70, 255, 255, 255)))
                g.FillPath(b, p);
            float kd = th - Dpi.F(4);
            float kx = on ? tw - kd - Dpi.F(2) : Dpi.F(2);
            g.FillEllipse(Brushes.White, kx, y + Dpi.F(2), kd, kd);
            TextRenderer.DrawText(g, Text, Font, new Rectangle((int)tw + Dpi.S(10), 0, Width - (int)tw - Dpi.S(10), Height),
                Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    // A dropdown drawn to match the dark cards (the Windows one stays white on Windows 11).
    class DarkCombo : ComboBox
    {
        bool hover;

        public DarkCombo()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            ItemHeight = Dpi.S(20);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnDropDownClosed(EventArgs e) { Invalidate(); base.OnDropDownClosed(e); }

        Color Parent0 { get { return Parent != null ? Parent.BackColor : Color.FromArgb(30, 30, 36); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent0);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            int a = Enabled ? 255 : 120;
            using (var path = Draw.Rounded(r, Dpi.F(8)))
            {
                using (var b = new SolidBrush(Color.FromArgb(a, hover && Enabled ? Draw.Blend(BackColor, Color.White, 0.06) : BackColor)))
                    g.FillPath(b, path);
                using (var pen = new Pen(Color.FromArgb(Enabled ? 45 : 20, 255, 255, 255), Dpi.F(1)))
                    g.DrawPath(pen, path);
            }
            string text = SelectedItem != null ? GetItemText(SelectedItem) : "";
            var tr = new Rectangle(Dpi.S(10), 0, Width - Dpi.S(34), Height);
            TextRenderer.DrawText(g, text, Font, tr, Color.FromArgb(Enabled ? 240 : 120, 255, 255, 255),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            // chevron
            float cx = Width - Dpi.F(16), cy = Height / 2f, w = Dpi.F(4);
            using (var pen = new Pen(Color.FromArgb(Enabled ? 200 : 90, 255, 255, 255), Dpi.F(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(cx - w, cy - w / 2), new PointF(cx, cy + w / 2), new PointF(cx + w, cy - w / 2) });
        }

        // the open list
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Color.FromArgb(52, 52, 60) : Color.FromArgb(28, 28, 33)))
                e.Graphics.FillRectangle(b, e.Bounds);
            var tr = new Rectangle(e.Bounds.X + Dpi.S(8), e.Bounds.Y, e.Bounds.Width - Dpi.S(10), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, tr, Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    class VolumeSlider : Control
    {
        int value = 70;
        bool dragging;
        public event EventHandler ValueChanged;     // while dragging
        public event EventHandler ValueCommitted;   // when the mouse is let go

        public VolumeSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public int Value
        {
            get { return value; }
            set { int v = Math.Max(0, Math.Min(100, value)); if (v == this.value) return; this.value = v; Invalidate(); }
        }

        float Left0 { get { return Dpi.F(9); } }
        float Right0 { get { return Width - Dpi.F(9); } }

        void SetFromMouse(int x)
        {
            int v = (int)Math.Round((x - Left0) / Math.Max(1, Right0 - Left0) * 100);
            v = Math.Max(0, Math.Min(100, v));
            if (v != value) { value = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        }

        protected override void OnMouseDown(MouseEventArgs e) { if (!Enabled) return; dragging = true; Capture = true; SetFromMouse(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromMouse(e.X); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging) return;
            dragging = false; Capture = false;
            if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty);
        }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cy = Height / 2f, th = Dpi.F(4);
            float x = Left0 + (Right0 - Left0) * value / 100f;
            int a = Enabled ? 255 : 110;
            using (var p = Draw.Rounded(new RectangleF(Left0, cy - th / 2, Right0 - Left0, th), th / 2))
            using (var b = new SolidBrush(Color.FromArgb(Enabled ? 70 : 35, 255, 255, 255)))
                g.FillPath(b, p);
            if (x > Left0)
                using (var p = Draw.Rounded(new RectangleF(Left0, cy - th / 2, x - Left0, th), th / 2))
                using (var b = new SolidBrush(Color.FromArgb(a, 52, 199, 89)))
                    g.FillPath(b, p);
            float kd = Dpi.F(16);
            using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                g.FillEllipse(b, x - kd / 2, cy - kd / 2, kd, kd);
        }
    }

    class ThemeDot : Control
    {
        public Color[] Colors;
        public bool Selected;

        public ThemeDot()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float pad = Dpi.F(3);
            var rc = new RectangleF(pad, pad, Width - pad * 2 - 1, Height - pad * 2 - 1);
            using (var b = new LinearGradientBrush(rc, Colors[0], Colors[2], 90f))
                g.FillEllipse(b, rc);
            using (var pen = new Pen(Color.White, Selected ? Dpi.F(3) : Dpi.F(1.2f)))
                g.DrawEllipse(pen, rc);
            if (Selected)
                using (var f = Draw.Icons(10f))
                    TextRenderer.DrawText(g, "", f, ClientRectangle, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    class SidebarTab : Control
    {
        public bool Selected;
        public string Glyph;
        bool hover;

        public SidebarTab(string text, string glyph)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Text = text;
            Glyph = glyph;
            Cursor = Cursors.Hand;
            Font = Draw.Ui(10.5f, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int a = Selected ? 64 : (hover ? 28 : 0);
            if (a > 0)
                using (var p = Draw.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), Dpi.F(10)))
                using (var b = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                    g.FillPath(b, p);
            var fg = Color.FromArgb(Selected ? 255 : 200, 255, 255, 255);
            using (var icon = Draw.Icons(11f))
                TextRenderer.DrawText(g, Glyph, icon, new Rectangle(Dpi.S(12), 0, Dpi.S(24), Height), fg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Dpi.S(40), 0, Width - Dpi.S(40), Height), fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    // ------------------------------------------------------------------ floating pill

    class OverlayForm : Form
    {
        public string Mode = "rec";      // rec | busy | done | idle
        public bool AlwaysOn;            // keep a small resting pill on screen
        public string BusyText = "Cleaning up";
        public string Note = "";
        public Func<int> Level;          // 0-100, or -1 when the level can't be read
        public Color Accent = Theme.All[0].Accent;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
        double t;

        // animated state
        float curW, curH, alpha;
        bool hiding;
        double smooth, talkUntil;
        readonly double[] hist = new double[64];
        int histPos;

        static readonly int CANVAS_W = 260, CANVAS_H = 72;

        public OverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = Dpi.Z(CANVAS_W, CANVAS_H);
            anim.Interval = 16;
            anim.Tick += (s, e) => Frame();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // no-activate | tool window | click-through | layered | topmost
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020 | 0x00080000 | 0x00000008;
                return cp;
            }
        }

        public void ShowPill()
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Bottom - Height - Dpi.S(8));
            anim.Interval = 16;
            if (!Visible || hiding)
            {
                if (!Visible) { alpha = 0; curW = 0; curH = 0; smooth = 0; Array.Clear(hist, 0, hist.Length); talkUntil = 0; }
                hiding = false;
                if (!Visible) Show();
            }
            anim.Start();
        }

        public void HidePill()
        {
            if (AlwaysOn) { Mode = "idle"; ShowPill(); return; }   // shrink to the resting pill instead
            hiding = true;   // fades out, then hides itself
        }

        // ---- per frame
        void Frame()
        {
            t += 0.016;

            // read the mic and decide if the person is talking
            bool talking = true;
            if (Mode == "rec")
            {
                int raw = Level != null ? Level() : -1;
                if (raw < 0) { smooth = 0.35 + 0.25 * Math.Sin(t * 6); talking = true; }
                else
                {
                    double v = raw / 100.0;
                    smooth = v > smooth ? smooth + (v - smooth) * 0.6 : smooth * 0.88;
                    if (smooth > 0.34) talkUntil = t + 0.9;
                    talking = t < talkUntil;
                }
                hist[histPos] = smooth;
                histPos = (histPos + 1) % hist.Length;
            }

            // target size for this mode
            float tw, th;
            if (Mode == "rec")
            {
                if (talking) { tw = 112; th = 30; } else { tw = 46; th = 14; }
            }
            else if (Mode == "idle") { tw = 34; th = 9; }
            else if (Mode == "busy") { tw = 128; th = 30; }
            else
            {
                using (var f = Draw.Ui(9.5f, true))
                using (var tmp = Graphics.FromHwnd(IntPtr.Zero))
                    tw = Math.Min(CANVAS_W - 30, tmp.MeasureString(DoneText, f).Width / Dpi.Scale + 52);
                th = 30;
            }
            if (curW == 0) { curW = tw * 0.6f; curH = th * 0.6f; }
            curW += (tw - curW) * 0.22f;
            curH += (th - curH) * 0.22f;

            float ta = hiding ? 0f : 1f;
            alpha += (ta - alpha) * (hiding ? 0.2f : 0.3f);
            if (hiding && alpha < 0.02f)
            {
                anim.Stop();
                hiding = false;
                alpha = 0;
                Hide();
                return;
            }
            Render();

            // resting pill has finished animating: redraw rarely to save power
            if (Mode == "idle" && !hiding && Math.Abs(tw - curW) < 0.3f && Math.Abs(th - curH) < 0.3f && alpha > 0.99f)
                anim.Interval = 250;
        }

        string DoneText { get { return Note.Length > 0 ? Note : "Done"; } }

        void Render()
        {
            if (!IsHandleCreated) return;
            int W = Width, H = Height;
            using (var bmp = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    DrawPill(g, W, H);
                }
                Native.PushLayered(this, bmp, (byte)Math.Max(0, Math.Min(255, alpha * 255)));
            }
        }

        void DrawPill(Graphics g, int W, int H)
        {
            float w = Dpi.F(curW), h = Dpi.F(curH);
            var pill = new RectangleF((W - w) / 2f, H - Dpi.F(12) - h, w, h);
            float r = h / 2f;

            // soft shadow
            for (int i = 6; i >= 1; i--)
            {
                var sr = pill; sr.Inflate(Dpi.F(i * 1.5f), Dpi.F(i * 1.5f)); sr.Offset(0, Dpi.F(3));
                using (var p = Draw.Rounded(sr, sr.Height / 2f))
                using (var b = new SolidBrush(Color.FromArgb(10, 0, 0, 0)))
                    g.FillPath(b, p);
            }

            // body: dark glass with a hint of the theme colour
            using (var p = Draw.Rounded(pill, r))
            {
                using (var b = new SolidBrush(Color.FromArgb(250, 8, 8, 10)))
                    g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(70, 255, 255, 255), Dpi.F(1)))
                    g.DrawPath(pen, p);
            }

            var bar = Color.White;

            if (Mode == "idle")
            {
                // resting: four faint dots
                float cy = pill.Y + h / 2f, d = Math.Max(1.5f, Math.Min(Dpi.F(2.2f), h * 0.3f)), sp = Dpi.F(5);
                float x0 = pill.X + w / 2f - sp * 1.5f;
                using (var b = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                    for (int i = 0; i < 4; i++)
                        g.FillEllipse(b, x0 + i * sp - d / 2, cy - d / 2, d, d);
            }
            else if (Mode == "rec")
            {
                float cy = pill.Y + h / 2f;

                // waveform: recent mic levels scrolling right to left
                float pad = Math.Min(Dpi.F(12), r * 0.9f);
                float left = pill.X + pad, right = pill.Right - pad;
                float bw = Dpi.F(2.4f), gap = Dpi.F(2.6f);
                int n = Math.Max(0, (int)((right - left + gap) / (bw + gap)));
                float maxH = h - Dpi.F(10), minH = Math.Min(bw, h * 0.3f);
                float used = n * (bw + gap) - gap;
                float x0 = left + ((right - left) - used) / 2f;
                for (int i = 0; i < n; i++)
                {
                    int idx = ((histPos - 1 - (n - 1 - i) * 2) % hist.Length + hist.Length * 4) % hist.Length;
                    double v = Math.Max(0, Math.Min(1, (hist[idx] - 0.22) / 0.6));   // ignore background hiss
                    double centre = 1 - Math.Abs(i - (n - 1) / 2.0) / Math.Max(1, n / 2.0) * 0.35;
                    float bh = (float)(minH + (maxH - minH) * Math.Pow(v, 0.8) * centre);
                    if (bh < minH) bh = minH;
                    int a = (int)(150 + 105 * Math.Min(1, v * 2));
                    using (var b = new SolidBrush(Color.FromArgb(a, bar)))
                    using (var p = Draw.Rounded(new RectangleF(x0 + i * (bw + gap), cy - bh / 2, bw, bh), bw / 2))
                        g.FillPath(b, p);
                }
            }
            else
            {
                float cy = pill.Y + h / 2f;
                using (var font = Draw.Ui(9.5f, true))
                using (var white = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
                {
                    var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                    float textX;
                    if (Mode == "busy")
                    {
                        // three bouncing dots
                        float dx = pill.X + Dpi.F(18);
                        for (int i = 0; i < 3; i++)
                        {
                            float bounce = (float)Math.Max(0, Math.Sin(t * 7 - i * 0.7)) * Dpi.F(4);
                            using (var b = new SolidBrush(bar))
                                g.FillEllipse(b, dx + i * Dpi.F(8), cy - Dpi.F(2.5f) - bounce, Dpi.F(5), Dpi.F(5));
                        }
                        textX = dx + Dpi.F(30);
                        g.DrawString(BusyText, font, white, new RectangleF(textX, pill.Y, pill.Right - textX - Dpi.F(10), h), sf);
                    }
                    else
                    {
                        // green check
                        float cr = Dpi.F(8), cx = pill.X + Dpi.F(20);
                        bool cancelled = Note == "Cancelled";
                        using (var gb = new SolidBrush(cancelled ? Color.FromArgb(90, 255, 255, 255) : Color.White))
                            g.FillEllipse(gb, cx - cr, cy - cr, cr * 2, cr * 2);
                        using (var pen = new Pen(Color.Black, Dpi.F(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            if (cancelled)
                            {
                                float k = Dpi.F(3.2f);
                                g.DrawLine(pen, cx - k, cy - k, cx + k, cy + k);
                                g.DrawLine(pen, cx - k, cy + k, cx + k, cy - k);
                            }
                            else g.DrawLines(pen, new[] {
                                new PointF(cx - Dpi.F(3.5f), cy + Dpi.F(0.2f)),
                                new PointF(cx - Dpi.F(1f), cy + Dpi.F(2.8f)),
                                new PointF(cx + Dpi.F(3.8f), cy - Dpi.F(2.6f)) });
                        textX = cx + cr + Dpi.F(8);
                        g.DrawString(DoneText, font, white, new RectangleF(textX, pill.Y, pill.Right - textX - Dpi.F(12), h), sf);
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------ main window

    class MainForm : Form
    {
        const string MODEL = "claude-haiku-5-5";   // fast; use "claude-sonnet-5-5" for higher quality
        const int HOTKEY_ID = 1, ESC_ID = 2;
        readonly System.Windows.Forms.Timer holdTimer = new System.Windows.Forms.Timer { Interval = 25 };
        DateTime holdStart;
        DateTime recordStart;
        double lastRecSeconds;
        Label wpmLabel;
        readonly ToolTip toolTip = new ToolTip();
        const int SIDEBAR = 160, CONTENT_X = 160, CONTENT_W = 420, WIN_W = 580, WIN_H = 768;

        static readonly string[][] STYLES = new string[][]
        {
            new[] { "light", "Natural", "Fix grammar, punctuation, capitalization and obvious speech-recognition errors. Remove filler words (um, uh, like, you know) and false starts. If the speaker corrects themselves (\"no wait, I mean...\"), keep only the corrected version. Otherwise keep their wording and voice as close to the original as possible." },
            new[] { "clear", "Concise", "Rewrite as clear, concise prose. Fix grammar, remove filler and repetition, keep only self-corrected versions, and tighten wording while preserving every idea." },
            new[] { "formal", "Formal", "Rewrite in a polished, professional tone suitable for an email or document. Fix grammar, remove filler, keep only self-corrected versions, and preserve every idea." },
            new[] { "email", "Email", "Format it as a ready-to-send email. If the speaker mentions a subject, put 'Subject: <subject>' on the first line, then a blank line. Start with a greeting on its own line (use the recipient's name if mentioned, for example 'Hi Sam,', otherwise 'Hi,'). Then the message in short, friendly paragraphs separated by blank lines. End with a closing on its own line such as 'Thanks,' or 'Best,' and, if the speaker said their name, the name on the next line. Fix grammar, remove filler, keep only self-corrected versions, preserve every idea, and never invent details that weren't said." },
            new[] { "bullets", "Bullets", "Turn the ideas into a clean bulleted list (each line starting with '- '). Fix grammar, remove filler, keep only self-corrected versions, and preserve every idea." },
        };

        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        readonly Settings settings;
        readonly bool startHidden;
        FlowState state = FlowState.Idle;
        SpeechRecognitionEngine engine;
        StringBuilder transcript = new StringBuilder();
        int level;
        int session;
        IntPtr targetWindow = IntPtr.Zero;
        bool startedFromWindow;
        bool capturingShortcut;
        bool quitting;
        bool shownTrayTip;
        bool usingWhisper;
        WaveRecorder recorder;
        readonly string wavPath = Path.Combine(Path.GetTempPath(), "simonflow_rec.wav");

        // UI
        RootPanel root;
        readonly List<Control> mainControls = new List<Control>();
        readonly List<Control> settingsControls = new List<Control>();
        readonly List<Card> cards = new List<Card>();
        readonly List<ChipButton> styleChips = new List<ChipButton>();
        readonly List<ChipButton> allChips = new List<ChipButton>();
        readonly List<ThemeDot> themeDots = new List<ThemeDot>();
        SidebarTab tabDictate, tabSettings;
        MicButton mic;
        Label statusLabel, keyStatus, noteLabel, engineLabel, sideStatus;
        Pill sideShortcut, hintPill, shortcutPill;
        ChipButton changeShortcutButton;
        TextBox heardBox, resultBox;
        Field keyField;
        ComboBox languageBox;
        ToggleSwitch soundsToggle, startupToggle, pillToggle;
        ComboBox micBox, translateBox;
        ToggleSwitch holdToggle;
        VolumeSlider volumeSlider;
        Label volumeLabel;
        ComboBox startSoundBox, stopSoundBox;
        Card heardCard, resultCard;
        NotifyIcon tray;
        ToolStripMenuItem trayToggle;
        OverlayForm overlay;

        public MainForm(bool background)
        {
            startHidden = background;
            settings = Settings.Load();
            if (!AllowedSound(settings.StartSound)) { settings.StartSound = BaseSounds.PathFor("Chime Up"); settings.Save(); }
            if (!AllowedSound(settings.StopSound)) { settings.StopSound = BaseSounds.PathFor("Chime Down"); settings.Save(); }
            Text = "Simon Flow";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = Dpi.Z(WIN_W, WIN_H);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            Font = Draw.Ui(9.5f, false);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            SuspendLayout();
            root = new RootPanel { Dock = DockStyle.Fill, SidebarWidth = SIDEBAR };
            Controls.Add(root);
            BuildSidebar();
            BuildMainPage();
            BuildSettingsPage();
            ResumeLayout();

            BuildTray();
            overlay = new OverlayForm();
            overlay.Level = ReadLevel;
            holdTimer.Tick += (s, e) => HoldTick();
            ApplyColors();
            ShowSettings(false);
            SetState(FlowState.Idle);
            Log.Write("launched (scale " + Dpi.Scale + ", whisper=" + Whisper.Available + ")");
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && !IsHandleCreated)
            {
                CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateTitleBar();
            ApplyAlwaysPill();
            if (!RegisterCurrentHotKey())
                Note("The shortcut " + settings.HotKeyLabel + " is taken by another app. Pick a new one in Settings.");
            if (string.IsNullOrEmpty(settings.ApiKey))
                BeginInvoke(new Action(() => Note("Free mode: press " + settings.HotKeyLabel + " and talk. Add a Claude API key in Settings for smarter cleanup, styles and translate.")));
        }

        // ----------------------------------------------------------- layout helpers (100% coordinates)

        Label AddLabel(Control parent, List<Control> page, string text, int x, int y, int w, int h,
                       float size, bool bold, ContentAlignment align)
        {
            var l = new Label
            {
                Text = text,
                Location = Dpi.P(x, y),
                Size = Dpi.Z(w, h),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = Draw.Ui(size, bold),
                TextAlign = align,
                UseMnemonic = false,
            };
            parent.Controls.Add(l);
            if (page != null) page.Add(l);
            return l;
        }

        ChipButton AddChip(Control parent, List<Control> page, string text, int x, int y, int w, int h, EventHandler click)
        {
            var c = new ChipButton(text) { Location = Dpi.P(x, y), Size = Dpi.Z(w, h) };
            c.Click += click;
            parent.Controls.Add(c);
            if (page != null) page.Add(c);
            allChips.Add(c);
            return c;
        }

        Card AddCard(List<Control> page, string title, int x, int y, int w, int h)
        {
            var c = new Card { Title = title, Location = Dpi.P(x, y), Size = Dpi.Z(w, h) };
            root.Controls.Add(c);
            page.Add(c);
            cards.Add(c);
            return c;
        }

        Pill AddPill(Control parent, List<Control> page, string text, int x, int y, int w, int h, float size)
        {
            var p = new Pill { Text = text, Location = Dpi.P(x, y), Size = Dpi.Z(w, h), Font = Draw.Ui(size, true) };
            parent.Controls.Add(p);
            if (page != null) page.Add(p);
            return p;
        }

        // ----------------------------------------------------------- sidebar

        void BuildSidebar()
        {
            var brandIcon = new Label
            {
                Text = "", Font = Draw.Icons(13f), ForeColor = Color.White, BackColor = Color.Transparent,
                Location = Dpi.P(18, 22), Size = Dpi.Z(26, 28), TextAlign = ContentAlignment.MiddleLeft,
            };
            root.Controls.Add(brandIcon);
            AddLabel(root, null, "Simon Flow", 44, 22, 110, 28, 12f, true, ContentAlignment.MiddleLeft);

            tabDictate = new SidebarTab("Dictate", "") { Location = Dpi.P(10, 70), Size = Dpi.Z(140, 40) };
            tabDictate.Click += (s, e) => ShowSettings(false);
            root.Controls.Add(tabDictate);
            tabSettings = new SidebarTab("Settings", "") { Location = Dpi.P(10, 114), Size = Dpi.Z(140, 40) };
            tabSettings.Click += (s, e) => ShowSettings(true);
            root.Controls.Add(tabSettings);

            sideStatus = AddLabel(root, null, "", 16, WIN_H - 76, 140, 22, 8.5f, false, ContentAlignment.MiddleLeft);
            sideShortcut = AddPill(root, null, "", 16, WIN_H - 48, 128, 26, 8.5f);
        }

        // ----------------------------------------------------------- dictate page

        void BuildMainPage()
        {
            var page = mainControls;
            int cx = CONTENT_X;

            AddLabel(root, page, "Simon Flow", cx, 26, CONTENT_W, 46, 22f, true, ContentAlignment.MiddleCenter);
            AddLabel(root, page, "Talk. Get clean, grammatical text.", cx, 70, CONTENT_W, 22, 10f, false, ContentAlignment.MiddleCenter)
                .ForeColor = Color.FromArgb(225, 255, 255, 255);

            mic = new MicButton { Location = Dpi.P(cx + 120, 96), Size = Dpi.Z(180, 180) };
            mic.Click += (s, e) => { startedFromWindow = true; Toggle(); };
            root.Controls.Add(mic);
            page.Add(mic);

            statusLabel = AddLabel(root, page, "", cx + 20, 276, 380, 42, 9.5f, false, ContentAlignment.MiddleCenter);

            int x = cx + 30;
            foreach (var st in STYLES)
            {
                string key = st[0];
                var chip = AddChip(root, page, st[1], x, 324, 68, 30, (s, e) => SetStyleKey(key));
                chip.Tag = key;
                styleChips.Add(chip);
                x += 74;
            }

            heardCard = AddCard(page, "WHAT YOU SAID", cx + 20, 368, 380, 92);
            heardBox = new TextBox
            {
                Multiline = true, BorderStyle = BorderStyle.None, ForeColor = Color.White,
                Font = Draw.Ui(10f, false), ScrollBars = ScrollBars.None,
                Location = Dpi.P(14, 30), Size = Dpi.Z(352, 54),
            };
            heardCard.Controls.Add(heardBox);
            wpmLabel = AddLabel(heardCard, null, "", 170, 6, 196, 20, 8.5f, true, ContentAlignment.MiddleRight);
            ShowWpm(0, 0, 0);

            resultCard = AddCard(page, "CLEANED UP", cx + 20, 470, 380, 112);
            resultBox = new TextBox
            {
                Multiline = true, BorderStyle = BorderStyle.None, ReadOnly = true, ForeColor = Color.White,
                Font = Draw.Ui(11f, false), ScrollBars = ScrollBars.None,
                Location = Dpi.P(14, 30), Size = Dpi.Z(352, 74),
            };
            resultCard.Controls.Add(resultBox);

            hintPill = AddPill(root, page, "", cx + 20, 594, 170, 30, 8.5f);
            var fix = AddChip(root, page, "Fix text", cx + 200, 594, 96, 30, (s, e) => FixTypedText());
            fix.Glyph = "";
            var copy = AddChip(root, page, "Copy", cx + 304, 594, 96, 30, (s, e) => CopyResult());
            copy.Glyph = "";
            copy.Solid = true;
        }

        // ----------------------------------------------------------- settings page

        void BuildSettingsPage()
        {
            var page = settingsControls;
            int cx = CONTENT_X + 20, w = 380;

            AddLabel(root, page, "Settings", cx, 18, 300, 46, 20f, true, ContentAlignment.MiddleLeft);

            // Shortcut
            var c1 = AddCard(page, "KEYBOARD SHORTCUT", cx, 70, w, 96);
            shortcutPill = AddPill(c1, null, "", 14, 34, 170, 32, 11f);
            shortcutPill.Alpha = 40;
            changeShortcutButton = AddChip(c1, null, "Change", 196, 35, 80, 30, (s, e) => ToggleShortcutCapture());
            changeShortcutButton.Solid = true;
            AddChip(c1, null, "Reset", 284, 35, 80, 30, (s, e) => ApplyShortcut(
                (int)(Native.MOD_CONTROL | Native.MOD_ALT), (int)Keys.Space, "Ctrl+Alt+Space"));
            holdToggle = new ToggleSwitch("Hold to talk (let go to finish)")
                { Location = Dpi.P(14, 66), Size = Dpi.Z(352, 26), Checked = settings.HoldToTalk };
            holdToggle.CheckedChanged += (s, e) =>
            {
                settings.HoldToTalk = holdToggle.Checked;
                settings.Save();
                Note(settings.HoldToTalk
                    ? "Hold your shortcut while you talk, let go to finish."
                    : "Press your shortcut once to start talking, again to finish.");
            };
            c1.Controls.Add(holdToggle);

            // API key
            var c2 = AddCard(page, "CLAUDE API KEY", cx, 176, w, 96);
            keyField = new Field { Location = Dpi.P(14, 34), Size = Dpi.Z(262, 32) };
            keyField.Box.UseSystemPasswordChar = true;
            c2.Controls.Add(keyField);
            var save = AddChip(c2, null, "Save", 284, 35, 80, 30, (s, e) => SaveKey());
            save.Solid = true;
            keyStatus = AddLabel(c2, null, "", 14, 70, 350, 20, 8.5f, false, ContentAlignment.MiddleLeft);

            // Background
            var c3 = AddCard(page, "BACKGROUND", cx, 280, w, 84);
            int dx = 12;
            foreach (var t in Theme.All)
            {
                var theme = t;
                var dot = new ThemeDot { Colors = new[] { t.A, t.B, t.C }, Location = Dpi.P(dx, 32), Size = Dpi.Z(38, 38), Tag = t.Key };
                dot.Click += (s, e) => { settings.ThemeKey = theme.Key; settings.Save(); ApplyColors(); };
                new ToolTip().SetToolTip(dot, t.Name);
                c3.Controls.Add(dot);
                themeDots.Add(dot);
                dx += 44;
            }
            AddChip(c3, null, "Custom\u2026", 282, 36, 84, 30, (s, e) => ChooseColor());

            // Sounds & startup
            var c4 = AddCard(page, "SOUNDS AND STARTUP", cx, 372, w, 220);
            soundsToggle = new ToggleSwitch("Play sounds when dictation starts and stops")
                { Location = Dpi.P(14, 28), Size = Dpi.Z(352, 26), Checked = settings.Sounds };
            soundsToggle.CheckedChanged += (s, e) =>
            {
                settings.Sounds = soundsToggle.Checked;
                settings.Save();
                startSoundBox.Enabled = stopSoundBox.Enabled = volumeSlider.Enabled = settings.Sounds;
                if (settings.Sounds) PlaySound(true);
            };
            c4.Controls.Add(soundsToggle);
            startSoundBox = AddSoundRow(c4, "Start", 60, true);
            stopSoundBox = AddSoundRow(c4, "Stop", 92, false);
            AddLabel(c4, null, "Volume", 14, 124, 60, 26, 9.5f, false, ContentAlignment.MiddleLeft).ForeColor = Color.FromArgb(200, 255, 255, 255);
            volumeSlider = new VolumeSlider { Location = Dpi.P(70, 124), Size = Dpi.Z(254, 26), Value = settings.SoundVolume };
            volumeLabel = AddLabel(c4, null, settings.SoundVolume + "%", 326, 124, 42, 26, 9f, false, ContentAlignment.MiddleRight);
            volumeSlider.ValueChanged += (s, e) => { volumeLabel.Text = volumeSlider.Value + "%"; };
            volumeSlider.ValueCommitted += (s, e) =>
            {
                settings.SoundVolume = volumeSlider.Value;
                settings.Save();
                PlayFile(settings.StartSound);    // let them hear the new volume
            };
            c4.Controls.Add(volumeSlider);
            startSoundBox.Enabled = stopSoundBox.Enabled = volumeSlider.Enabled = settings.Sounds;
            startupToggle = new ToggleSwitch("Start Simon Flow when Windows starts")
                { Location = Dpi.P(14, 156), Size = Dpi.Z(352, 26), Checked = IsStartupEnabled() };
            startupToggle.CheckedChanged += (s, e) => SetStartup(startupToggle.Checked);
            c4.Controls.Add(startupToggle);
            pillToggle = new ToggleSwitch("Always show the pill at the bottom of the screen")
                { Location = Dpi.P(14, 188), Size = Dpi.Z(352, 26), Checked = settings.AlwaysShowPill };
            pillToggle.CheckedChanged += (s, e) =>
            {
                settings.AlwaysShowPill = pillToggle.Checked;
                settings.Save();
                ApplyAlwaysPill();
            };
            c4.Controls.Add(pillToggle);

            // Speech engine
            var c5 = AddCard(page, "SPEECH", cx, 600, w, 120);
            engineLabel = AddLabel(c5, null, "", 14, 26, 226, 24, 8.5f, false, ContentAlignment.MiddleLeft);
            languageBox = new DarkCombo
            {
                Location = Dpi.P(244, 27), Size = Dpi.Z(122, 24), DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Font = Draw.Ui(9f, false),
            };
            c5.Controls.Add(languageBox);
            FillLanguages();
            languageBox.SelectedIndexChanged += (s, e) =>
            {
                var item = languageBox.SelectedItem as LangItem;
                if (item != null && item.Code.Length > 0) { settings.Culture = item.Code; settings.Save(); }
            };

            // Microphone
            AddLabel(c5, null, "Mic", 14, 58, 66, 26, 9.5f, false, ContentAlignment.MiddleLeft).ForeColor = Color.FromArgb(200, 255, 255, 255);
            micBox = new DarkCombo
            {
                Location = Dpi.P(84, 59), Size = Dpi.Z(282, 24), DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Font = Draw.Ui(9f, false), MaxDropDownItems = 12,
            };
            c5.Controls.Add(micBox);
            FillMics();
            micBox.DropDown += (s, e) => FillMics();     // pick up mics plugged in later
            micBox.SelectedIndexChanged += (s, e) =>
            {
                if (fillingMics) return;
                var name = micBox.SelectedItem as string;
                if (name != null && name.EndsWith(" (unplugged)")) name = name.Substring(0, name.Length - " (unplugged)".Length);
                settings.MicName = (name == null || micBox.SelectedIndex == 0) ? "" : name;
                settings.Save();
                Note(settings.MicName.Length == 0 ? "Using the Windows default mic." : "Using " + settings.MicName + ".");
            };

            // Translate
            AddLabel(c5, null, "Translate", 14, 90, 70, 26, 9.5f, false, ContentAlignment.MiddleLeft).ForeColor = Color.FromArgb(200, 255, 255, 255);
            translateBox = new DarkCombo
            {
                Location = Dpi.P(84, 91), Size = Dpi.Z(282, 24), DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Font = Draw.Ui(9f, false), MaxDropDownItems = 14,
            };
            c5.Controls.Add(translateBox);
            translateBox.Items.Add("Off (keep my language)");
            foreach (var l in TRANSLATE_LANGS) translateBox.Items.Add(l);
            int ti = Array.IndexOf(TRANSLATE_LANGS, settings.TranslateTo ?? "");
            translateBox.SelectedIndex = ti >= 0 ? ti + 1 : 0;
            translateBox.SelectedIndexChanged += (s, e) =>
            {
                settings.TranslateTo = translateBox.SelectedIndex <= 0 ? "" : (string)translateBox.SelectedItem;
                settings.Save();
                Note(settings.TranslateTo.Length == 0 ? "Translate is off." : "Talk in English and it types in " + settings.TranslateTo + ".");
            };

            noteLabel = AddLabel(root, page, "", cx, 726, w, 38, 9f, true, ContentAlignment.MiddleLeft);
        }

        static readonly string[] TRANSLATE_LANGS = {
            "Spanish", "French", "German", "Italian", "Portuguese", "Dutch", "Polish", "Russian",
            "Ukrainian", "Turkish", "Arabic", "Hindi", "Chinese (Simplified)", "Japanese", "Korean", "Vietnamese" };

        bool fillingMics;

        void FillMics()
        {
            fillingMics = true;
            micBox.Items.Clear();
            micBox.Items.Add("Windows default");
            int sel = 0;
            var names = WaveRecorder.DeviceNames();
            foreach (var n in names)
            {
                micBox.Items.Add(n);
                if (string.Equals(n, settings.MicName, StringComparison.OrdinalIgnoreCase)) sel = micBox.Items.Count - 1;
            }
            if (sel == 0 && !string.IsNullOrEmpty(settings.MicName))
            {
                micBox.Items.Add(settings.MicName + " (unplugged)");
                sel = micBox.Items.Count - 1;
            }
            micBox.SelectedIndex = sel;
            fillingMics = false;
        }

        class LangItem
        {
            public string Code, Name;
            public override string ToString() { return Name; }
        }

        class SoundItem
        {
            public string Name, File;
            public override string ToString() { return Name; }
        }

        static string MediaDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media"); }
        }

        ComboBox AddSoundRow(Control card, string label, int y, bool isStart)
        {
            AddLabel(card, null, label, 14, y, 44, 26, 9.5f, false, ContentAlignment.MiddleLeft).ForeColor = Color.FromArgb(200, 255, 255, 255);
            var box = new DarkCombo
            {
                Location = Dpi.P(60, y + 1), Size = Dpi.Z(220, 24), DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Font = Draw.Ui(9f, false),
                MaxDropDownItems = 14,
            };
            card.Controls.Add(box);
            FillSounds(box, isStart ? settings.StartSound : settings.StopSound);
            box.SelectedIndexChanged += (s, e) => OnSoundPicked(box, isStart);
            var play = AddChip(card, null, "", 288, y, 36, 26, (s, e) => PlayFile(isStart ? settings.StartSound : settings.StopSound));
            play.Glyph = "\uE768";
            new ToolTip().SetToolTip(play, "Preview");
            var reset = AddChip(card, null, "", 330, y, 36, 26, (s, e) =>
            {
                string def = BaseSounds.PathFor(isStart ? "Chime Up" : "Chime Down");
                if (isStart) settings.StartSound = def; else settings.StopSound = def;
                settings.Save();
                FillSounds(box, def);
                PlayFile(def);
            });
            reset.Glyph = "\uE777";
            new ToolTip().SetToolTip(reset, "Back to the default sound");
            return box;
        }

        bool fillingSounds;

        void FillSounds(ComboBox box, string current)
        {
            fillingSounds = true;
            box.Items.Clear();
            SoundItem selected = null;
            foreach (var kv in BaseSounds.All())
            {
                var item = new SoundItem { Name = kv.Key, File = kv.Value };
                box.Items.Add(item);
                if (string.Equals(kv.Value, current, StringComparison.OrdinalIgnoreCase)) selected = item;
            }
            if (selected != null) box.SelectedItem = selected;
            else if (box.Items.Count > 0) box.SelectedIndex = 0;
            fillingSounds = false;
        }

        void OnSoundPicked(ComboBox box, bool isStart)
        {
            if (fillingSounds) return;
            var item = box.SelectedItem as SoundItem;
            if (item == null || item.File == null) return;
            if (isStart) settings.StartSound = item.File; else settings.StopSound = item.File;
            settings.Save();
            PlayFile(item.File);
        }

        // Only Simon Flow's own sounds are allowed.
        static bool AllowedSound(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return BaseSounds.IsBuiltIn(path);
        }

        void FillLanguages()
        {
            languageBox.Items.Clear();
            try
            {
                foreach (var r in SpeechRecognitionEngine.InstalledRecognizers())
                {
                    var item = new LangItem { Code = r.Culture.Name, Name = r.Culture.DisplayName };
                    languageBox.Items.Add(item);
                    if (r.Culture.Name == settings.Culture) languageBox.SelectedItem = item;
                }
            }
            catch (Exception ex) { Log.Write("recognizers: " + ex.Message); }
            if (languageBox.Items.Count == 0) languageBox.Items.Add(new LangItem { Code = "", Name = "(none installed)" });
            if (languageBox.SelectedIndex < 0) languageBox.SelectedIndex = 0;
        }

        void BuildTray()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open Simon Flow", null, (s, e) => ShowWindow());
            trayToggle = new ToolStripMenuItem("Start dictation", null, (s, e) => { startedFromWindow = false; Toggle(); });
            menu.Items.Add(trayToggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit Simon Flow", null, (s, e) => { quitting = true; Close(); });

            tray = new NotifyIcon
            {
                Icon = Icon ?? SystemIcons.Application,
                Text = "Simon Flow",
                ContextMenuStrip = menu,
                Visible = true,
            };
            tray.DoubleClick += (s, e) => ShowWindow();
        }

        void ShowWindow()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void ShowSettings(bool show)
        {
            if (!show && capturingShortcut) EndShortcutCapture("Shortcut unchanged.");
            root.SuspendLayout();
            foreach (var c in mainControls) c.Visible = !show;
            foreach (var c in settingsControls) c.Visible = show;
            root.ResumeLayout();
            tabDictate.Selected = !show;
            tabSettings.Selected = show;
            tabDictate.Invalidate();
            tabSettings.Invalidate();
            UpdateLabels();
        }

        void Note(string s)
        {
            if (noteLabel != null) noteLabel.Text = s;
            if (statusLabel != null) statusLabel.Text = s;
            if (overlay != null) overlay.Note = s;
            Log.Write("note: " + s);
        }

        // ----------------------------------------------------------- colours

        Color CardColorAt(Control c)
        {
            var g = settings.Gradient;
            double t = (c.Top + c.Height / 2.0) / Math.Max(1, root.ClientSize.Height);
            var behind = Draw.GradientAt(g, t);
            // frosted: lighten dark backgrounds, slightly darken very light ones
            return Draw.Luminance(behind) > 0.75 ? Draw.Darken(behind, 0.18) : Draw.Blend(behind, Color.White, 0.16);
        }

        void ApplyColors()
        {
            root.Colors = settings.Gradient;
            var accent = settings.Accent;
            mic.Accent = accent;
            overlay.Accent = accent;
            foreach (var c in allChips) c.Accent = accent;
            foreach (var card in cards)
            {
                var cc = CardColorAt(card);
                card.BackColor = cc;
                foreach (Control child in card.Controls)
                {
                    if (child is TextBox) child.BackColor = cc;
                    else if (child is Field) ((Field)child).SetColor(Draw.Darken(cc, 0.22));
                    else if (child is ComboBox) child.BackColor = Draw.Darken(cc, 0.22);
                }
            }
            foreach (var d in themeDots)
            {
                d.Selected = settings.ThemeKey == (string)d.Tag;
                d.Invalidate();
            }
            root.Invalidate(true);
            UpdateChips();
            UpdateTitleBar();
        }

        void UpdateTitleBar()
        {
            if (!IsHandleCreated) return;
            var top = settings.Gradient[0];
            var text = Draw.Luminance(top) < 0.6 ? Color.White : Color.FromArgb(30, 30, 30);
            Native.TitleBar(Handle, top, text);
        }

        void ChooseColor()
        {
            using (var dlg = new ColorDialog { FullOpen = true, Color = Color.FromArgb(settings.CustomColor) })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                settings.CustomColor = dlg.Color.ToArgb();
                settings.ThemeKey = "custom";
                settings.Save();
                ApplyColors();
            }
        }

        void SetStyleKey(string key)
        {
            settings.Style = key;
            settings.Save();
            UpdateChips();
        }

        void UpdateChips()
        {
            foreach (var c in styleChips) { c.Selected = (string)c.Tag == settings.Style; c.Invalidate(); }
        }

        void UpdateLabels()
        {
            string k = settings.ApiKey ?? "";
            keyStatus.Text = k.Length >= 4 ? "Saved key ending in …" + k.Substring(k.Length - 4) : "No key: free mode. Add a key for AI cleanup, styles and translate.";
            if (!capturingShortcut) shortcutPill.Text = settings.HotKeyLabel;
            hintPill.Text = settings.HotKeyLabel;
            sideShortcut.Text = settings.HotKeyLabel;
            bool w = Whisper.Available;
            sideStatus.Text = w ? "●  Whisper ready" : "●  Basic speech";
            sideStatus.ForeColor = w ? Color.FromArgb(140, 255, 170) : Color.FromArgb(255, 220, 120);
            engineLabel.Text = w ? "Whisper (high accuracy) \u00B7 English" : "Basic speech (re-run Build to fix)";
            languageBox.Visible = !w;
        }

        void SaveKey()
        {
            var k = keyField.Box.Text.Trim();
            if (k.Length == 0) return;
            settings.ApiKey = k;
            settings.Save();
            keyField.Box.Text = "";
            UpdateLabels();
            Note("API key saved.");
        }

        // ----------------------------------------------------------- state

        void SetState(FlowState s)
        {
            var old = state;
            state = s;
            // Esc cancels, but only grab it while recording so it works normally the rest of the time
            if (s == FlowState.Recording && old != FlowState.Recording)
            {
                Native.RegisterHotKey(Handle, ESC_ID, Native.MOD_NOREPEAT, (uint)Keys.Escape);
                recordStart = DateTime.Now;
                lastRecSeconds = 0;
            }
            else if (s != FlowState.Recording && old == FlowState.Recording)
                Native.UnregisterHotKey(Handle, ESC_ID);
            mic.State = s;
            switch (s)
            {
                case FlowState.Idle:
                    statusLabel.Text = "Click the mic to talk, or press " + settings.HotKeyLabel + "\nin any app to type straight into it.";
                    trayToggle.Text = "Start dictation";
                    if (old != FlowState.Idle) HidePillSoon();
                    break;
                case FlowState.Recording:
                    statusLabel.Text = "Listening… click again (or press " + settings.HotKeyLabel + ") when you're done.";
                    trayToggle.Text = "Stop dictation";
                    overlay.Mode = "rec";
                    overlay.Note = "";
                    overlay.ShowPill();
                    break;
                default:
                    statusLabel.Text = "Cleaning up your words…";
                    overlay.Mode = "busy";
                    overlay.BusyText = string.IsNullOrEmpty(settings.TranslateTo) ? "Cleaning up" : "Translating";
                    overlay.ShowPill();
                    break;
            }
            mic.Enabled = s == FlowState.Idle || s == FlowState.Recording;
            UpdateLabels();
        }

        void ApplyAlwaysPill()
        {
            overlay.AlwaysOn = settings.AlwaysShowPill;
            if (state != FlowState.Idle) return;
            if (settings.AlwaysShowPill) { overlay.Mode = "idle"; overlay.ShowPill(); }
            else overlay.HidePill();
        }

        void HidePillSoon()
        {
            overlay.Mode = "done";
            overlay.Invalidate();
            int mySession = session;
            After(1300, () => { if (state == FlowState.Idle && session == mySession) overlay.HidePill(); });
        }

        static void After(int ms, Action a)
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += (s, e) => { t.Stop(); t.Dispose(); a(); };
            t.Start();
        }

        void HoldTick()
        {
            if (state != FlowState.Recording) { holdTimer.Stop(); return; }
            if (Native.KeyDown(settings.HotKeyCode)) return;
            holdTimer.Stop();
            // a quick tap keeps the normal behaviour (press again to finish); a real hold finishes on release
            if ((DateTime.Now - holdStart).TotalMilliseconds < 350) return;
            StopListening();
        }

        void Toggle()
        {
            if (state == FlowState.Idle) StartListening();
            else if (state == FlowState.Recording) StopListening();
        }

        // ----------------------------------------------------------- hotkey

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == ESC_ID)
            {
                CancelRecording();
                return;
            }
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                bool wasIdle = state == FlowState.Idle;
                if (wasIdle)
                {
                    targetWindow = Native.GetForegroundWindow();
                    startedFromWindow = false;
                }
                Toggle();
                // Hold to talk: watch for the key being let go
                if (wasIdle && settings.HoldToTalk && state == FlowState.Recording)
                {
                    holdStart = DateTime.Now;
                    holdTimer.Start();
                }
            }
            base.WndProc(ref m);
        }

        bool RegisterCurrentHotKey()
        {
            try
            {
                Native.UnregisterHotKey(Handle, HOTKEY_ID);
                bool ok = Native.RegisterHotKey(Handle, HOTKEY_ID, (uint)settings.HotKeyMods | Native.MOD_NOREPEAT, (uint)settings.HotKeyCode);
                Log.Write("hotkey " + settings.HotKeyLabel + " registered=" + ok);
                return ok;
            }
            catch (Exception ex) { Log.Write("hotkey error: " + ex.Message); return false; }
        }

        void ToggleShortcutCapture()
        {
            if (capturingShortcut) { EndShortcutCapture("Shortcut unchanged."); return; }
            if (state == FlowState.Recording) StopListening();
            Native.UnregisterHotKey(Handle, HOTKEY_ID);
            capturingShortcut = true;
            shortcutPill.Text = "Press keys…";
            changeShortcutButton.Text = "Cancel";
            Note("Press your new shortcut (Esc to cancel).");
        }

        void EndShortcutCapture(string note)
        {
            capturingShortcut = false;
            changeShortcutButton.Text = "Change";
            RegisterCurrentHotKey();
            UpdateLabels();
            Note(note);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (capturingShortcut)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                var key = e.KeyCode;
                if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
                    key == Keys.LWin || key == Keys.RWin) return;
                if (key == Keys.Escape && e.Modifiers == Keys.None) { EndShortcutCapture("Shortcut unchanged."); return; }

                bool fkey = key >= Keys.F1 && key <= Keys.F24;
                if (!fkey && !e.Control && !e.Alt)
                {
                    Note("Use Ctrl or Alt together with a key (or an F-key), so normal typing isn't affected.");
                    return;
                }
                uint mods = 0;
                string label = "";
                if (e.Control) { mods |= Native.MOD_CONTROL; label += "Ctrl+"; }
                if (e.Alt) { mods |= Native.MOD_ALT; label += "Alt+"; }
                if (e.Shift) { mods |= Native.MOD_SHIFT; label += "Shift+"; }
                label += KeyName(key);
                capturingShortcut = false;
                changeShortcutButton.Text = "Change";
                ApplyShortcut((int)mods, (int)key, label);
                return;
            }
            base.OnKeyDown(e);
        }

        static string KeyName(Keys k)
        {
            if (k == Keys.Space) return "Space";
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            if (k == Keys.Return) return "Enter";
            return k.ToString();
        }

        void ApplyShortcut(int mods, int code, string label)
        {
            int oldMods = settings.HotKeyMods, oldCode = settings.HotKeyCode;
            string oldLabel = settings.HotKeyLabel;
            settings.HotKeyMods = mods;
            settings.HotKeyCode = code;
            settings.HotKeyLabel = label;
            if (RegisterCurrentHotKey())
            {
                settings.Save();
                Note("Shortcut set to " + label + ".");
            }
            else
            {
                settings.HotKeyMods = oldMods;
                settings.HotKeyCode = oldCode;
                settings.HotKeyLabel = oldLabel;
                RegisterCurrentHotKey();
                Note(label + " is already used by another app. Try a different one.");
            }
            SetState(state);
        }

        // ----------------------------------------------------------- speech

        void StartListening()
        {
            if (Whisper.Available)
            {
                try
                {
                    if (recorder != null) { recorder.Dispose(); recorder = null; }
                    int dev = WaveRecorder.FindDevice(settings.MicName);
                    if (dev < 0 && !string.IsNullOrEmpty(settings.MicName))
                        Log.Write("mic '" + settings.MicName + "' not found, using Windows default");
                    recorder = new WaveRecorder();
                    recorder.Start(dev);
                    usingWhisper = true;
                    session++;
                    transcript = new StringBuilder();
                    heardBox.Text = "";
                    level = 0;
                    PlaySound(true);
                    SetState(FlowState.Recording);
                    Log.Write("recording for whisper");
                    return;
                }
                catch (Exception ex)
                {
                    if (recorder != null) { recorder.Dispose(); recorder = null; }
                    Log.Write("whisper recording failed, falling back: " + ex.Message);
                }
            }

            usingWhisper = false;
            try
            {
                var all = SpeechRecognitionEngine.InstalledRecognizers();
                var info = all.FirstOrDefault(r => r.Culture.Name == settings.Culture) ?? all.FirstOrDefault();
                if (info == null)
                {
                    Note("No speech engine available. Run the Build file again to download Whisper, or type and click Fix text.");
                    ShowWindow();
                    return;
                }
                engine = new SpeechRecognitionEngine(info);
                engine.LoadGrammar(new DictationGrammar());
                engine.SetInputToDefaultAudioDevice();
                int mySession = ++session;
                transcript = new StringBuilder();
                heardBox.Text = "";
                engine.SpeechRecognized += (s, e) => BeginInvoke(new Action(() =>
                {
                    if (mySession != session) return;
                    transcript.Append(e.Result.Text).Append(' ');
                    heardBox.Text = transcript.ToString();
                }));
                engine.SpeechHypothesized += (s, e) => BeginInvoke(new Action(() =>
                {
                    if (mySession == session && state == FlowState.Recording)
                        heardBox.Text = transcript.ToString() + e.Result.Text;
                }));
                engine.AudioLevelUpdated += (s, e) => { level = e.AudioLevel; };
                engine.RecognizeCompleted += (s, e) => BeginInvoke(new Action(() =>
                {
                    if (mySession == session && state == FlowState.Transcribing) Finish();
                }));
                engine.RecognizeAsync(RecognizeMode.Multiple);
                PlaySound(true);
                SetState(FlowState.Recording);
                Log.Write("listening with Windows recognizer (" + info.Culture.Name + ")");
            }
            catch (Exception ex)
            {
                Log.Write("start failed: " + ex);
                Note("Couldn't start the microphone: " + ex.Message +
                     "\nCheck Settings > Privacy > Microphone > Let desktop apps access your microphone.");
                ShowWindow();
                DisposeEngine();
            }
        }

        // Mic level for the pill, 0-100 (or -1 if it can't be read).
        int ReadLevel()
        {
            if (state != FlowState.Recording) return 0;
            if (!usingWhisper) return level;
            return recorder != null ? recorder.Level : -1;
        }

        // Throw the recording away: nothing gets transcribed or typed.
        void CancelRecording()
        {
            if (state != FlowState.Recording) return;
            session++;
            level = 0;
            if (usingWhisper)
            {
                if (recorder != null) { recorder.Dispose(); recorder = null; }
            }
            else
            {
                try { if (engine != null) engine.RecognizeAsyncCancel(); } catch { }
                DisposeEngine();
            }
            transcript = new StringBuilder();
            heardBox.Text = "";
            overlay.Note = "Cancelled";
            SetState(FlowState.Idle);
            statusLabel.Text = "Cancelled. " + statusLabel.Text;
            Log.Write("cancelled");
        }

        void StopListening()
        {
            if (state != FlowState.Recording) return;
            lastRecSeconds = (DateTime.Now - recordStart).TotalSeconds;
            PlaySound(false);
            SetState(FlowState.Transcribing);
            if (usingWhisper) { StopWhisper(); return; }
            try { engine.RecognizeAsyncStop(); } catch { }
            int mySession = session;
            After(2500, () =>
            {
                if (mySession == session && state == FlowState.Transcribing)
                {
                    try { engine.RecognizeAsyncCancel(); } catch { }
                    Finish();
                }
            });
        }

        async void StopWhisper()
        {
            level = 0;
            int mySession = session;
            try
            {
                try { File.Delete(wavPath); } catch { }
                var rec = recorder;
                recorder = null;
                rec.StopAndSave(wavPath);
                Log.Write("recording peak " + rec.PeakAll);
            }
            catch (Exception ex)
            {
                if (recorder != null) { recorder.Dispose(); recorder = null; }
                SetState(FlowState.Idle);
                Note("Couldn't record: " + ex.Message);
                return;
            }

            string text;
            try
            {
                string path = wavPath;
                text = await Task.Run(() => Whisper.Transcribe(path));
            }
            catch (Exception ex)
            {
                Log.Write("whisper failed: " + ex.Message);
                if (mySession != session) return;
                SetState(FlowState.Idle);
                Note(ex.Message);
                return;
            }
            if (mySession != session || state != FlowState.Transcribing) return;
            heardBox.Text = text;
            Log.Write("whisper heard: '" + text + "'");
            if (text.Length == 0)
            {
                SetState(FlowState.Idle);
                Note("Didn't hear anything.");
                return;
            }
            CleanAndDeliver(text);
        }

        void DisposeEngine()
        {
            if (engine == null) return;
            try { engine.Dispose(); } catch { }
            engine = null;
        }

        void Finish()
        {
            if (state != FlowState.Transcribing) return;
            DisposeEngine();
            level = 0;
            string text = transcript.ToString().Trim();
            Log.Write("heard: '" + text + "'");
            if (text.Length == 0)
            {
                SetState(FlowState.Idle);
                Note("Didn't hear anything.");
                return;
            }
            CleanAndDeliver(text);
        }

        void FixTypedText()
        {
            string text = heardBox.Text.Trim();
            if (text.Length == 0) { Note("Say or type something first."); return; }
            if (state != FlowState.Idle) return;
            if (string.IsNullOrEmpty(settings.ApiKey)) { ShowSettings(true); Note("Add your Anthropic API key first."); return; }
            startedFromWindow = true;
            session++;
            CleanAndDeliver(text);
        }

        // Free-mode cleanup: drop filler words, fix spacing, capitals and the final period.
        static string BasicCleanup(string text)
        {
            string t = (text ?? "").Trim();
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\[[^\]]*\]|\([^\)]*\)", " ");                 // [BLANK_AUDIO], (music)
            t = System.Text.RegularExpressions.Regex.Replace(t, @"(?i)\b(um+|uh+|erm+|hmm+|uhm+|mm+)\b[,.]?\s*", "");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+([,.!?;:])", "$1");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"^[,.;:\s]+", "");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\bi\b", "I");
            t = System.Text.RegularExpressions.Regex.Replace(t, @"(^|[.!?]\s+)([a-z])", m => m.Groups[1].Value + m.Groups[2].Value.ToUpper());
            if (t.Length > 0 && char.IsLetterOrDigit(t[t.Length - 1])) t += ".";
            return t;
        }

        static int CountWords(string text)
        {
            int n = 0;
            foreach (var w in (text ?? "").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (w.Any(char.IsLetterOrDigit)) n++;
            return n;
        }

        void RecordWpm(string raw)
        {
            double secs = lastRecSeconds;
            lastRecSeconds = 0;
            int words = CountWords(raw);
            if (secs < 1 || words == 0) return;
            int wpm = (int)Math.Round(words / (secs / 60.0));
            settings.WpmWords += words;
            settings.WpmSeconds += secs;
            settings.Save();
            Log.Write("wpm " + wpm + " (" + words + " words in " + secs.ToString("0.0") + "s)");
            ShowWpm(wpm, words, secs);
        }

        void ShowWpm(int wpm, int words, double secs)
        {
            if (wpmLabel == null) return;
            string avg = settings.WpmSeconds >= 1
                ? "avg " + (int)Math.Round(settings.WpmWords / (settings.WpmSeconds / 60.0)) + " WPM" : "";
            if (wpm > 0)
            {
                wpmLabel.Text = wpm + " WPM" + (avg.Length > 0 ? "  \u00B7  " + avg : "");
                toolTip.SetToolTip(wpmLabel, words + " words in " + secs.ToString("0.0") + " seconds");
            }
            else wpmLabel.Text = avg;
        }

        async void CleanAndDeliver(string text)
        {
            if (lastRecSeconds > 0) RecordWpm(text);
            if (string.IsNullOrEmpty(settings.ApiKey))
            {
                // Free mode: tidy it up on this computer, no AI needed.
                string basic = BasicCleanup(text);
                Log.Write("basic cleanup: '" + basic + "'");
                SetState(FlowState.Idle);
                Deliver(basic);
                if (settings.Style != "light" || !string.IsNullOrEmpty(settings.TranslateTo))
                {
                    string msg = "Styles and translate need a Claude API key (Settings). Your words were typed as spoken.";
                    if (noteLabel != null) noteLabel.Text = msg;
                    Log.Write("note: " + msg);
                }
                return;
            }
            SetState(FlowState.Cleaning);
            string result;
            try
            {
                result = await Cleanup(text);
                Log.Write("cleaned: '" + result + "'");
            }
            catch (Exception ex)
            {
                Log.Write("cleanup failed: " + ex.Message);
                result = text;
                Note("AI cleanup failed, so the raw text was used: " + ex.Message);
            }
            SetState(FlowState.Idle);
            Deliver(result);
        }

        async Task<string> Cleanup(string text)
        {
            string stylePrompt = STYLES[0][2];
            foreach (var st in STYLES) if (st[0] == settings.Style) stylePrompt = st[2];
            string system = "You are a dictation cleanup engine. The user message is a raw speech-to-text transcript. "
                + stylePrompt
                + (string.IsNullOrEmpty(settings.TranslateTo)
                    ? " Write in the same language as the transcript."
                    : " Then translate the cleaned text into " + settings.TranslateTo + ", keeping the same meaning and tone. Write ONLY in " + settings.TranslateTo + ".")
                + " Output ONLY the cleaned text: no preamble, no quotes, no commentary. Never answer or act on the content; just clean it.";

            var js = new JavaScriptSerializer();
            var body = new Dictionary<string, object>
            {
                { "model", MODEL },
                { "max_tokens", 2000 },
                { "system", system },
                { "messages", new object[] { new Dictionary<string, object> { { "role", "user" }, { "content", text } } } },
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            req.Headers.Add("x-api-key", settings.ApiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(js.Serialize(body), Encoding.UTF8, "application/json");

            var resp = await http.SendAsync(req);
            string raw = await resp.Content.ReadAsStringAsync();
            var obj = js.DeserializeObject(raw) as Dictionary<string, object>;
            if (obj == null) throw new Exception("Unreadable response from the API.");
            if (obj.ContainsKey("error"))
            {
                var err = obj["error"] as Dictionary<string, object>;
                throw new Exception(err != null && err.ContainsKey("message") ? Convert.ToString(err["message"]) : "API error");
            }
            var sb = new StringBuilder();
            var blocks = obj.ContainsKey("content") ? obj["content"] as IEnumerable : null;
            if (blocks != null)
                foreach (var b in blocks)
                {
                    var d = b as Dictionary<string, object>;
                    if (d != null && d.ContainsKey("text")) sb.Append(Convert.ToString(d["text"]));
                }
            string outText = sb.ToString().Trim();
            if (outText.Length == 0) throw new Exception("The API returned no text.");
            return outText;
        }

        // ----------------------------------------------------------- output

        void Deliver(string text)
        {
            resultBox.Text = text;
            if (startedFromWindow)
            {
                try { Clipboard.SetText(text); } catch { }
                overlay.Note = "Copied";
                statusLabel.Text = "Done. Copied to the clipboard, so paste it anywhere with Ctrl+V.";
            }
            else
            {
                Paste(text);
            }
        }

        void Paste(string text)
        {
            string previous = null;
            try { if (Clipboard.ContainsText()) previous = Clipboard.GetText(); } catch { }
            try { Clipboard.SetText(text); }
            catch { Note("Couldn't use the clipboard."); return; }

            if (targetWindow != IntPtr.Zero) Native.SetForegroundWindow(targetWindow);
            Native.keybd_event(0x11, 0, 0, UIntPtr.Zero);                       // Ctrl down
            Native.keybd_event(0x56, 0, 0, UIntPtr.Zero);                       // V down
            Native.keybd_event(0x56, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);  // V up
            Native.keybd_event(0x11, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);  // Ctrl up
            overlay.Note = "Pasted";
            Log.Write("pasted");

            After(800, () =>
            {
                if (previous == null) return;
                try { Clipboard.SetText(previous); } catch { }
            });
        }

        void CopyResult()
        {
            if (resultBox.Text.Length == 0) return;
            try { Clipboard.SetText(resultBox.Text); statusLabel.Text = "Copied to the clipboard."; } catch { }
        }

        void PlaySound(bool start)
        {
            if (!settings.Sounds) return;
            PlayFile(start ? settings.StartSound : settings.StopSound, start);
        }

        // Returns the .wav with its loudness scaled (0-100%), ready for SoundPlayer.
        static Stream WithVolume(string path, int volume)
        {
            byte[] wav = File.ReadAllBytes(path);
            double gain = Math.Max(0, Math.Min(100, volume)) / 100.0;
            gain = gain * gain;   // feels more even to the ear
            try
            {
                int bits = 16, pos = 12;
                while (pos + 8 <= wav.Length)
                {
                    string id = Encoding.ASCII.GetString(wav, pos, 4);
                    int size = BitConverter.ToInt32(wav, pos + 4);
                    if (id == "fmt " && pos + 24 <= wav.Length)
                    {
                        if (BitConverter.ToInt16(wav, pos + 8) != 1) break;          // not plain PCM: leave as is
                        bits = BitConverter.ToInt16(wav, pos + 22);
                    }
                    else if (id == "data")
                    {
                        int end = Math.Min(wav.Length, pos + 8 + size);
                        if (bits == 16)
                            for (int i = pos + 8; i + 1 < end; i += 2)
                            {
                                int v = (int)(BitConverter.ToInt16(wav, i) * gain);
                                wav[i] = (byte)(v & 0xFF); wav[i + 1] = (byte)((v >> 8) & 0xFF);
                            }
                        else if (bits == 8)
                            for (int i = pos + 8; i < end; i++)
                                wav[i] = (byte)(128 + (int)((wav[i] - 128) * gain));
                        break;
                    }
                    pos += 8 + size + (size & 1);
                }
            }
            catch { }
            return new MemoryStream(wav);
        }

        void PlayFile(string path, bool startFallback = true)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var player = new SoundPlayer(WithVolume(path, settings.SoundVolume));
                    player.Play();
                }
                else if (startFallback) SystemSounds.Asterisk.Play();
                else SystemSounds.Beep.Play();
            }
            catch (Exception ex)
            {
                Log.Write("sound failed: " + ex.Message);
                Note("That sound couldn't be played. Pick a different one.");
            }
        }

        // ----------------------------------------------------------- start with Windows

        const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static bool IsStartupEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RUN_KEY))
                    return k != null && k.GetValue("SimonFlow") != null;
            }
            catch { return false; }
        }

        void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("SimonFlow", "\"" + Application.ExecutablePath + "\" --background");
                    else if (k.GetValue("SimonFlow") != null) k.DeleteValue("SimonFlow");
                }
                Note(on ? "Simon Flow will start with Windows." : "Simon Flow won't start with Windows.");
            }
            catch (Exception ex) { Note("Couldn't change that setting: " + ex.Message); }
        }

        // ----------------------------------------------------------- closing

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!quitting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!shownTrayTip)
                {
                    shownTrayTip = true;
                    tray.ShowBalloonTip(3000, "Simon Flow is still running",
                        "Press " + settings.HotKeyLabel + " in any app. Right-click the icon near the clock to quit.", ToolTipIcon.Info);
                }
                return;
            }
            Native.UnregisterHotKey(Handle, HOTKEY_ID);
            Native.UnregisterHotKey(Handle, ESC_ID);
            if (recorder != null) { recorder.Dispose(); recorder = null; }
            DisposeEngine();
            tray.Visible = false;
            tray.Dispose();
            overlay.Close();
            base.OnFormClosing(e);
        }
    }
}
