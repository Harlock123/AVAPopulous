namespace AVAPopulous.Engine;

/// <summary>
/// The screen and the land: a square of 32 by 32 tiles seen corner-on, with a height at every corner (0 is the sea,
/// 8 the mountain tops), the bar along the top and the powers along the bottom.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int N = 32;
    public const int MaxHeight = 8;
    /// <summary>A tile seen from above: its width and depth on screen, and how far up a step of height lifts it.</summary>
    public const double TileW = 28, TileH = 14, Lift = 7;
    public const double OriginX = 512, OriginY = 150;

    /// <summary>Where a corner of the land is on screen.</summary>
    public static (double X, double Y) Screen(double x, double y, double h) =>
        (OriginX + (x - y) * TileW / 2, OriginY + (x + y) * TileH / 2 - h * Lift);

    public static bool InLand(int x, int y) => x >= 0 && y >= 0 && x <= N && y <= N;
    public static bool InTiles(int i, int j) => i >= 0 && j >= 0 && i < N && j < N;
}

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>
/// The divine powers, cheapest first. The magnet calls the people to a place; the earthquake shakes the land there
/// out of true; the swamp drowns whoever walks into it; the knight is the people's strongest made a crusader; the
/// volcano throws up a mountain of rock; the flood lowers the whole world a step; and Armageddon sends everyone to
/// fight it out in the middle.
/// </summary>
public enum Power { None, Magnet, Earthquake, Swamp, Knight, Volcano, Flood, Armageddon }

/// <summary>
/// The controls: nudge the cursor (screen directions), or point at a corner with the mouse; raise or lower the land
/// at the cursor; or call down a power there.
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Raise = false, bool Lower = false, Power Cast = Power.None, (int X, int Y)? Point = null);

/// <summary>What a god does, at a corner of the land.</summary>
public readonly record struct Deed(int X, int Y, bool Raise = false, bool Lower = false, Power Cast = Power.None);

/// <summary>A settlement on a flat tile, from a tent to a castle as the flat land round it grows.</summary>
public sealed class Town
{
    public int I, J, Side;
    public double Pop;
    public int Level = 1;
    public bool Burning;
    public Town Copy() => (Town)MemberwiseClone();
}

/// <summary>Someone on the move: looking for flat land to settle, going where the magnet calls, or a knight on crusade.</summary>
public sealed class Walker
{
    public double X, Y, Strength;
    public int Side;
    public bool Knight;
    /// <summary>The tile it's making for (-1: none), and the way it wanders when there's none.</summary>
    public int Ti = -1, Tj = -1;
    public double Dir, Look, Hunger;
    public Walker Copy() => (Walker)MemberwiseClone();
}

/// <summary>A god's side: its mana, its magnet, and how long until it can act again.</summary>
public sealed class God
{
    public double Mana, Busy;
    public int MagnetX = -1, MagnetY = -1;
    public double MagnetTime;
    /// <summary>When the god's mind next weighs calling down a power.</summary>
    public double NextPower;
    public God Copy() => (God)MemberwiseClone();
}

/// <summary>
/// Everything in the world that moves or changes, kept together so it can be copied whole: the land, the swamps
/// and the rocks, the two peoples, their towns and walkers, and their gods.
/// </summary>
public sealed class World
{
    public const int Good = 0, Evil = 1;

    public int Level = 1;
    public int[,] H = new int[Field.N + 1, Field.N + 1];
    /// <summary>Seconds each tile's swamp has left (0: none).</summary>
    public double[,] Swamp = new double[Field.N, Field.N];
    public bool[,] Rock = new bool[Field.N, Field.N];
    public List<Town> Towns = [];
    public List<Walker> Walkers = [];
    public God[] Gods = [new(), new()];
    /// <summary>Armageddon: everyone to the middle to fight.</summary>
    public bool Armageddon;
    public int Winner = -1;
    public int Score;
    public double Clock, Census;
    public Dice Dice = new(1);
    public List<Sfx> Heard = [];
    /// <summary>Things to show for a moment: a quake, a swamp opening, a volcano, a fight.</summary>
    public List<(double X, double Y, Power What, double T)> Marks = [];

    public World Copy() => new()
    {
        Level = Level, H = (int[,])H.Clone(), Swamp = (double[,])Swamp.Clone(), Rock = (bool[,])Rock.Clone(),
        Towns = Towns.Select(t => t.Copy()).ToList(), Walkers = Walkers.Select(w => w.Copy()).ToList(),
        Gods = [Gods[0].Copy(), Gods[1].Copy()], Armageddon = Armageddon, Winner = Winner, Score = Score, Clock = Clock,
        Census = Census, Dice = Dice,
    };

    public bool Flat(int i, int j) =>
        Field.InTiles(i, j) && H[i, j] == H[i + 1, j] && H[i, j] == H[i, j + 1] && H[i, j] == H[i + 1, j + 1];

    public bool Water(int i, int j) =>
        !Field.InTiles(i, j) || H[i, j] == 0 && H[i + 1, j] == 0 && H[i, j + 1] == 0 && H[i + 1, j + 1] == 0;

    /// <summary>A flat tile of dry land, free of swamp and rock: somewhere a town could stand.</summary>
    public bool Buildable(int i, int j) => Flat(i, j) && H[i, j] > 0 && !IsSwamp(i, j) && !Rock[i, j];

    public bool IsSwamp(int i, int j) => Field.InTiles(i, j) && Swamp[i, j] > 0;

    public Town? TownAt(int i, int j) => Towns.FirstOrDefault(t => t.I == i && t.J == j);

    public double Population(int side) => Towns.Where(t => t.Side == side).Sum(t => t.Pop) + Walkers.Where(w => w.Side == side).Sum(w => w.Strength);

    public double Height(double x, double y)
    {
        int i = Math.Clamp((int)x, 0, Field.N - 1), j = Math.Clamp((int)y, 0, Field.N - 1);
        double fx = Math.Clamp(x - i, 0, 1), fy = Math.Clamp(y - j, 0, 1);
        return H[i, j] * (1 - fx) * (1 - fy) + H[i + 1, j] * fx * (1 - fy) + H[i, j + 1] * (1 - fx) * fy + H[i + 1, j + 1] * fx * fy;
    }
}
