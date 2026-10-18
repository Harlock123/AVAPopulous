using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPopulous.Engine;

namespace AVAPopulous.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAPopulous/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private const double BarTop = 660;

    /// <summary>Where each power's button is on the bar along the bottom.</summary>
    public static Rect PowerRect(int k) => new(120 + (k - 1) * 112, BarTop + 14, 104, 86);

    private const uint GoodColour = 0xFF3A7AF0, EvilColour = 0xFFE03A30;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawWorld(dc, g.World, false); DrawTitle(dc); break;
            case GameState.HighScores: DrawWorld(dc, g.World, false); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawWorld(dc, g.World, false); DrawInitials(dc); break;
            case GameState.GameOver: DrawWorld(dc, g.World, false); DrawGameOver(dc); break;
            default:
                DrawWorld(dc, g.World, true);
                DrawTop(dc, g.World);
                DrawPowers(dc, g.World);
                if (g.Message is { } msg)
                {
                    dc.FillRectangle(Brush(0xB0000000), new Rect(0, 300, Field.Width, 76));
                    Text(dc, msg, new Point(512, 338), 54, g.Phase == Phase.Lost ? Red : Yellow, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xD0000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, 92), 22, Yellow, center: true);
    }

    private static uint Blend(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Shade(uint c, double k)
    {
        uint Ch(int shift) => (uint)Math.Clamp(Math.Round(((c >> shift) & 0xFF) * k), 0, 255);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    /// <summary>Each world's land: green and pleasant, then desert, then snow, then rock and lava, and round again.</summary>
    private static (uint Low, uint High, uint Shore, uint Peak, uint Sea) Palette(int level) => ((level - 1) % 4) switch
    {
        0 => (0xFF3E9A3A, 0xFF7ABE4A, 0xFFE0D088, 0xFFF0F0F0, 0xFF2858B8),
        1 => (0xFFD8B060, 0xFFE8CC80, 0xFFF0E0A0, 0xFFB08850, 0xFF2A70A8),
        2 => (0xFFC8D8E8, 0xFFF0F6FF, 0xFFA0B0C0, 0xFFFFFFFF, 0xFF304878),
        _ => (0xFF6A5A50, 0xFF8A7A70, 0xFF4A3A30, 0xFFC04020, 0xFF802010),
    };

    private static Point P(double x, double y, double h)
    {
        var (sx, sy) = Field.Screen(x, y, h);
        return new Point(sx, sy);
    }

    /// <summary>The land, back to front, and on it the towns and the walkers.</summary>
    private void DrawWorld(DrawingContext dc, World w, bool cursor)
    {
        var (low, high, shore, peak, sea) = Palette(w.Level);
        for (int i = 0; i < 24; i++)
            dc.FillRectangle(Brush(Blend(0xFF0A1028, 0xFF26304A, i / 23.0)), new Rect(0, i * 32, Field.Width, 33));
        // The edge of the world: the land's sides, down to the bedrock.
        int n = Field.N;
        Poly(dc, 0xFF3A2A20, null, 0, P(0, n, 0), P(n, n, 0), new Point(Field.OriginX, Field.OriginY + n * Field.TileH + 28), new Point(Field.OriginX - n * Field.TileW / 2, Field.OriginY + n * Field.TileH / 2 + 28));
        Poly(dc, 0xFF2A1C14, null, 0, P(n, 0, 0), P(n, n, 0), new Point(Field.OriginX, Field.OriginY + n * Field.TileH + 28), new Point(Field.OriginX + n * Field.TileW / 2, Field.OriginY + n * Field.TileH / 2 + 28));
        for (int s = 0; s <= 2 * n - 2; s++)
        for (int i = Math.Max(0, s - n + 1); i <= Math.Min(n - 1, s); i++)
        {
            int j = s - i;
            int a = w.H[i, j], b = w.H[i + 1, j], c = w.H[i + 1, j + 1], d = w.H[i, j + 1];
            uint col;
            if (a + b + c + d == 0)
            {
                double ripple = Math.Sin(_clock * 2 + i * 0.9 + j * 0.6) * 0.06;
                col = Shade(sea, 1 + ripple);
            }
            else
            {
                double avg = (a + b + c + d) / 4.0;
                int lo = Math.Min(Math.Min(a, b), Math.Min(c, d));
                col = lo == 0 ? shore : avg >= 7.5 ? peak : avg >= 6 ? Blend(high, 0xFF8A8A8A, (avg - 6) / 1.5) : Blend(low, high, (avg - 1) / 5);
                // The light from the upper left.
                double slope = (a + d) - (b + c) + (a + b) - (c + d);
                col = Shade(col, 1 + slope * 0.07);
                if (w.IsSwamp(i, j)) col = Blend(col, 0xFF3A4A20, 0.75);
                if (w.Rock[i, j]) col = Blend(col, 0xFF5A5450, 0.7);
            }
            Poly(dc, col, Shade(col, 0.85), 1, P(i, j, a), P(i + 1, j, b), P(i + 1, j + 1, c), P(i, j + 1, d));
            if (w.IsSwamp(i, j))
            {
                var m = P(i + 0.5, j + 0.5, a);
                for (int k = 0; k < 3; k++) dc.FillEllipse(m.X - 6 + k * 6, m.Y + (k == 1 ? -2 : 1) + Math.Sin(_clock * 3 + k + i) * 1.2, 1.8, 0xFF7A8A40);
            }
            if (w.Rock[i, j] && (Hash(i, j, 3) & 3) != 0)
            {
                var m = P(i + 0.5, j + 0.5, (a + b + c + d) / 4.0);
                Poly(dc, 0xFF7A7470, 0xFF4A4440, 1, new Point(m.X - 6, m.Y + 2), new Point(m.X - 2, m.Y - 5), new Point(m.X + 5, m.Y - 3), new Point(m.X + 6, m.Y + 2));
            }
        }
        // Towns and walkers, back to front.
        var things = new List<(double Depth, Action Draw)>();
        foreach (var t in w.Towns) { var tt = t; things.Add((t.I + t.J + 1, () => DrawTown(dc, w, tt))); }
        foreach (var k in w.Walkers) { var kk = k; things.Add((k.X + k.Y, () => DrawWalker(dc, w, kk))); }
        foreach (var x in things.OrderBy(t => t.Depth)) x.Draw();
        // The gods' magnets: an ankh standing where each calls its people.
        for (int s = 0; s < 2; s++)
        {
            var g = w.Gods[s];
            if (g.MagnetX >= 0) DrawAnkh(dc, P(g.MagnetX, g.MagnetY, w.H[g.MagnetX, g.MagnetY]), s == World.Good ? GoodColour : EvilColour);
        }
        foreach (var m in w.Marks) DrawMark(dc, w, m);
        if (w.Armageddon && cursor)
        {
            double pulse = 0.12 + 0.06 * Math.Sin(_clock * 6);
            dc.FillRectangle(Brush(Alpha(0xFFFF3010, pulse)), new Rect(0, 0, Field.Width, BarTop));
        }
        if (cursor) DrawCursor(dc, w);
    }

    private void DrawTown(DrawingContext dc, World w, Town t)
    {
        var at = P(t.I + 0.5, t.J + 0.5, w.H[t.I, t.J]);
        double x = at.X, y = at.Y + 4;
        uint roof = t.Side == World.Good ? GoodColour : EvilColour, wall = 0xFFE8D8B8, dark = 0xFF5A4A3A;
        void Box(double cx, double cy, double wdt, double ht, uint wallC, uint roofC, double roofH)
        {
            // A little building seen corner-on: two walls and a roof.
            Poly(dc, Shade(wallC, 0.9), dark, 1, new Point(cx - wdt, cy - wdt / 2), new Point(cx, cy), new Point(cx, cy - ht), new Point(cx - wdt, cy - wdt / 2 - ht));
            Poly(dc, Shade(wallC, 0.7), dark, 1, new Point(cx, cy), new Point(cx + wdt, cy - wdt / 2), new Point(cx + wdt, cy - wdt / 2 - ht), new Point(cx, cy - ht));
            Poly(dc, roofC, Shade(roofC, 0.6), 1, new Point(cx - wdt, cy - wdt / 2 - ht), new Point(cx, cy - ht), new Point(cx + wdt, cy - wdt / 2 - ht), new Point(cx, cy - wdt - ht - roofH));
        }
        switch (t.Level)
        {
            case 1:
                Poly(dc, Shade(roof, 1.1), Shade(roof, 0.5), 1, new Point(x - 8, y - 2), new Point(x, y - 14), new Point(x + 8, y - 2));
                Poly(dc, 0xFF201810, null, 0, new Point(x - 2, y - 2), new Point(x, y - 8), new Point(x + 2, y - 2));
                break;
            case 2: Box(x, y, 7, 6, wall, roof, 4); break;
            case 3: Box(x - 3, y - 1, 7, 8, wall, roof, 5); Box(x + 6, y + 2, 5, 5, wall, roof, 3); break;
            case 4: Box(x, y, 10, 10, wall, roof, 6); Box(x + 9, y + 3, 5, 6, wall, roof, 3); break;
            case 5: Box(x - 6, y, 8, 14, 0xFFC8B898, roof, 8); Box(x + 6, y + 1, 9, 9, wall, roof, 5); break;
            default:
            {
                // A castle: a keep and four towers.
                uint stone = 0xFFB0A898;
                Box(x - 12, y - 1, 5, 18, stone, roof, 7);
                Box(x + 12, y - 1, 5, 18, stone, roof, 7);
                Box(x, y - 2, 11, 16, stone, roof, 6);
                Box(x, y - 22, 5, 14, stone, roof, 8);
                Box(x - 1, y + 7, 5, 15, stone, roof, 6);
                break;
            }
        }
        // The flag, and how many live there.
        double fy = y - 18 - t.Level * 5;
        dc.DrawLine(Pen(0xFF2A2018, 1.5), new Point(x + 2, fy), new Point(x + 2, fy + 14));
        Poly(dc, roof, null, 0, new Point(x + 2, fy), new Point(x + 11, fy + 3), new Point(x + 2, fy + 6));
    }

    private void DrawWalker(DrawingContext dc, World w, Walker k)
    {
        var at = P(k.X, k.Y, w.Height(k.X, k.Y));
        double x = at.X, y = at.Y;
        uint c = k.Side == World.Good ? GoodColour : EvilColour;
        double step = Math.Sin(_clock * 10 + k.X * 3 + k.Y) * 2;
        double size = k.Knight ? 1.5 : 1 + Math.Min(0.4, k.Strength / 250);
        dc.DrawLine(Pen(0xFF2A2018, 1.6 * size), new Point(x, y - 5 * size), new Point(x - 2 * size + step * 0.5, y));
        dc.DrawLine(Pen(0xFF2A2018, 1.6 * size), new Point(x, y - 5 * size), new Point(x + 2 * size - step * 0.5, y));
        dc.DrawLine(Pen(c, 3.2 * size), new Point(x, y - 5 * size), new Point(x, y - 10 * size));
        dc.FillEllipse(x, y - 12.5 * size, 2.2 * size, 0xFFF0C8A0);
        if (k.Knight)
        {
            dc.FillEllipse(x, y - 13.5 * size, 2.4 * size, 0xFFB8B8C8);
            dc.DrawLine(Pen(0xFFE0E0F0, 1.6), new Point(x + 3, y - 6 * size), new Point(x + 6, y - 18 * size));
            dc.DrawLine(Pen(0xFF8A6A30, 2), new Point(x + 1, y - 9 * size), new Point(x + 6, y - 10 * size));
        }
    }

    private static void DrawAnkh(DrawingContext dc, Point at, uint c)
    {
        double x = at.X, y = at.Y;
        dc.DrawLine(Pen(0xFF201810, 5), new Point(x, y), new Point(x, y - 26));
        dc.DrawLine(Pen(c, 3), new Point(x, y), new Point(x, y - 26));
        dc.DrawLine(Pen(c, 3), new Point(x - 7, y - 20), new Point(x + 7, y - 20));
        dc.DrawEllipse(null, Pen(c, 3), new Point(x, y - 31), 4.5, 6);
    }

    private void DrawMark(DrawingContext dc, World w, (double X, double Y, Power What, double T) m)
    {
        int hx = Math.Clamp((int)m.X, 0, Field.N), hy = Math.Clamp((int)m.Y, 0, Field.N);
        var at = P(m.X, m.Y, w.H[hx, hy]);
        double k = m.T / 2, a = 1 - k;
        switch (m.What)
        {
            case Power.None:
                // A fight: a burst of dust.
                for (int i = 0; i < 6; i++)
                {
                    double ang = i * Math.PI / 3 + m.T * 4, r = 4 + (m.T - 1.4) * 30;
                    dc.FillEllipse(at.X + Math.Cos(ang) * r, at.Y - 6 + Math.Sin(ang) * r * 0.5, 2.5, Alpha(0xFFF0E0C0, (2 - m.T) * 1.6));
                }
                break;
            case Power.Earthquake:
                for (int i = 0; i < 3; i++)
                {
                    double r = (k * 3 + i * 0.4) % 1.2 * 90;
                    dc.DrawEllipse(null, Pen(Alpha(0xFFE0C090, a), 3), at, r, r / 2);
                }
                break;
            case Power.Volcano:
                for (int i = 0; i < 10; i++)
                {
                    double ang = -Math.PI / 2 + (Hash(i, 1, 7) % 100 - 50) / 60.0, r = m.T * (60 + Hash(i, 2, 7) % 60);
                    dc.FillEllipse(at.X + Math.Cos(ang) * r, at.Y - 40 + Math.Sin(ang) * r + m.T * m.T * 40, 4, Alpha(i % 2 == 0 ? 0xFFFF6020 : 0xFFFFC040, a));
                }
                dc.FillEllipse(at.X, at.Y - 50 - m.T * 30, 20 + m.T * 20, Alpha(0xFF404040, a * 0.6));
                break;
            case Power.Flood:
                dc.FillRectangle(Brush(Alpha(0xFF3070E0, a * 0.35)), new Rect(0, 0, Field.Width, BarTop));
                break;
            case Power.Armageddon:
                dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, a * a * 0.5)), new Rect(0, 0, Field.Width, BarTop));
                break;
            case Power.Swamp:
            case Power.Knight:
            case Power.Magnet:
                dc.DrawEllipse(null, Pen(Alpha(0xFFFFF0A0, a), 3), at, 10 + k * 50, (10 + k * 50) / 2);
                break;
        }
    }

    private void DrawCursor(DrawingContext dc, World w)
    {
        var g = Engine;
        int cx = g.CursorX, cy = g.CursorY;
        bool reach = Sim.InReach(w, World.Good, cx, cy);
        var armed = Input.Armed;
        uint c = armed != Power.None ? Yellow : reach ? 0xFFFFFFFF : 0xFFFF6050;
        var at = P(cx, cy, w.H[cx, cy]);
        double pulse = 1 + 0.15 * Math.Sin(_clock * 8);
        Poly(dc, 0, c, 2, new Point(at.X - 14 * pulse, at.Y), new Point(at.X, at.Y - 7 * pulse), new Point(at.X + 14 * pulse, at.Y), new Point(at.X, at.Y + 7 * pulse));
        dc.DrawLine(Pen(c, 2), new Point(at.X, at.Y), new Point(at.X, at.Y - 22));
        dc.FillEllipse(at.X, at.Y - 24, 3, c);
        if (armed != Power.None) Text(dc, PowerName(armed), new Point(at.X, at.Y - 40), 20, Yellow, center: true);
    }

    private static string PowerName(Power p) => p switch
    {
        Power.Magnet => "MAGNET", Power.Earthquake => "EARTHQUAKE", Power.Swamp => "SWAMP", Power.Knight => "KNIGHT",
        Power.Volcano => "VOLCANO", Power.Flood => "FLOOD", Power.Armageddon => "ARMAGEDDON", _ => "",
    };

    /// <summary>The top: the world, your mana, the two peoples' numbers side by side, and the score.</summary>
    private void DrawTop(DrawingContext dc, World w)
    {
        dc.FillRectangle(Brush(0xE0101420), new Rect(0, 0, Field.Width, 70));
        dc.DrawLine(Pen(0xFF8A7A50, 2), new Point(0, 70), new Point(Field.Width, 70));
        Text(dc, $"WORLD {w.Level}", new Point(20, 20), 28, Yellow);
        Text(dc, GameEngine.WorldName(w.Level), new Point(20, 48), 24, 0xFFE0D0A0);
        // Mana.
        double mana = w.Gods[World.Good].Mana;
        Text(dc, "MANA", new Point(220, 20), 22, Cyan);
        dc.FillRectangle(Brush(0xFF202838), new Rect(280, 12, 250, 16), 4);
        dc.FillRectangle(Brush(0xFF50C8F0), new Rect(280, 12, 250 * Math.Min(1, mana / Sim.MaxMana), 16), 4);
        for (int k = 1; k <= 7; k++)
        {
            double x = 280 + 250 * Sim.Cost((Power)k) / Sim.MaxMana;
            dc.DrawLine(Pen(0xFF101420, 1.5), new Point(x, 12), new Point(x, 28));
        }
        Text(dc, $"{mana:0}", new Point(540, 20), 22, White);
        // The peoples.
        double good = w.Population(World.Good), evil = w.Population(World.Evil), most = Math.Max(1, Math.Max(good, evil));
        Text(dc, "PEOPLE", new Point(206, 50), 22, 0xFFE0D0A0);
        dc.FillRectangle(Brush(GoodColour), new Rect(280, 40, 250 * good / most, 9), 3);
        dc.FillRectangle(Brush(EvilColour), new Rect(280, 52, 250 * evil / most, 9), 3);
        Text(dc, $"{good:0} / {evil:0}", new Point(540, 50), 22, White);
        Text(dc, $"TOWNS {w.Towns.Count(t => t.Side == World.Good)} / {w.Towns.Count(t => t.Side == World.Evil)}", new Point(700, 20), 22, White);
        Text(dc, $"SCORE {Engine.Score}", new Point(1004, 20), 28, Yellow, right: true);
        if (w.Armageddon && (int)(_clock * 3) % 2 == 0) Text(dc, "ARMAGEDDON", new Point(1004, 50), 26, Red, right: true);
    }

    /// <summary>The bottom: the seven powers, each with its key and its price; dim until there's mana for it.</summary>
    private void DrawPowers(DrawingContext dc, World w)
    {
        dc.FillRectangle(Brush(0xE0101420), new Rect(0, BarTop, Field.Width, Field.Height - BarTop));
        dc.DrawLine(Pen(0xFF8A7A50, 2), new Point(0, BarTop), new Point(Field.Width, BarTop));
        Text(dc, "Z RAISE", new Point(60, BarTop + 34), 20, 0xFFE0D0A0, center: true);
        Text(dc, "X LOWER", new Point(60, BarTop + 62), 20, 0xFFE0D0A0, center: true);
        double mana = w.Gods[World.Good].Mana;
        for (int k = 1; k <= 7; k++)
        {
            var p = (Power)k;
            var r = PowerRect(k);
            bool can = mana >= Sim.Cost(p) && !w.Armageddon, armed = Input.Armed == p;
            dc.FillRectangle(Brush(armed ? 0xFF4A4020 : can ? 0xFF283048 : 0xFF181C28), r, 8);
            dc.DrawRectangle(null, Pen(armed ? Yellow : can ? 0xFF8A9AC8 : 0xFF3A4058, 2), r, 8, 8);
            uint ic = can ? 0xFFF0E0B0 : 0xFF5A6078;
            DrawIcon(dc, p, new Point(r.X + r.Width / 2, r.Y + 34), ic);
            Text(dc, $"{k}", new Point(r.X + 10, r.Y + 12), 20, can ? Yellow : Dim);
            Text(dc, PowerName(p), new Point(r.X + r.Width / 2, r.Y + 62), 17, ic, center: true);
            Text(dc, $"{Sim.Cost(p)}", new Point(r.Right - 8, r.Y + 12), 17, can ? Cyan : Dim, right: true);
        }
    }

    private void DrawIcon(DrawingContext dc, Power p, Point c, uint col)
    {
        double x = c.X, y = c.Y;
        var pen = Pen(col, 2.5);
        switch (p)
        {
            case Power.Magnet: DrawAnkh(dc, new Point(x, y + 18), col); break;
            case Power.Earthquake:
                dc.DrawLine(pen, new Point(x - 22, y + 6), new Point(x + 22, y + 6));
                dc.DrawLine(pen, new Point(x - 4, y - 14), new Point(x + 4, y - 4));
                dc.DrawLine(pen, new Point(x + 4, y - 4), new Point(x - 4, y + 6));
                dc.DrawLine(pen, new Point(x - 4, y + 6), new Point(x + 6, y + 16));
                break;
            case Power.Swamp:
                dc.DrawEllipse(null, pen, new Point(x, y + 8), 20, 7);
                for (int i = 0; i < 3; i++) dc.DrawEllipse(null, Pen(col, 1.8), new Point(x - 8 + i * 8, y - 2 - (i % 2) * 6), 3, 3);
                break;
            case Power.Knight:
                dc.DrawLine(Pen(col, 3), new Point(x, y + 16), new Point(x, y - 16));
                dc.DrawLine(Pen(col, 3), new Point(x - 9, y + 6), new Point(x + 9, y + 6));
                break;
            case Power.Volcano:
                Poly(dc, 0, col, 2.5, new Point(x - 22, y + 14), new Point(x - 6, y - 8), new Point(x + 6, y - 8), new Point(x + 22, y + 14));
                for (int i = 0; i < 3; i++) dc.FillEllipse(x - 6 + i * 6, y - 14 - (i % 2) * 5, 2.5, col == 0xFF5A6078 ? col : 0xFFFF7030);
                break;
            case Power.Flood:
                for (int i = 0; i < 3; i++)
                {
                    double yy = y - 8 + i * 9;
                    dc.DrawLine(pen, new Point(x - 20, yy), new Point(x - 8, yy - 4));
                    dc.DrawLine(pen, new Point(x - 8, yy - 4), new Point(x + 4, yy));
                    dc.DrawLine(pen, new Point(x + 4, yy), new Point(x + 16, yy - 4));
                }
                break;
            case Power.Armageddon:
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    dc.DrawLine(pen, new Point(x + Math.Cos(a) * 7, y + Math.Sin(a) * 7), new Point(x + Math.Cos(a) * 18, y + Math.Sin(a) * 18));
                }
                dc.FillEllipse(x, y, 5, col);
                break;
        }
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 420, 40, 840, 300), 12);
        string title = "AVA POPULOUS";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 30 + 30 + i * 60, ty = 96 + Math.Sin(_clock * 2.4 + i * 0.45) * 5;
            Text(dc, title[i].ToString(), new Point(tx, ty), 108, i < 3 ? 0xFFF0E0B0 : 0xFF6AB0FF, center: true);
        }
        Text(dc, "AFTER BULLFROG'S 1989 GOD GAME", new Point(cx, 162), 26, White, center: true);
        Text(dc, "YOU ARE A GOD, AND THE PEOPLE IN BLUE ARE YOURS. RAISE AND LOWER THE LAND", new Point(cx, 204), 23, White, center: true);
        Text(dc, "TO MAKE IT FLAT, AND THEY SETTLE AND GROW, FROM TENTS TO CASTLES; THE MORE", new Point(cx, 232), 23, White, center: true);
        Text(dc, "PEOPLE, THE MORE MANA FOR YOUR POWERS. THE EVIL GOD DOES THE SAME FOR THE RED.", new Point(cx, 260), 23, White, center: true);
        Text(dc, "WIPE HIS PEOPLE FROM THE WORLD, AND ON TO THE NEXT.", new Point(cx, 288), 23, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 620), 44, Yellow, center: true);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 692, Field.Width, 76));
        Text(dc, "ARROWS OR MOUSE: CURSOR   Z / LEFT: RAISE   X / RIGHT: LOWER   1-7: POWERS   P: PAUSE", new Point(cx, 712), 19, 0xFFE0D0B0, center: true);
        Text(dc, "MUSIC: GREENSLEEVES (TRADITIONAL) AND A TUNE OF ITS OWN", new Point(cx, 744), 18, Dim, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xC0000000), new Rect(cx - 380, 200, 760, 250), 12);
        Text(dc, "THE END", new Point(cx, 270), 74, (int)(_clock * 4) % 2 == 0 ? Yellow : White, center: true);
        Text(dc, $"SCORE {g.Score}", new Point(cx, 350), 44, White, center: true);
        Text(dc, $"YOUR PEOPLE FELL ON WORLD {g.Level}, {GameEngine.WorldName(g.Level)}", new Point(cx, 404), 28, Cyan, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 330, 70, 660, 640), 12);
        Text(dc, "THE GREATEST GODS", new Point(cx, 120), 54, Yellow, center: true);
        Text(dc, "RANK NAME    SCORE  WORLD", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xD0000000), new Rect(cx - 330, 140, 660, 460), 12);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
