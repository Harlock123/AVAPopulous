namespace AVAPopulous.Engine;

/// <summary>The rules, one 60th of a second at a time.</summary>
public static class Sim
{

    public const double LandCost = 1, MaxMana = 1500, ManaRate = 0.02;
    public const double WalkSpeed = 1.3, KnightSpeed = 1.9, Hunger = 0.12;
    /// <summary>How far from its own towns a god can shape the land.</summary>
    public const int Reach = 4;
    public const double MagnetSeconds = 25, SwampSeconds = 40;
    /// <summary>How many a town holds at each size: a tent, a hut, a cottage, a house, a manor, a castle.</summary>
    public static readonly double[] Capacity = [0, 12, 28, 50, 80, 120, 180];
    public static readonly (int X, int Y) GoodHome = (7, 24), EvilHome = (24, 7), Middle = (16, 16);

    public static int Cost(Power p) => p switch
    {
        Power.Magnet => 20, Power.Earthquake => 150, Power.Swamp => 200, Power.Knight => 350, Power.Volcano => 500,
        Power.Flood => 700, Power.Armageddon => 1000, _ => 0,
    };

    /// <summary>The evil god's powers come to it world by world (all but Armageddon); the good god has them all from the first.</summary>
    public static bool Allowed(World w, int side, Power p) => side == World.Good || p switch
    {
        Power.Magnet => true, Power.Earthquake => w.Level >= 2, Power.Swamp => w.Level >= 3, Power.Knight => w.Level >= 4,
        Power.Volcano => w.Level >= 6, Power.Flood => w.Level >= 8, Power.Armageddon => true, _ => false,
    };

    /// <summary>How long the evil god waits between deeds: slow on the first world, quicker on every one after.</summary>
    public static double EvilPace(int level) => Math.Max(0.16, 0.62 - level * 0.045);
    public const double GoodPace = 0.09;
    public static double EvilMana(int level) => 0.75 + level * 0.07;

    // ───────────────────────────── a new world ─────────────────────────────

    public static void Start(World w, int level)
    {
        w.Level = level;
        w.Towns.Clear();
        w.Walkers.Clear();
        w.Gods = [new God { Mana = 120 }, new God { Mana = 120 }];
        w.Swamp = new double[Field.N, Field.N];
        w.Rock = new bool[Field.N, Field.N];
        w.Armageddon = false;
        w.Winner = -1;
        w.Clock = 0;
        w.Dice = new Dice(level * 7919 + 13);
        MakeLand(w, level);
        Home(w, World.Good, GoodHome);
        Home(w, World.Evil, EvilHome);
        Census(w);
    }

    /// <summary>An island: hills along the way from one people's home to the other's, and more about at random; the sea at the rim.</summary>
    private static void MakeLand(World w, int level)
    {
        int n = Field.N;
        var f = new double[n + 1, n + 1];
        var d = w.Dice;
        var bumps = new List<(double X, double Y, double R, double H)>();
        for (int k = 0; k <= 5; k++)
        {
            double t = k / 5.0;
            bumps.Add((GoodHome.X + (EvilHome.X - GoodHome.X) * t + d.Next() * 4 - 2, GoodHome.Y + (EvilHome.Y - GoodHome.Y) * t + d.Next() * 4 - 2, 5 + d.Next() * 3, 2.5 + d.Next() * 2));
        }
        int extra = 8 + Math.Min(10, level);
        for (int k = 0; k < extra; k++)
        {
            bool lake = d.Next() < 0.12 + Math.Min(0.2, level * 0.02);
            bumps.Add((3 + d.Next() * (n - 6), 3 + d.Next() * (n - 6), 2.5 + d.Next() * 5, lake ? -2 - d.Next() * 2 : 1.5 + d.Next() * (3 + Math.Min(3, level * 0.3))));
        }
        w.Dice = d;
        for (int x = 0; x <= n; x++)
        for (int y = 0; y <= n; y++)
        {
            double v = 0;
            foreach (var b in bumps)
            {
                double q = ((x - b.X) * (x - b.X) + (y - b.Y) * (y - b.Y)) / (b.R * b.R);
                if (q < 1) v += b.H * (1 - q);
            }
            w.H[x, y] = x == 0 || y == 0 || x == n || y == n ? 0 : Math.Clamp((int)Math.Round(v), 0, Field.MaxHeight);
        }
        Smooth(w);
    }

    /// <summary>No corner more than a step above its neighbour: the peaks come down until it's so.</summary>
    private static void Smooth(World w)
    {
        int n = Field.N;
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int x = 0; x <= n; x++)
            for (int y = 0; y <= n; y++)
                foreach (var (u, v) in Around(x, y))
                    if (w.H[x, y] > w.H[u, v] + 1) { w.H[x, y] = w.H[u, v] + 1; changed = true; }
        }
    }

    private static IEnumerable<(int, int)> Around(int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
            if ((dx != 0 || dy != 0) && Field.InLand(x + dx, y + dy)) yield return (x + dx, y + dy);
    }

    /// <summary>A people's home: a little flat land, a tent on it, and two walkers.</summary>
    private static void Home(World w, int side, (int X, int Y) at)
    {
        int h = Math.Max(1, Math.Min(4, w.H[at.X, at.Y]));
        var seeds = new List<(int, int)>();
        for (int x = at.X - 1; x <= at.X + 2; x++)
        for (int y = at.Y - 1; y <= at.Y + 2; y++)
        {
            w.H[x, y] = h;
            seeds.Add((x, y));
        }
        Spread(w, seeds, null);
        w.Towns.Add(new Town { I = at.X, J = at.Y, Side = side, Pop = 8 });
        for (int k = 0; k < 2; k++)
            w.Walkers.Add(new Walker { X = at.X + 0.5 + (k == 0 ? -1 : 1), Y = at.Y + 0.5 + (k == 0 ? 1 : -1), Strength = 6, Side = side, Dir = w.Dice.Next() * Math.Tau });
    }

    // ───────────────────────────── the land ─────────────────────────────

    /// <summary>From corners just set, bring every neighbour to within a step, and on outwards.</summary>
    private static void Spread(World w, List<(int X, int Y)> seeds, HashSet<(int, int)>? changed)
    {
        var q = new Queue<(int, int)>(seeds);
        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            int h = w.H[x, y];
            foreach (var (u, v) in Around(x, y))
            {
                int was = w.H[u, v];
                if (was < h - 1) w.H[u, v] = h - 1;
                else if (was > h + 1) w.H[u, v] = h + 1;
                else continue;
                changed?.Add((u, v));
                q.Enqueue((u, v));
            }
        }
    }

    /// <summary>Whether a god can shape the land at a corner: near enough its own towns, or right where one of its walkers stands.</summary>
    public static bool InReach(World w, int side, int x, int y)
    {
        if (!Field.InLand(x, y) || x == 0 || y == 0 || x == Field.N || y == Field.N) return false;
        foreach (var t in w.Towns)
            if (t.Side == side && Math.Max(Math.Abs(t.I + 0.5 - x), Math.Abs(t.J + 0.5 - y)) <= Reach + 0.5) return true;
        foreach (var k in w.Walkers)
            if (k.Side == side && Math.Max(Math.Abs(k.X - x), Math.Abs(k.Y - y)) <= 1.5) return true;
        return false;
    }

    /// <summary>Raises (or lowers) a corner a step and the land round it with it. Returns the corners that moved.</summary>
    public static HashSet<(int, int)> Shape(World w, int x, int y, int by)
    {
        var changed = new HashSet<(int, int)>();
        int h = w.H[x, y] + by;
        if (h < 0 || h > Field.MaxHeight) return changed;
        w.H[x, y] = h;
        changed.Add((x, y));
        Spread(w, [(x, y)], changed);
        return changed;
    }

    /// <summary>After the land moves: towns no longer on the flat fall (or drown), and swamps and rocks it moved go.</summary>
    private static void Settled(World w, HashSet<(int X, int Y)> changed)
    {
        foreach (var (x, y) in changed)
            for (int i = x - 1; i <= x; i++)
            for (int j = y - 1; j <= y; j++)
                if (Field.InTiles(i, j)) { w.Swamp[i, j] = 0; w.Rock[i, j] = false; }
        for (int k = w.Towns.Count - 1; k >= 0; k--)
        {
            var t = w.Towns[k];
            if (w.Flat(t.I, t.J) && !w.Water(t.I, t.J)) continue;
            w.Towns.RemoveAt(k);
            if (w.Water(t.I, t.J)) { if (t.Side == World.Good) w.Heard.Add(Sfx.Drown); continue; }
            // Half are lost in the ruins; the rest go to find land of their own.
            w.Walkers.Add(new Walker { X = t.I + 0.5, Y = t.J + 0.5, Strength = t.Pop / 2, Side = t.Side, Dir = w.Dice.Next() * Math.Tau });
            if (t.Side == World.Good) w.Heard.Add(Sfx.Burn);
        }
        foreach (var k in w.Walkers)
            if (!w.Armageddon && w.Water((int)k.X, (int)k.Y)) k.Strength = 0;
    }

    // ───────────────────────────── the gods' deeds ─────────────────────────────

    /// <summary>A god's deed, if it can: land shaped near its people, for a little mana; or a power, for more.</summary>
    public static bool Apply(World w, int side, Deed d)
    {
        var g = w.Gods[side];
        if (g.Busy > 0 || w.Winner >= 0) return false;
        if (d.Cast != Power.None) return Cast(w, side, d.Cast, d.X, d.Y);
        if (!d.Raise && !d.Lower) return false;
        if (g.Mana < LandCost || !InReach(w, side, d.X, d.Y)) return false;
        var changed = Shape(w, d.X, d.Y, d.Raise ? 1 : -1);
        if (changed.Count == 0) return false;
        g.Mana -= LandCost;
        g.Busy = side == World.Good ? GoodPace : EvilPace(w.Level);
        Settled(w, changed);
        if (side == World.Good) w.Heard.Add(d.Raise ? Sfx.Raise : Sfx.Lower);
        return true;
    }

    public static bool Cast(World w, int side, Power p, int x, int y)
    {
        var g = w.Gods[side];
        if (!Allowed(w, side, p) || g.Mana < Cost(p) || w.Armageddon) return false;
        x = Math.Clamp(x, 1, Field.N - 1);
        y = Math.Clamp(y, 1, Field.N - 1);
        switch (p)
        {
            case Power.Magnet:
                g.MagnetX = x;
                g.MagnetY = y;
                g.MagnetTime = MagnetSeconds;
                foreach (var k in w.Walkers) if (k.Side == side) k.Look = 0;
                break;
            case Power.Earthquake:
            {
                var changed = new HashSet<(int, int)>();
                var d = w.Dice;
                for (int u = x - 3; u <= x + 3; u++)
                for (int v = y - 3; v <= y + 3; v++)
                {
                    if (!Field.InLand(u, v) || u == 0 || v == 0 || u == Field.N || v == Field.N || (u - x) * (u - x) + (v - y) * (v - y) > 10) continue;
                    int by = d.Next(3) - 1;
                    if (by != 0 && w.H[u, v] + by is >= 0 and <= Field.MaxHeight) changed.UnionWith(Shape(w, u, v, by));
                }
                w.Dice = d;
                Settled(w, changed);
                break;
            }
            case Power.Swamp:
            {
                int made = 0;
                for (int r = 0; r <= 3 && made < 7; r++)
                for (int i = x - r - 1; i <= x + r; i++)
                for (int j = y - r - 1; j <= y + r; j++)
                    if (made < 7 && w.Buildable(i, j) && w.TownAt(i, j) == null) { w.Swamp[i, j] = SwampSeconds; made++; }
                if (made == 0) return false;
                break;
            }
            case Power.Knight:
            {
                if (w.Walkers.Any(k => k.Side == side && k.Knight)) return false;
                var best = w.Walkers.Where(k => k.Side == side && !k.Knight).MaxBy(k => k.Strength);
                if (best == null || best.Strength < 10)
                {
                    var town = w.Towns.Where(t => t.Side == side).MaxBy(t => t.Pop);
                    if (town == null || town.Pop < 10) return false;
                    best = new Walker { X = town.I + 0.5, Y = town.J + 0.5, Side = side, Strength = town.Pop / 2 };
                    town.Pop /= 2;
                    w.Walkers.Add(best);
                }
                best.Knight = true;
                best.Strength *= 1.5;
                best.Ti = best.Tj = -1;
                x = (int)best.X;
                y = (int)best.Y;
                break;
            }
            case Power.Volcano:
            {
                var changed = new HashSet<(int, int)>();
                int top = Math.Min(Field.MaxHeight, w.H[x, y] + 4);
                for (int u = x - 3; u <= x + 3; u++)
                for (int v = y - 3; v <= y + 3; v++)
                {
                    if (!Field.InLand(u, v) || u == 0 || v == 0 || u == Field.N || v == Field.N) continue;
                    int want = top - Math.Max(Math.Abs(u - x), Math.Abs(v - y));
                    while (w.H[u, v] < want) changed.UnionWith(Shape(w, u, v, 1));
                }
                // The towns under it burn.
                foreach (var t in w.Towns.Where(t => Math.Max(Math.Abs(t.I + 0.5 - x), Math.Abs(t.J + 0.5 - y)) <= 3.5).ToList())
                {
                    w.Towns.Remove(t);
                    if (t.Side != side && side == World.Good) w.Score += 50;
                }
                Settled(w, changed);
                for (int i = x - 3; i < x + 3; i++)
                for (int j = y - 3; j < y + 3; j++)
                    if (Field.InTiles(i, j) && !w.Water(i, j)) w.Rock[i, j] = (Hash(i, j) & 1) == 0 || w.Flat(i, j);
                break;
            }
            case Power.Flood:
            {
                for (int u = 0; u <= Field.N; u++)
                for (int v = 0; v <= Field.N; v++)
                    if (w.H[u, v] > 0) w.H[u, v]--;
                var none = new HashSet<(int, int)>();
                Settled(w, none);
                break;
            }
            case Power.Armageddon:
                foreach (var t in w.Towns)
                    w.Walkers.Add(new Walker { X = t.I + 0.5, Y = t.J + 0.5, Strength = t.Pop, Side = t.Side });
                w.Towns.Clear();
                w.Armageddon = true;
                x = Middle.X;
                y = Middle.Y;
                break;
        }
        g.Mana -= Cost(p);
        g.Busy = 0.5;
        w.Marks.Add((x, y, p, 0));
        w.Heard.Add(p switch
        {
            Power.Magnet => Sfx.Magnet, Power.Earthquake => Sfx.Quake, Power.Swamp => Sfx.SwampSound, Power.Knight => Sfx.KnightSound,
            Power.Volcano => Sfx.VolcanoSound, Power.Flood => Sfx.FloodSound, _ => Sfx.ArmageddonSound,
        });
        return true;
    }

    private static uint Hash(int i, int j)
    {
        uint h = (uint)(i * 73856093) ^ (uint)(j * 19349663);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        return h ^ (h >> 15);
    }

    // ───────────────────────────── the world going on ─────────────────────────────

    public static void Step(World w)
    {
        double dt = Field.Step;
        if (w.Winner >= 0) return;
        w.Clock += dt;
        for (int s = 0; s < 2; s++)
        {
            var g = w.Gods[s];
            g.Busy = Math.Max(0, g.Busy - dt);
            g.Mana = Math.Min(MaxMana, g.Mana + w.Population(s) * ManaRate * (s == World.Evil ? EvilMana(w.Level) : 1) * dt);
            if (g.MagnetTime > 0 && (g.MagnetTime -= dt) <= 0) g.MagnetX = g.MagnetY = -1;
        }
        // The evil god thinks, and acts.
        if (w.Gods[World.Evil].Busy <= 0 && Mind.Think(w, World.Evil) is { } deed && !Apply(w, World.Evil, deed))
            w.Gods[World.Evil].Busy = EvilPace(w.Level);
        for (int i = 0; i < Field.N; i++)
        for (int j = 0; j < Field.N; j++)
            if (w.Swamp[i, j] > 0) w.Swamp[i, j] = Math.Max(0, w.Swamp[i, j] - dt);
        w.Census += dt;
        if (w.Census >= 0.5) { w.Census = 0; Census(w); }
        Grow(w, dt);
        Walk(w, dt);
        Meet(w);
        w.Walkers.RemoveAll(k => k.Strength < 0.5);
        for (int i = w.Marks.Count - 1; i >= 0; i--)
        {
            var m = w.Marks[i];
            m.T += dt;
            if (m.T > 2) w.Marks.RemoveAt(i);
            else w.Marks[i] = m;
        }
        for (int s = 0; s < 2; s++)
            if (w.Population(s) < 0.5)
            {
                w.Winner = 1 - s;
                w.Heard.Add(s == World.Evil ? Sfx.Win : Sfx.Lose);
                break;
            }
    }

    /// <summary>The flat land of a town's height round it, up to two tiles off, that's free: how big it can grow.</summary>
    public static int FlatRound(World w, int i, int j, int h, Town? self = null)
    {
        int n = 0;
        for (int a = i - 2; a <= i + 2; a++)
        for (int b = j - 2; b <= j + 2; b++)
        {
            if (a == i && b == j || !w.Buildable(a, b) || w.H[a, b] != h) continue;
            var t = w.TownAt(a, b);
            if (t == null || t == self) n++;
        }
        return n;
    }

    public static int LevelFor(int flat) => flat switch { <= 2 => 1, <= 6 => 2, <= 11 => 3, <= 16 => 4, <= 21 => 5, _ => 6 };

    public static void Census(World w)
    {
        foreach (var t in w.Towns)
        {
            int was = t.Level;
            t.Level = LevelFor(FlatRound(w, t.I, t.J, w.H[t.I, t.J], t));
            if (t.Level > was && t.Side == World.Good) w.Heard.Add(Sfx.Grow);
        }
    }

    /// <summary>Towns grow; a full one sends half its people out to find land of their own.</summary>
    private static void Grow(World w, double dt)
    {
        foreach (var t in w.Towns.ToList())
        {
            double cap = Capacity[t.Level];
            t.Pop += (0.25 + 0.3 * t.Level) * dt;
            if (t.Pop < cap) continue;
            double out_ = t.Level == 1 ? t.Pop - 4 : t.Pop / 2;
            t.Pop -= out_;
            var d = w.Dice;
            w.Walkers.Add(new Walker { X = t.I + 0.5, Y = t.J + 0.5, Strength = out_, Side = t.Side, Dir = d.Next() * Math.Tau, Look = 0.3 });
            w.Dice = d;
        }
    }

    /// <summary>Where a walker could settle: free flat land with no town within two tiles, the more flat land round it the better.</summary>
    public static (int I, int J)? Find(World w, Walker k, int radius = 6)
    {
        int ci = (int)k.X, cj = (int)k.Y;
        double best = double.NegativeInfinity;
        (int, int)? pick = null;
        for (int i = ci - radius; i <= ci + radius; i++)
        for (int j = cj - radius; j <= cj + radius; j++)
        {
            if (!w.Buildable(i, j) || Crowded(w, i, j)) continue;
            double dist = Math.Max(Math.Abs(i + 0.5 - k.X), Math.Abs(j + 0.5 - k.Y));
            double v = FlatRound(w, i, j, w.H[i, j]) - dist * 1.2;
            if (v > best) { best = v; pick = (i, j); }
        }
        return pick;
    }

    public static bool Crowded(World w, int i, int j) => w.Towns.Any(t => Math.Abs(t.I - i) <= 2 && Math.Abs(t.J - j) <= 2);

    private static void Walk(World w, double dt)
    {
        foreach (var k in w.Walkers.ToList())
        {
            if (k.Strength <= 0) continue;
            if (!w.Armageddon) k.Strength -= Hunger * (k.Knight ? 3 : 1) * dt;
            k.Look -= dt;
            var g = w.Gods[k.Side];
            double tx, ty;
            bool go;
            if (w.Armageddon) { tx = Middle.X; ty = Middle.Y; go = true; }
            else if (k.Knight)
            {
                // A knight goes for the nearest of the other side's towns, or walkers.
                var foe = w.Towns.Where(t => t.Side != k.Side).MinBy(t => Dist(k, t.I + 0.5, t.J + 0.5));
                var other = w.Walkers.Where(o => o.Side != k.Side && o.Strength > 0).MinBy(o => Dist(k, o.X, o.Y));
                if (foe != null && (other == null || Dist(k, foe.I + 0.5, foe.J + 0.5) < Dist(k, other.X, other.Y) + 3)) { tx = foe.I + 0.5; ty = foe.J + 0.5; go = true; }
                else if (other != null) { tx = other.X; ty = other.Y; go = true; }
                else { tx = ty = 0; go = false; }
            }
            else if (g.MagnetX >= 0 && Dist(k, g.MagnetX, g.MagnetY) > 1.5) { tx = g.MagnetX; ty = g.MagnetY; go = true; }
            else
            {
                if (k.Ti >= 0 && (!w.Buildable(k.Ti, k.Tj) || Crowded(w, k.Ti, k.Tj))) k.Ti = k.Tj = -1;
                if (k.Look <= 0)
                {
                    k.Look = 0.6;
                    if (Find(w, k) is { } at) { k.Ti = at.I; k.Tj = at.J; }
                    else k.Ti = k.Tj = -1;
                }
                if (k.Ti >= 0) { tx = k.Ti + 0.5; ty = k.Tj + 0.5; go = true; }
                else if (k.Strength >= 15 && w.Towns.Where(t => t.Side != k.Side).MinBy(t => Dist(k, t.I + 0.5, t.J + 0.5)) is { } foe && Dist(k, foe.I + 0.5, foe.J + 0.5) < 9)
                {
                    // Nowhere to settle, and strong: at the other side's nearest town.
                    tx = foe.I + 0.5;
                    ty = foe.J + 0.5;
                    go = true;
                }
                else { tx = ty = 0; go = false; }
            }
            if (go) k.Dir = Math.Atan2(ty - k.Y, tx - k.X);
            else if (w.Dice.Next() < dt * 0.4) k.Dir += (w.Dice.Next() - 0.5) * 2;
            double speed = (k.Knight ? KnightSpeed : WalkSpeed) * dt;
            if (go && Dist(k, tx, ty) < speed) { k.X = tx; k.Y = ty; }
            else Move(w, k, speed);
            int ci = (int)k.X, cj = (int)k.Y;
            if (w.IsSwamp(ci, cj) && !w.Armageddon)
            {
                k.Strength = 0;
                if (k.Side == World.Good) w.Heard.Add(Sfx.Drown);
                continue;
            }
            if (w.Armageddon) continue;
            var town = w.TownAt(ci, cj);
            if (town != null && town.Side != k.Side) { Attack(w, k, town); continue; }
            if (town != null && !k.Knight && k.Ti != ci && town.Pop + k.Strength <= Capacity[town.Level] && g.MagnetX < 0)
            {
                town.Pop += k.Strength;
                k.Strength = 0;
                continue;
            }
            // Arrived: settle.
            if (!k.Knight && k.Ti == ci && k.Tj == cj && Dist(k, ci + 0.5, cj + 0.5) < 0.2 && w.Buildable(ci, cj) && !Crowded(w, ci, cj))
                Build(w, k, ci, cj);
        }
    }

    private static void Build(World w, Walker k, int i, int j)
    {
        var t = new Town { I = i, J = j, Side = k.Side, Pop = k.Strength };
        t.Level = LevelFor(FlatRound(w, i, j, w.H[i, j], t));
        if (t.Pop > Capacity[t.Level]) { k.Strength = t.Pop - Capacity[t.Level] + 1; t.Pop = Capacity[t.Level] - 1; k.Ti = k.Tj = -1; k.Look = 0.5; }
        else k.Strength = 0;
        w.Towns.Add(t);
        if (k.Side == World.Good) { w.Score += 10; w.Heard.Add(Sfx.Build); }
    }

    /// <summary>A walker into the other side's town: the stronger wins; a walker that wins settles there, a knight burns it.</summary>
    private static void Attack(World w, Walker k, Town t)
    {
        w.Heard.Add(Sfx.Fight);
        w.Marks.Add((t.I + 0.5, t.J + 0.5, Power.None, 1.4));
        if (k.Strength > t.Pop)
        {
            k.Strength -= t.Pop;
            w.Towns.Remove(t);
            w.Heard.Add(Sfx.Burn);
            if (k.Side == World.Good) w.Score += 100;
            if (!k.Knight && w.Buildable(t.I, t.J) && !Crowded(w, t.I, t.J)) Build(w, k, t.I, t.J);
        }
        else
        {
            t.Pop -= k.Strength;
            k.Strength = 0;
            if (t.Pop < 0.5) w.Towns.Remove(t);
        }
    }

    private static double Dist(Walker k, double x, double y) => Math.Sqrt((k.X - x) * (k.X - x) + (k.Y - y) * (k.Y - y));

    /// <summary>A step on, round water if need be (in Armageddon, through it).</summary>
    private static void Move(World w, Walker k, double speed)
    {
        foreach (double turn in new[] { 0, 0.7, -0.7, 1.5, -1.5, 2.3, -2.3, Math.PI })
        {
            double a = k.Dir + turn, nx = k.X + Math.Cos(a) * speed, ny = k.Y + Math.Sin(a) * speed;
            if (nx < 0.5 || ny < 0.5 || nx > Field.N - 0.5 || ny > Field.N - 0.5) continue;
            if (!w.Armageddon && w.Water((int)nx, (int)ny)) continue;
            k.X = nx;
            k.Y = ny;
            if (turn != 0 && k.Ti < 0) k.Dir = a;
            return;
        }
    }

    /// <summary>Walkers meeting: the other side's fight it out; the same side's join up.</summary>
    private static void Meet(World w)
    {
        var all = w.Walkers;
        for (int a = 0; a < all.Count; a++)
        {
            var p = all[a];
            if (p.Strength <= 0) continue;
            for (int b = a + 1; b < all.Count; b++)
            {
                var q = all[b];
                if (q.Strength <= 0) continue;
                double dx = p.X - q.X, dy = p.Y - q.Y, d2 = dx * dx + dy * dy;
                if (p.Side != q.Side && d2 < 0.36)
                {
                    double m = Math.Min(p.Strength, q.Strength);
                    p.Strength -= m;
                    q.Strength -= m;
                    w.Heard.Add(Sfx.Fight);
                    w.Marks.Add(((p.X + q.X) / 2, (p.Y + q.Y) / 2, Power.None, 1.4));
                    if (p.Strength <= 0) break;
                }
                else if (p.Side == q.Side && d2 < 0.09 && !p.Knight && !q.Knight && !w.Armageddon)
                {
                    p.Strength += q.Strength;
                    q.Strength = 0;
                }
            }
        }
    }
}
