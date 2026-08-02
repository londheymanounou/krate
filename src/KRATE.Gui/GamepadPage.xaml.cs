using System.Runtime.InteropServices;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Krate.Gui;

/// <summary>Live controller tester. Xbox / XInput pads get the familiar labelled layout via XInput;
/// everything else (PlayStation, Switch, generic USB) is read through the DirectInput/HID joystick API
/// (<c>winmm</c> <c>joyGetPosEx</c>) — the same one Windows' own "Game Controllers" panel uses — and
/// laid out in a controller shape using the common generic-pad button order (each position labelled with
/// its raw button number so any mismatch is visible).</summary>
// ponytail: two Win32 APIs cover every controller on Windows with zero dependencies. XInput first (clean
// Xbox mapping), joyGetPosEx as the catch-all. Generic button order is the de-facto PS-style convention
// (1 Square, 2 Cross, 3 Circle, 4 Triangle, 5/6 L1/R1, 7/8 L2/R2, 9/10 Select/Start, 11/12 L3/R3);
// exotic pads that differ show the raw number, so remapping is a one-line change if ever needed.
public sealed partial class GamepadPage : UserControl
{
    // ---- XInput ----
    [StructLayout(LayoutKind.Sequential)]
    struct XInputGamepad { public ushort Buttons; public byte LeftTrigger; public byte RightTrigger; public short LX; public short LY; public short RX; public short RY; }
    [StructLayout(LayoutKind.Sequential)]
    struct XInputState { public uint Packet; public XInputGamepad Gamepad; }
    [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint index, out XInputState state);

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
    const uint JoyReturnAll = 0xFF;

    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    (Border Chip, ushort Mask, Brush On)[] _xboxButtons = [];
    (Border Chip, int Button)[] _genMap = [];       // fixed controller positions → 1-based button number
    (Border Chip, int Button)[] _extraButtons = []; // any button number not in _genMap
    int _genBuiltFor = -1;

    public GamepadPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Gamepad_Title");
        GenHint.Text = Strings.Get("Gamepad_GenericHint");
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        _xboxButtons =
        [
            (BtnA, 0x1000, Rgb(0x6C, 0xB3, 0x3F)), (BtnB, 0x2000, Rgb(0xD1, 0x4A, 0x3F)),
            (BtnX, 0x4000, Rgb(0x2E, 0x6D, 0xB4)), (BtnY, 0x8000, Rgb(0xE3, 0xB1, 0x1E)),
            (BtnLB, 0x0100, accent), (BtnRB, 0x0200, accent),
            (BtnView, 0x0020, accent), (BtnMenu, 0x0010, accent),
            (BtnUp, 0x0001, accent), (BtnDown, 0x0002, accent),
            (BtnLeft, 0x0004, accent), (BtnRight, 0x0008, accent),
            (BtnLS, 0x0040, accent), (BtnRS, 0x0080, accent),
        ];
        // This pad's actual button order (reported by pressing each button), position → 1-based number.
        _genMap =
        [
            (GFaceBottom, 1), (GFaceRight, 2), (GFaceLeft, 4), (GFaceTop, 5),
            (GL1, 7), (GR1, 8), (GL2, 9), (GR2, 10),
            (GSelect, 11), (GStart, 12), (GHome, 13), (GL3, 14), (GR3, 15),
        ];
        _timer.Tick += OnTick;
    }

    void OnLoaded(object sender, RoutedEventArgs e) => _timer.Start();
    void OnUnloaded(object sender, RoutedEventArgs e) => _timer.Stop();

    static SolidColorBrush Rgb(byte r, byte g, byte b) => new(Windows.UI.Color.FromArgb(255, r, g, b));
    Brush Accent => (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
    Brush Off => (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
    Brush Stroke => (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];

    void OnTick(object? sender, object e)
    {
        for (uint i = 0; i < 4; i++)
            if (TryXInput(i, out var st)) { ShowXbox(st); return; }

        var count = SafeNumDevs();
        for (uint i = 0; i < count; i++)
        {
            var info = new JoyInfoEx { Size = (uint)Marshal.SizeOf<JoyInfoEx>(), Flags = JoyReturnAll };
            if (joyGetPosEx(i, ref info) == 0) { ShowGeneric(i, info); return; }
        }

        // No device: keep whichever layout is already showing (don't snap back to Xbox on unplug);
        // just clear it so nothing reads as pressed and the sticks recentre.
        Status.Text = Strings.Get("Gamepad_None");
        foreach (var (chip, _, _) in _xboxButtons) chip.Background = Off;
        foreach (var (chip, _) in _genMap) chip.Background = Off;
        foreach (var (chip, _) in _extraButtons) chip.Background = Off;
        PovUp.Background = PovDown.Background = PovLeft.Background = PovRight.Background = Off;
        TrigL.Value = TrigR.Value = 0;
        Place(DotL, 0, 0, 65, 52, 13); Place(DotR, 0, 0, 65, 52, 13);
        Place(GDotL, 0, 0, 65, 52, 13); Place(GDotR, 0, 0, 65, 52, 13);
    }

    static bool TryXInput(uint i, out XInputState st)
    {
        st = default;
        try { return XInputGetState(i, out st) == 0; }
        catch (DllNotFoundException) { return false; }
    }

    static uint SafeNumDevs()
    {
        try { return joyGetNumDevs(); }
        catch (DllNotFoundException) { return 0; }
    }

    void ShowXbox(XInputState state)
    {
        Status.Text = Strings.Get("Gamepad_Connected");
        XboxPanel.Visibility = Visibility.Visible;
        GenericPanel.Visibility = GenHint.Visibility = ExtraPanel.Visibility = Visibility.Collapsed;
        var g = state.Gamepad;
        foreach (var (chip, mask, on) in _xboxButtons)
            chip.Background = (g.Buttons & mask) != 0 ? on : Off;
        LtLabel.Text = $"{Strings.Get("Gamepad_LT")}  {g.LeftTrigger / 255.0:P0}";
        RtLabel.Text = $"{Strings.Get("Gamepad_RT")}  {g.RightTrigger / 255.0:P0}";
        TrigL.Value = g.LeftTrigger / 255.0 * 100;
        TrigR.Value = g.RightTrigger / 255.0 * 100;
        Place(DotL, g.LX / 32767.0, g.LY / 32767.0, 65, 52, 13);
        Place(DotR, g.RX / 32767.0, g.RY / 32767.0, 65, 52, 13);
    }

    void ShowGeneric(uint id, JoyInfoEx info)
    {
        Status.Text = Strings.Get("Gamepad_Connected");
        XboxPanel.Visibility = Visibility.Collapsed;
        GenericPanel.Visibility = GenHint.Visibility = Visibility.Visible;

        if (_genBuiltFor != (int)id) BuildGeneric(id);

        // Fixed controller positions.
        foreach (var (chip, button) in _genMap)
            chip.Background = (info.Buttons & (1u << (button - 1))) != 0 ? Accent : Off;

        // Any button number not shown in a fixed position.
        foreach (var (chip, num) in _extraButtons)
            chip.Background = (info.Buttons & (1u << (num - 1))) != 0 ? Accent : Off;

        // Sticks: X/Y left, Z/R right (0..65535, centre 32767; Y is inverted so negate for y-up).
        Place(GDotL, info.X / 32767.5 - 1, -(info.Y / 32767.5 - 1), 65, 52, 13);
        Place(GDotR, info.Z / 32767.5 - 1, -(info.R / 32767.5 - 1), 65, 52, 13);

        // POV hat → d-pad. 0xFFFF (or out of range) = centred; else centi-degrees clockwise from up.
        var pov = info.POV;
        var centred = pov == 0xFFFF || pov > 36000;
        PovUp.Background = !centred && (pov >= 31500 || pov <= 4500) ? Accent : Off;
        PovRight.Background = !centred && pov is >= 4500 and <= 13500 ? Accent : Off;
        PovDown.Background = !centred && pov is >= 13500 and <= 22500 ? Accent : Off;
        PovLeft.Background = !centred && pov is >= 22500 and <= 31500 ? Accent : Off;
    }

    void BuildGeneric(uint id)
    {
        _genBuiltFor = (int)id;
        var count = 12;
        var name = $"{Strings.Get("Gamepad_Generic")} #{id + 1}";
        try
        {
            if (joyGetDevCaps(id, out var caps, (uint)Marshal.SizeOf<JoyCaps>()) == 0)
            {
                count = Math.Clamp((int)caps.NumButtons, 1, 32);
                if (!string.IsNullOrWhiteSpace(caps.Pname)) name = caps.Pname;
            }
        }
        catch (DllNotFoundException) { }
        GenName.Text = name;

        // Any button number the fixed layout doesn't cover gets a numbered chip in the extras row.
        GenButtons.Children.Clear();
        GenButtons.ColumnDefinitions.Clear();
        GenButtons.RowDefinitions.Clear();
        var mapped = _genMap.Select(m => m.Button).ToHashSet();
        var extras = Enumerable.Range(1, count).Where(n => !mapped.Contains(n)).ToArray();
        ExtraPanel.Visibility = extras.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExtraLabel.Text = Strings.Get("Gamepad_Extra");
        const int cols = 8;
        for (var c = 0; c < cols; c++) GenButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _extraButtons = new (Border, int)[extras.Length];
        for (var i = 0; i < extras.Length; i++)
        {
            int row = i / cols, col = i % cols;
            while (GenButtons.RowDefinitions.Count <= row) GenButtons.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var chip = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1), BorderBrush = Stroke, Background = Off,
                Child = new TextBlock { Text = extras[i].ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Grid.SetRow(chip, row);
            Grid.SetColumn(chip, col);
            GenButtons.Children.Add(chip);
            _extraButtons[i] = (chip, extras[i]);
        }
    }

    // Move a stick dot: centre it, then offset by the axis values (already y-up, clamped).
    static void Place(FrameworkElement dot, double x, double y, double centre, double radius, double half)
    {
        Canvas.SetLeft(dot, centre + Math.Clamp(x, -1, 1) * radius - half);
        Canvas.SetTop(dot, centre - Math.Clamp(y, -1, 1) * radius - half);
    }
}
