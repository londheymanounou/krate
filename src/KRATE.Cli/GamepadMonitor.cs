using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Krate.Core;

namespace Krate.Cli;

/// <summary>`krate gamepad` — a live terminal controller monitor (jstest-style). Reads XInput first
/// (Xbox pads) then the winmm DirectInput/HID joystick API (everything else), and redraws the state in
/// place until a key is pressed. Windows-only; the two APIs are the same ones the GUI tester uses.</summary>
static class GamepadMonitor
{
    // ---- XInput ----
    [StructLayout(LayoutKind.Sequential)]
    struct XGamepad { public ushort Buttons; public byte LT; public byte RT; public short LX, LY, RX, RY; }
    [StructLayout(LayoutKind.Sequential)]
    struct XState { public uint Packet; public XGamepad Gamepad; }
    [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint index, out XState state);

    // ---- winmm joystick (DirectInput/HID) ----
    [StructLayout(LayoutKind.Sequential)]
    struct JoyInfoEx { public uint Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, POV, R1, R2; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct JoyCaps
    {
        public ushort Mid, Pid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Pname;
        public uint Xmin, Xmax, Ymin, Ymax, Zmin, Zmax, NumButtons, PeriodMin, PeriodMax,
                    Rmin, Rmax, Umin, Umax, Vmin, Vmax, Caps, MaxAxes, NumAxes, MaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string OemVxD;
    }
    [DllImport("winmm.dll")] static extern uint joyGetNumDevs();
    [DllImport("winmm.dll")] static extern uint joyGetPosEx(uint id, ref JoyInfoEx info);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern uint joyGetDevCaps(uint id, out JoyCaps caps, uint size);

    public static int Run()
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine(Strings.Get("Cli_GamepadWindowsOnly")); return 2; }
        return RunWindows();
    }

    [SupportedOSPlatform("windows")]
    static int RunWindows()
    {
        Console.WriteLine(Strings.Get("Cli_GamepadHeader"));
        Console.WriteLine();

        // Piped/redirected: no live loop or keypress — print a single snapshot and leave.
        if (Console.IsInputRedirected) { foreach (var line in Frame()) Console.WriteLine(line); return 0; }

        var top = Console.CursorTop;
        var prev = 0;
        try { Console.CursorVisible = false; } catch { /* not a real console */ }
        try
        {
            while (!(Console.KeyAvailable && Console.ReadKey(intercept: true) is { })) // any key stops
            {
                var lines = Frame();
                for (var i = 0; i < lines.Count; i++) { SetLine(top + i); Console.Write(Pad(lines[i])); }
                for (var i = lines.Count; i < prev; i++) { SetLine(top + i); Console.Write(Pad("")); } // clear shrink
                prev = lines.Count;
                Thread.Sleep(50);
            }
        }
        finally { try { Console.CursorVisible = true; } catch { } SetLine(top + prev); }
        return 0;
    }

    [SupportedOSPlatform("windows")]
    static List<string> Frame()
    {
        var lines = new List<string>();
        for (uint i = 0; i < 4; i++)
            if (XInputGetState(i, out var st) == 0) { Xbox(lines, st.Gamepad); return lines; }

        var count = joyGetNumDevs();
        for (uint i = 0; i < count; i++)
        {
            var info = new JoyInfoEx { Size = (uint)Marshal.SizeOf<JoyInfoEx>(), Flags = 0xFF };
            if (joyGetPosEx(i, ref info) == 0) { Generic(lines, i, info); return lines; }
        }
        lines.Add(Strings.Get("Gamepad_None"));
        return lines;
    }

    static readonly (string Name, ushort Mask)[] XNames =
    [
        ("A", 0x1000), ("B", 0x2000), ("X", 0x4000), ("Y", 0x8000), ("LB", 0x0100), ("RB", 0x0200),
        ("Back", 0x0020), ("Start", 0x0010), ("Up", 0x0001), ("Down", 0x0002), ("Left", 0x0004), ("Right", 0x0008),
        ("LS", 0x0040), ("RS", 0x0080),
    ];

    static void Xbox(List<string> lines, XGamepad g)
    {
        lines.Add("Xbox / XInput controller");
        var pressed = XNames.Where(n => (g.Buttons & n.Mask) != 0).Select(n => n.Name);
        lines.Add($"Buttons : {(pressed.Any() ? string.Join(" ", pressed) : "-")}");
        lines.Add($"L-stick : {Ax(g.LX / 32767.0)} , {Ax(g.LY / 32767.0)}    R-stick : {Ax(g.RX / 32767.0)} , {Ax(g.RY / 32767.0)}");
        lines.Add($"Triggers: LT {Bar(g.LT / 255.0)}   RT {Bar(g.RT / 255.0)}");
    }

    static void Generic(List<string> lines, uint id, JoyInfoEx info)
    {
        var name = Strings.Get("Gamepad_Generic");
        try { if (joyGetDevCaps(id, out var caps, (uint)Marshal.SizeOf<JoyCaps>()) == 0 && !string.IsNullOrWhiteSpace(caps.Pname)) name = caps.Pname; }
        catch (DllNotFoundException) { }
        lines.Add($"{name} (DirectInput)");
        var pressed = Enumerable.Range(1, 32).Where(n => (info.Buttons & (1u << (n - 1))) != 0);
        lines.Add($"Buttons : {(pressed.Any() ? string.Join(" ", pressed) : "-")}");
        lines.Add($"Axes    : X {Ax(info.X / 32767.5 - 1)}  Y {Ax(info.Y / 32767.5 - 1)}  Z {Ax(info.Z / 32767.5 - 1)}  R {Ax(info.R / 32767.5 - 1)}");
        lines.Add($"POV     : {Pov(info.POV)}");
    }

    static string Ax(double v) => (v >= 0 ? "+" : "") + Math.Clamp(v, -1, 1).ToString("0.00", CultureInfo.InvariantCulture);

    static string Bar(double f)
    {
        var n = (int)Math.Round(Math.Clamp(f, 0, 1) * 10);
        return $"[{new string('#', n)}{new string('.', 10 - n)}] {f,4:P0}";
    }

    static string Pov(uint pov)
    {
        if (pov == 0xFFFF || pov > 36000) return "centre";
        string[] dirs = ["up", "up-right", "right", "down-right", "down", "down-left", "left", "up-left"];
        return dirs[(int)Math.Round(pov / 4500.0) % 8];
    }

    static void SetLine(int row) { try { Console.SetCursorPosition(0, row); } catch { /* off-screen */ } }

    static string Pad(string s)
    {
        int w;
        try { w = Console.WindowWidth - 1; } catch { w = 80; }
        if (w < 1) w = 80;
        return s.Length >= w ? s[..w] : s.PadRight(w);
    }
}
