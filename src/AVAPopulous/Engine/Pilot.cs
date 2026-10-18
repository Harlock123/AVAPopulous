namespace AVAPopulous.Engine;

/// <summary>
/// A god's mind: the evil god's, and the good god's in the demonstration. Every second or so it weighs the powers:
/// Armageddon when its people far outnumber the other side's, a knight when it has none, a volcano, an earthquake or
/// a swamp on the other side's thickest towns, the magnet to send its people at them when it's the stronger. The
/// rest of the time it shapes the land for its people: flat ground under a walker with nowhere to settle, then the
/// land round its towns brought to their height a corner at a time, the town that's nearest growing first, so they
/// grow from tents to castles. It never moves a corner that would bring down a town of its own; and a corner of
/// the other side's town within its reach, it lowers, and the town is gone.
/// </summary>
public static class Mind
{
    public static Deed? Think(World w, int side)
    {
        var g = w.Gods[side];
        if (w.Winner >= 0 || w.Armageddon) return null;
        if (w.Clock >= g.NextPower)
        {
            g.NextPower = w.Clock + 1.2;
            if (Power(w, side) is { } p) return p;
        }
        if (g.Mana < Sim.LandCost + 2 || Saving(w, side) && w.Dice.Next() < 0.8) return null;
        return Shape(w, side);
    }

    /// <summary>Far ahead: saving the mana for Armageddon, to finish it.</summary>
    private static bool Saving(World w, int side)
    {
        double mine = w.Population(side), theirs = w.Population(1 - side);
        return Sim.Allowed(w, side, Engine.Power.Armageddon) && theirs > 0 && mine > theirs * 1.8;
    }

    private static Deed? Power(World w, int side)
    {
        var g = w.Gods[side];
        int other = 1 - side;
        double mine = w.Population(side), theirs = w.Population(other);
        bool Can(Power p, double spare = 0) => Sim.Allowed(w, side, p) && g.Mana >= Sim.Cost(p) + spare;
        var theirTowns = w.Towns.Where(t => t.Side == other).ToList();
        if (Saving(w, side)) return Can(Engine.Power.Armageddon) ? new Deed(Sim.Middle.X, Sim.Middle.Y, Cast: Engine.Power.Armageddon) : null;
        if (Can(Engine.Power.Knight, 200) && theirTowns.Count > 0 && !w.Walkers.Any(k => k.Side == side && k.Knight) && mine > 60)
            return new Deed(0, 0, Cast: Engine.Power.Knight);
        // The other side's thickest towns, and none of ours near them.
        Town? target = null;
        double best = 0;
        foreach (var t in theirTowns)
        {
            double v = 0;
            foreach (var u in w.Towns)
            {
                double d = Math.Max(Math.Abs(u.I - t.I), Math.Abs(u.J - t.J));
                if (u.Side == other && d <= 3) v += u.Pop + 10 * u.Level;
                if (u.Side == side && d <= 4) v -= 1000;
            }
            if (v > best) { best = v; target = t; }
        }
        if (target != null)
        {
            if (Can(Engine.Power.Volcano, 150) && best >= 120) return new Deed(target.I, target.J, Cast: Engine.Power.Volcano);
            if (Can(Engine.Power.Swamp, 250) && best >= 60) return new Deed(target.I, target.J, Cast: Engine.Power.Swamp);
            if (Can(Engine.Power.Earthquake, 350) && best >= 80) return new Deed(target.I, target.J, Cast: Engine.Power.Earthquake);
        }
        if (Can(Engine.Power.Flood, 100) && TownHeight(w, side) - TownHeight(w, other) >= 1.5 && theirTowns.Count(t => w.H[t.I, t.J] == 1) >= 3)
            return new Deed(0, 0, Cast: Engine.Power.Flood);
        // Stronger: the people to the nearest of their towns.
        if (Can(Engine.Power.Magnet) && g.MagnetX < 0 && mine > theirs * 1.6 + 40 && theirTowns.Count > 0)
        {
            var mineAt = w.Towns.Where(t => t.Side == side).ToList();
            if (mineAt.Count > 0)
            {
                double cx = mineAt.Average(t => t.I), cy = mineAt.Average(t => t.J);
                var near = theirTowns.MinBy(t => Math.Abs(t.I - cx) + Math.Abs(t.J - cy))!;
                return new Deed(near.I, near.J, Cast: Engine.Power.Magnet);
            }
        }
        return null;
    }

    private static double TownHeight(World w, int side)
    {
        var ts = w.Towns.Where(t => t.Side == side).ToList();
        return ts.Count == 0 ? 0 : ts.Average(t => w.H[t.I, t.J]);
    }

    /// <summary>The corners of a side's towns, at their heights: what shaping mustn't move.</summary>
    private static List<(int X, int Y, int H)> Keep(World w, int side)
    {
        var keep = new List<(int, int, int)>();
        foreach (var t in w.Towns)
            if (t.Side == side)
                for (int x = t.I; x <= t.I + 1; x++)
                for (int y = t.J; y <= t.J + 1; y++)
                    keep.Add((x, y, w.H[x, y]));
        return keep;
    }

    /// <summary>Whether a step at a corner leaves every one of the side's towns where it is.</summary>
    private static bool Safe(World w, List<(int X, int Y, int H)> keep, int x, int y, int by)
    {
        var c = w.Copy();
        Sim.Shape(c, x, y, by);
        foreach (var k in keep)
            if (c.H[k.X, k.Y] != k.H) return false;
        return true;
    }

    private static Deed? Shape(World w, int side)
    {
        var keep = Keep(w, side);
        var mineAt = new HashSet<(int, int)>(keep.Select(k => (k.X, k.Y)));
        // A corner of their town near enough: down with it. (The evil god learns this world by world.)
        double spite = side == World.Good ? 1 : Math.Min(0.5, (w.Level - 1) * 0.06);
        foreach (var t in w.Towns)
        {
            if (w.Dice.Next() >= spite) break;
            if (t.Side == side || t.Pop < 20) continue;
            for (int x = t.I; x <= t.I + 1; x++)
            for (int y = t.J; y <= t.J + 1; y++)
                if (!mineAt.Contains((x, y)) && Sim.InReach(w, side, x, y) && w.H[x, y] > 0 && Safe(w, keep, x, y, -1))
                    return new Deed(x, y, Lower: true);
        }
        // A walker with nowhere to settle: flat ground where it stands.
        foreach (var k in w.Walkers)
        {
            if (k.Side != side || k.Knight || k.Ti >= 0 || w.Gods[side].MagnetX >= 0) continue;
            int i = (int)k.X, j = (int)k.Y;
            if (w.Water(i, j) || Sim.Crowded(w, i, j)) continue;
            int h = Math.Max(1, (int)Math.Round(w.Height(k.X, k.Y)));
            if (Toward(w, side, keep, i, j, h, 0) is { } d) return d;
        }
        // The towns' land, brought to their height: the town nearest the next size first.
        Deed? pick = null;
        int least = int.MaxValue;
        foreach (var t in w.Towns)
        {
            if (t.Side != side || t.Level >= 6) continue;
            int h = w.H[t.I, t.J], left = 0;
            for (int x = t.I - 2; x <= t.I + 3; x++)
            for (int y = t.J - 2; y <= t.J + 3; y++)
                if (Field.InLand(x, y) && w.H[x, y] != h && Sim.InReach(w, side, x, y)) left++;
            if (left == 0 || left >= least) continue;
            if (Toward(w, side, keep, t.I, t.J, h, 2) is { } d) { least = left; pick = d; }
        }
        return pick ?? Expand(w, side, keep);
    }

    /// <summary>New ground: a tile three out from a town, with room round it, made flat for the next walker to settle.</summary>
    private static Deed? Expand(World w, int side, List<(int X, int Y, int H)> keep)
    {
        (int I, int J, int H)? site = null;
        int least = int.MaxValue;
        foreach (var t in w.Towns)
        {
            if (t.Side != side) continue;
            for (int i = t.I - 3; i <= t.I + 3; i++)
            for (int j = t.J - 3; j <= t.J + 3; j++)
            {
                if (Math.Max(Math.Abs(i - t.I), Math.Abs(j - t.J)) != 3 || !Field.InTiles(i, j) || w.Water(i, j) || Sim.Crowded(w, i, j) || w.IsSwamp(i, j) || w.Rock[i, j]) continue;
                if (w.Buildable(i, j)) continue;
                int[] hs = [w.H[i, j], w.H[i + 1, j], w.H[i, j + 1], w.H[i + 1, j + 1]];
                int h = Math.Max(1, hs.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key);
                int work = hs.Sum(v => Math.Abs(v - h));
                if (work < least && Sim.InReach(w, side, i, j) && Sim.InReach(w, side, i + 1, j + 1)) { least = work; site = (i, j, h); }
            }
        }
        return site is { } s ? Toward(w, side, keep, s.I, s.J, s.H, 0) : null;
    }

    /// <summary>The nearest corner round a tile not at a height, and a step toward it, if that's safe.</summary>
    private static Deed? Toward(World w, int side, List<(int X, int Y, int H)> keep, int i, int j, int h, int r)
    {
        var corners = new List<(int X, int Y, double D)>();
        for (int x = i - r; x <= i + 1 + r; x++)
        for (int y = j - r; y <= j + 1 + r; y++)
            if (Field.InLand(x, y) && w.H[x, y] != h && Sim.InReach(w, side, x, y))
                corners.Add((x, y, Math.Max(Math.Abs(x - i - 0.5), Math.Abs(y - j - 0.5)) + Math.Abs(w.H[x, y] - h) * 0.1));
        foreach (var c in corners.OrderBy(c => c.D))
        {
            int by = w.H[c.X, c.Y] < h ? 1 : -1;
            if (Safe(w, keep, c.X, c.Y, by)) return new Deed(c.X, c.Y, Raise: by > 0, Lower: by < 0);
        }
        return null;
    }
}

/// <summary>The computer player, for the demonstration and the tests: the good god's mind at the controls.</summary>
public sealed class Pilot
{
    public string Doing { get; private set; } = "";

    public void Reset() => Doing = "";

    public Controls Decide(GameEngine g) => g.Phase != Phase.Play ? default : Decide(g.World);

    public Controls Decide(World w)
    {
        if (w.Gods[World.Good].Busy > 0 || Mind.Think(w, World.Good) is not { } d) return default;
        Doing = d.Cast != Power.None ? d.Cast.ToString() : d.Raise ? "raising" : "lowering";
        // A knight or a flood isn't cast anywhere in particular: the cursor stays put.
        return new Controls(Raise: d.Raise, Lower: d.Lower, Cast: d.Cast, Point: d.Cast is Power.Knight or Power.Flood ? null : (d.X, d.Y));
    }
}
