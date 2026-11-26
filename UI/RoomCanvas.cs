using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ClubEQ.Model;

namespace ClubEQ.UI;

// Top-down room: walls, speakers (pulsing with their level), cars with their inside zone, and you. Click selects, drag
// moves, the mouse wheel turns what's under the cursor (Shift = fine). +z is up the screen.
public sealed class RoomCanvas : FrameworkElement
{
    public Preset Preset;
    public object Selected;                 // a Speaker, the Room (= the listener), or null
    public Func<int, float> Level = _ => 0;  // per speaker, 0..1
    public Func<int, bool> InsideOf = _ => false;
    public event Action<object> SelectionChanged;
    public event Action Committed;          // after a drag or a turn: save, refresh the inspector

    double scale = 20, ox, oy;
    object dragging;
    Vector grab;
    bool moved;

    static Brush B(string hex, double a = 1)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        c.A = (byte)(a * 255);
        var b = new SolidColorBrush(c); b.Freeze(); return b;
    }
    static Pen P(Brush b, double w, bool dash = false)
    {
        var p = new Pen(b, w) { LineJoin = PenLineJoin.Round };
        if (dash) p.DashStyle = DashStyles.Dash;
        p.Freeze(); return p;
    }

    static readonly Brush Stage = B("#0B0A1E"), Floor = B("#100E28"), GridLine = B("#1A1740"), Text = B("#DAD6FF"), Dim = B("#9A95CC"),
        Neon = B("#FF3DA8"), NeonSoft = B("#FF3DA8", .18), Cyan = B("#3DE8FF"), CyanSoft = B("#3DE8FF", .15), Accent = B("#B9A4FF"),
        Body = B("#221D58"), Purple = B("#6D4BFF"), Plum = B("#2A1640"), Black = B("#15131F"), Cream = B("#EDE3C8");
    static readonly Pen GridPen = P(GridLine, 1), WallGlow = P(NeonSoft, 10), WallPen = P(Neon, 2), SelPen = P(Accent, 2),
        ZonePen = P(B("#B9A4FF", .6), 1.5, true), ZoneIn = P(Cyan, 2, true), Edge = P(B("#3A3488"), 1.5), CyanPen = P(Cyan, 2);

    public RoomCanvas() { Focusable = true; ClipToBounds = true; }

    Point S(float x, float z) => new(ox + x * scale, oy - z * scale);
    (float X, float Z) W(Point p) => ((float)((p.X - ox) / scale), (float)((oy - p.Y) / scale));

    void Fit()
    {
        var r = Preset.Room;
        double m = 1.2, w = r.MaxX - r.MinX + 2 * m, h = r.MaxZ - r.MinZ + 2 * m;
        scale = Math.Max(1, Math.Min(ActualWidth / w, ActualHeight / h));
        ox = ActualWidth / 2 - (r.MinX + r.MaxX) / 2 * scale;
        oy = ActualHeight / 2 + (r.MinZ + r.MaxZ) / 2 * scale;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Stage, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Preset == null || ActualWidth < 10) return;
        Fit();
        var r = Preset.Room;
        var room = new Rect(S(r.MinX, r.MaxZ), S(r.MaxX, r.MinZ));
        dc.DrawRectangle(Floor, null, room);
        for (int x = (int)Math.Ceiling(r.MinX); x <= r.MaxX; x++) dc.DrawLine(GridPen, S(x, r.MinZ), S(x, r.MaxZ));
        for (int z = (int)Math.Ceiling(r.MinZ); z <= r.MaxZ; z++) dc.DrawLine(GridPen, S(r.MinX, z), S(r.MaxX, z));
        dc.DrawRectangle(null, WallGlow, room);
        dc.DrawRectangle(null, WallPen, room);

        for (int i = 0; i < Preset.Speakers.Count; i++) DrawSpeaker(dc, Preset.Speakers[i], i);
        DrawListener(dc, r);
    }

    void DrawSpeaker(DrawingContext dc, Speaker s, int i)
    {
        var c = S(s.X, s.Z);
        float lvl = Math.Clamp(Level(i), 0, 1);
        bool sel = ReferenceEquals(Selected, s), off = s.Mute || (Preset.Speakers.Any(x => x.Solo) && !s.Solo);
        dc.PushOpacity(off ? .35 : 1);
        if (s.Inside != null)
        {
            var (zx, zz) = Mix.ZonePoint(s);
            dc.DrawEllipse(null, InsideOf(i) ? ZoneIn : ZonePen, S(zx, zz), s.Inside.Radius * scale, s.Inside.Radius * scale);
        }
        // the pulse: a soft neon ring that grows with the level
        double pr = Math.Max(10, .5 * scale) + lvl * .9 * scale;
        dc.DrawEllipse(B("#FF3DA8", .08 + .35 * lvl), null, c, pr, pr);

        dc.PushTransform(new RotateTransform(s.Yaw, c.X, c.Y));
        double u = scale;
        switch (s.Kind)
        {
            case SpeakerKind.Car:
                {
                    var body = new Rect(c.X - 1.0 * u, c.Y - 2.4 * u, 2.0 * u, 4.8 * u);
                    dc.DrawRoundedRectangle(Body, sel ? SelPen : Edge, body, .5 * u, .5 * u);
                    dc.DrawRoundedRectangle(Plum, null, new Rect(c.X - .8 * u, c.Y - 1.1 * u, 1.6 * u, 1.9 * u), .3 * u, .3 * u); // glasshouse
                    dc.DrawRectangle(Neon, null, new Rect(c.X - .9 * u, c.Y - 2.35 * u, 1.8 * u, .08 * u)); // nose light
                    dc.DrawEllipse(Cyan, null, c, Math.Max(3, .12 * u), Math.Max(3, .12 * u));
                    break;
                }
            case SpeakerKind.Boombox:
                {
                    double w = Math.Max(26, .86 * u), h = Math.Max(12, .3 * u);
                    dc.DrawRoundedRectangle(Purple, sel ? SelPen : Edge, new Rect(c.X - w / 2, c.Y - h / 2, w, h), 4, 4);
                    dc.DrawEllipse(Cream, null, new Point(c.X - w / 4, c.Y), h * .32, h * .32);
                    dc.DrawEllipse(Cream, null, new Point(c.X + w / 4, c.Y), h * .32, h * .32);
                    dc.DrawRectangle(Neon, null, new Rect(c.X - w / 2, c.Y - h / 2 - 3, w, 2)); // front
                    break;
                }
            case SpeakerKind.Cabinet:
                {
                    double w = Math.Max(20, .52 * u), h = Math.Max(16, .44 * u);
                    dc.DrawRoundedRectangle(Black, sel ? SelPen : Edge, new Rect(c.X - w / 2, c.Y - h / 2, w, h), 3, 3);
                    dc.DrawEllipse(null, P(Neon, 2), c, h * .32, h * .32);
                    dc.DrawRectangle(Neon, null, new Rect(c.X - w / 2, c.Y - h / 2 - 3, w, 2));
                    break;
                }
            default:
                dc.DrawEllipse(Body, sel ? SelPen : Edge, c, Math.Max(10, .3 * u), Math.Max(10, .3 * u));
                dc.DrawRectangle(Neon, null, new Rect(c.X - 6, c.Y - Math.Max(10, .3 * u) - 4, 12, 2));
                break;
        }
        dc.Pop();
        var at = new Point(c.X, c.Y + (s.Kind == SpeakerKind.Car ? 2.6 * scale : Math.Max(14, .4 * scale)) + 2);
        Label(dc, s.Name, at, sel ? Accent : Dim);
        var active = Preset.Macros.Where(m => s.Use(m).On && m.Targets.Any(t => t.Matches(s))).Select(m => m.Name.ToUpperInvariant()).ToList();
        if (active.Count > 0) Label(dc, string.Join(" · ", active), new Point(at.X, at.Y + 13), Neon);
        dc.Pop();
    }

    void DrawListener(DrawingContext dc, Room r)
    {
        var c = S(r.ListenerX, r.ListenerZ);
        bool sel = ReferenceEquals(Selected, r);
        double yaw = r.ListenerYaw * Math.PI / 180, len = Math.Max(22, 1.1 * scale);
        var tip = new Point(c.X + Math.Sin(yaw) * len, c.Y - Math.Cos(yaw) * len);
        var cone = new StreamGeometry();
        using (var g = cone.Open())
        {
            double a1 = yaw - .45, a2 = yaw + .45;
            g.BeginFigure(c, true, true);
            g.LineTo(new Point(c.X + Math.Sin(a1) * len, c.Y - Math.Cos(a1) * len), false, false);
            g.LineTo(new Point(c.X + Math.Sin(a2) * len, c.Y - Math.Cos(a2) * len), false, false);
        }
        cone.Freeze();
        dc.DrawGeometry(CyanSoft, null, cone);
        dc.DrawLine(CyanPen, c, tip);
        dc.DrawEllipse(Cyan, sel ? SelPen : null, c, 8, 8);
        Label(dc, "YOU", new Point(c.X, c.Y + 12), Cyan);
    }

    void Label(DrawingContext dc, string text, Point top, Brush b)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 11, b,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(ft, new Point(top.X - ft.Width / 2, top.Y));
    }

    // ---------- mouse ----------
    object HitAt(Point p)
    {
        if (Preset == null) return null;
        var r = Preset.Room;
        if ((S(r.ListenerX, r.ListenerZ) - p).Length < 14) return r;
        foreach (var s in Enumerable.Reverse(Preset.Speakers))
        {
            var c = S(s.X, s.Z);
            if (s.Kind == SpeakerKind.Car)
            {
                // into the car's own frame
                var (wx, wz) = W(p);
                double a = s.Yaw * Math.PI / 180, dx = wx - s.X, dz = wz - s.Z;
                double lx = dx * Math.Cos(a) - dz * Math.Sin(a), lz = dx * Math.Sin(a) + dz * Math.Cos(a);
                if (Math.Abs(lx) <= 1.0 && Math.Abs(lz) <= 2.4) return s;
            }
            else if ((c - p).Length < Math.Max(16, .5 * scale)) return s;
        }
        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        var hit = HitAt(p);
        if (!ReferenceEquals(hit, Selected)) { Selected = hit; SelectionChanged?.Invoke(hit); }
        if (hit != null)
        {
            dragging = hit; moved = false;
            var (x, z) = Pos(hit);
            grab = S(x, z) - p;
            CaptureMouse();
        }
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = dragging != null ? Cursors.SizeAll : HitAt(e.GetPosition(this)) != null ? Cursors.Hand : Cursors.Arrow;
        if (dragging == null) return;
        var (x, z) = W(e.GetPosition(this) + grab);
        float snap = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .01f : .05f;
        x = MathF.Round(x / snap) * snap; z = MathF.Round(z / snap) * snap;
        if (dragging is Speaker s) { s.X = x; s.Z = z; }
        else if (dragging is Room r) { r.ListenerX = x; r.ListenerZ = z; }
        moved = true;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (dragging == null) return;
        dragging = null;
        ReleaseMouseCapture();
        if (moved) Committed?.Invoke();
    }

    // Right-click a speaker: its own switch for the first macro (Underground), like the toggle on each speaker in the mod.
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (HitAt(e.GetPosition(this)) is not Speaker s || Preset.Macros.Count == 0) return;
        var m = Preset.Macros[0];
        if (!s.Macros.TryGetValue(m.Name, out var u)) s.Macros[m.Name] = u = new SpeakerMacro { Amount = m.Amount };
        bool now = s.Use(m).On;
        u.Own = true; u.On = !now;
        e.Handled = true;
        InvalidateVisual();
        Committed?.Invoke();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var hit = HitAt(e.GetPosition(this)) ?? Selected;
        if (hit == null) return;
        float step = (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 15) * (e.Delta > 0 ? 1 : -1);
        if (hit is Speaker s) s.Yaw = Norm(s.Yaw + step);
        else if (hit is Room r) r.ListenerYaw = Norm(r.ListenerYaw + step);
        e.Handled = true;
        InvalidateVisual();
        Committed?.Invoke();
    }

    static float Norm(float a) => ((a % 360) + 540) % 360 - 180;

    static (float, float) Pos(object o) => o switch
    {
        Speaker s => (s.X, s.Z),
        Room r => (r.ListenerX, r.ListenerZ),
        _ => (0, 0),
    };
}
