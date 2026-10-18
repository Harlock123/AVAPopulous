using Avalonia.Input;
using AVAPopulous.Engine;

namespace AVAPopulous.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avapopulous-tests-" + Guid.NewGuid().ToString("N"));

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    /// <summary>A world of flat land at height 2 with a sea rim, no one on it, and the evil god asleep.</summary>
    private static World Plain(int level = 1)
    {
        var w = new World();
        Sim.Start(w, level);
        w.Towns.Clear();
        w.Walkers.Clear();
        for (int x = 0; x <= Field.N; x++)
        for (int y = 0; y <= Field.N; y++)
            w.H[x, y] = x == 0 || y == 0 || x == Field.N || y == Field.N ? 0 : x == 1 || y == 1 || x == Field.N - 1 || y == Field.N - 1 ? 1 : 2;
        w.Gods[World.Evil].Busy = 1e9;
        w.Gods[World.Evil].NextPower = 1e9;
        return w;
    }

    private static void Run(World w, double seconds)
    {
        for (int i = 0; i < (int)(seconds * 60); i++) Sim.Step(w);
    }

    /// <summary>Keeps both sides alive so the world doesn't end under a test.</summary>
    private static Town Anchor(World w, int side, int i, int j, double pop = 5)
    {
        var t = new Town { I = i, J = j, Side = side, Pop = pop };
        w.Towns.Add(t);
        Sim.Census(w);
        return t;
    }

    private static bool Smooth(World w)
    {
        for (int x = 0; x < Field.N; x++)
        for (int y = 0; y < Field.N; y++)
            if (Math.Abs(w.H[x, y] - w.H[x + 1, y]) > 1 || Math.Abs(w.H[x, y] - w.H[x, y + 1]) > 1 || Math.Abs(w.H[x, y] - w.H[x + 1, y + 1]) > 1) return false;
        return true;
    }

    // ───────────────────────────── the land ─────────────────────────────

    [Fact]
    public void Raising_a_corner_lifts_the_land_round_it_and_no_corner_is_ever_more_than_a_step_from_its_neighbour()
    {
        var w = Plain();
        for (int k = 0; k < 4; k++) Sim.Shape(w, 16, 16, 1);
        Assert.Equal(6, w.H[16, 16]);
        Assert.Equal(5, w.H[17, 16]);
        Assert.Equal(4, w.H[18, 18]);
        Assert.Equal(2, w.H[22, 16]);
        Assert.True(Smooth(w));
        for (int k = 0; k < 4; k++) Sim.Shape(w, 10, 10, -1);
        Assert.Equal(0, w.H[10, 10]);
        Assert.Equal(1, w.H[11, 10]);
        Assert.True(Smooth(w));
        Assert.Empty(Sim.Shape(w, 10, 10, -1));
    }

    [Fact]
    public void Every_world_starts_as_an_island_with_a_home_for_each_people()
    {
        for (int level = 1; level <= 8; level++)
        {
            var w = new World();
            Sim.Start(w, level);
            Assert.True(Smooth(w));
            for (int k = 0; k <= Field.N; k++) Assert.Equal(0, w.H[k, 0] + w.H[0, k] + w.H[k, Field.N] + w.H[Field.N, k]);
            Assert.Single(w.Towns, t => t.Side == World.Good);
            Assert.Single(w.Towns, t => t.Side == World.Evil);
            Assert.All(w.Towns, t => Assert.True(w.Buildable(t.I, t.J)));
            Assert.Equal(2, w.Walkers.Count(k => k.Side == World.Good));
        }
    }

    [Fact]
    public void Shaping_costs_mana_and_works_only_near_your_own_towns()
    {
        var w = Plain();
        Anchor(w, World.Good, 8, 8);
        Anchor(w, World.Evil, 24, 24);
        w.Gods[World.Good].Mana = 10;
        Assert.True(Sim.Apply(w, World.Good, new Deed(10, 10, Raise: true)));
        Assert.Equal(3, w.H[10, 10]);
        Assert.Equal(10 - Sim.LandCost, w.Gods[World.Good].Mana);
        // Too soon after the last deed; then far from any town.
        Assert.False(Sim.Apply(w, World.Good, new Deed(10, 10, Raise: true)));
        Run(w, 0.2);
        Assert.False(Sim.Apply(w, World.Good, new Deed(20, 8, Raise: true)));
        // And with no mana.
        w.Gods[World.Good].Mana = 0;
        Run(w, 0.2);
        w.Gods[World.Good].Mana = 0;
        Assert.False(Sim.Apply(w, World.Good, new Deed(9, 9, Lower: true)));
    }

    // ───────────────────────────── the people ─────────────────────────────

    [Fact]
    public void A_walker_settles_on_flat_land_and_the_more_flat_land_round_a_town_the_bigger_it_grows()
    {
        var w = Plain();
        Anchor(w, World.Evil, 26, 26);
        w.Walkers.Add(new Walker { X = 10.2, Y = 10.3, Strength = 10, Side = World.Good });
        Run(w, 3);
        var t = Assert.Single(w.Towns, t => t.Side == World.Good);
        Assert.Equal(6, t.Level); // a castle: all the land round it is flat
        Assert.Equal(6, Sim.LevelFor(24));
        Assert.Equal(1, Sim.LevelFor(0));
        // Roughen the land round it: it shrinks.
        for (int x = t.I - 2; x <= t.I + 3; x++)
        for (int y = t.J - 2; y <= t.J + 3; y++)
            if ((x < t.I || x > t.I + 1 || y < t.J || y > t.J + 1) && (x + y) % 2 == 0) w.H[x, y] = 3;
        Sim.Census(w);
        Assert.True(t.Level <= 2);
    }

    [Fact]
    public void A_full_town_sends_half_its_people_out_to_find_land_of_their_own()
    {
        var w = Plain();
        Anchor(w, World.Evil, 26, 26);
        var t = Anchor(w, World.Good, 10, 10, Sim.Capacity[6] - 1);
        Sim.Census(w);
        Run(w, 2);
        var k = Assert.Single(w.Walkers);
        Assert.InRange(k.Strength, Sim.Capacity[6] / 2 - 5, Sim.Capacity[6] / 2 + 5);
        Assert.InRange(t.Pop, Sim.Capacity[6] / 2 - 5, Sim.Capacity[6] / 2 + 5);
    }

    [Fact]
    public void A_town_whose_land_is_moved_falls_half_its_people_lost_and_one_sunk_into_the_sea_drowns()
    {
        var w = Plain();
        Anchor(w, World.Evil, 26, 26);
        Anchor(w, World.Good, 10, 10, 40);
        Anchor(w, World.Good, 4, 4, 30);
        w.Gods[World.Good].Mana = 100;
        Assert.True(Sim.Apply(w, World.Good, new Deed(11, 11, Raise: true)));
        Assert.Null(w.TownAt(10, 10));
        var k = Assert.Single(w.Walkers);
        Assert.Equal(20, k.Strength);
        // Down into the sea.
        for (int n = 0; n < 2; n++)
        for (int x = 4; x <= 5; x++)
        for (int y = 4; y <= 5; y++)
            Sim.Shape(w, x, y, -1);
        Assert.True(w.Water(4, 4));
        Run(w, 0.1);
        w.Gods[World.Good].Busy = 0;
        Assert.True(Sim.Apply(w, World.Good, new Deed(7, 7, Lower: true)));
        Assert.Null(w.TownAt(4, 4));
    }

    [Fact]
    public void Walkers_go_round_water_and_a_swamp_swallows_them_until_it_dries_up()
    {
        var w = Plain();
        Anchor(w, World.Good, 26, 4);
        Anchor(w, World.Evil, 26, 26);
        // A lake across the way.
        for (int x = 8; x <= 12; x++)
        for (int y = 6; y <= 20; y++)
            w.H[x, y] = x is 8 or 12 || y is 6 or 20 ? 1 : 0;
        var k = new Walker { X = 6.5, Y = 13.5, Strength = 50, Side = World.Good, Dir = 0 };
        w.Walkers.Add(k);
        for (int i = 0; i < 60 * 20; i++)
        {
            Sim.Step(w);
            Assert.False(w.Water((int)k.X, (int)k.Y));
        }
        // A swamp, and a walker into it.
        w.Swamp[15, 15] = Sim.SwampSeconds;
        var lost = new Walker { X = 14.6, Y = 15.5, Strength = 30, Side = World.Good, Ti = 15, Tj = 15, Dir = 0 };
        w.Walkers.Add(lost);
        Run(w, 1);
        Assert.DoesNotContain(lost, w.Walkers);
        Run(w, Sim.SwampSeconds);
        Assert.False(w.IsSwamp(15, 15));
    }

    [Fact]
    public void Walkers_of_the_two_sides_meeting_fight_and_the_stronger_walks_on_weaker_by_the_other()
    {
        var w = Plain();
        Anchor(w, World.Good, 4, 4);
        Anchor(w, World.Evil, 26, 26);
        var a = new Walker { X = 15.5, Y = 15.5, Strength = 30, Side = World.Good, Look = 9 };
        var b = new Walker { X = 15.6, Y = 15.5, Strength = 12, Side = World.Evil, Look = 9 };
        w.Walkers.AddRange([a, b]);
        Sim.Step(w);
        Assert.DoesNotContain(b, w.Walkers);
        Assert.InRange(a.Strength, 17.5, 18);
    }

    [Fact]
    public void A_walker_into_the_other_sides_town_takes_it_if_stronger_and_is_lost_if_not()
    {
        var w = Plain();
        Anchor(w, World.Good, 4, 4);
        var town = Anchor(w, World.Evil, 15, 15, 20);
        Anchor(w, World.Evil, 26, 26);
        // The magnet draws them on through the town.
        w.Gods[World.Good].MagnetX = 17;
        w.Gods[World.Good].MagnetY = 16;
        w.Gods[World.Good].MagnetTime = 99;
        var weak = new Walker { X = 13.6, Y = 15.5, Strength = 8, Side = World.Good };
        w.Walkers.Add(weak);
        Run(w, 1);
        Run(w, 0.5);
        Assert.DoesNotContain(weak, w.Walkers);
        Assert.InRange(town.Pop, 12, 16.5); // 20 less the walker's 8, and two seconds' growth
        var strong = new Walker { X = 13.6, Y = 15.5, Strength = 40, Side = World.Good };
        w.Walkers.Add(strong);
        Run(w, 1.5);
        Assert.Equal(World.Good, w.TownAt(15, 15)!.Side);
    }

    [Fact]
    public void Mana_comes_with_people()
    {
        var w = Plain();
        Anchor(w, World.Good, 8, 8, 10);
        Anchor(w, World.Good, 14, 8, 10);
        Anchor(w, World.Evil, 26, 26);
        w.Gods[World.Good].Mana = 0;
        Run(w, 10);
        double few = w.Gods[World.Good].Mana;
        foreach (var t in w.Towns.Where(t => t.Side == World.Good)) t.Pop = 100;
        w.Gods[World.Good].Mana = 0;
        Run(w, 10);
        Assert.True(w.Gods[World.Good].Mana > few * 4);
    }

    // ───────────────────────────── the powers ─────────────────────────────

    [Fact]
    public void The_knight_is_your_strongest_made_stronger_and_he_burns_their_towns()
    {
        var w = Plain();
        Anchor(w, World.Good, 4, 4);
        var a = Anchor(w, World.Evil, 16, 6, 10);
        Anchor(w, World.Evil, 26, 26, 30);
        w.Walkers.Add(new Walker { X = 6.5, Y = 6.5, Strength = 40, Side = World.Good });
        w.Walkers.Add(new Walker { X = 4.5, Y = 8.5, Strength = 10, Side = World.Good });
        w.Gods[World.Good].Mana = Sim.Cost(Power.Knight);
        Assert.True(Sim.Cast(w, World.Good, Power.Knight, 0, 0));
        var knight = Assert.Single(w.Walkers, k => k.Knight);
        Assert.Equal(60, knight.Strength);
        Run(w, 12);
        Assert.DoesNotContain(a, w.Towns);
        Assert.Null(w.TownAt(16, 6));
        Assert.False(Sim.Cast(w, World.Good, Power.Knight, 0, 0));
    }

    [Fact]
    public void The_flood_lowers_the_world_a_step_and_the_low_towns_drown()
    {
        var w = Plain();
        Anchor(w, World.Good, 8, 8, 20);
        var low = Anchor(w, World.Evil, 2, 20, 20);
        for (int x = 1; x <= 4; x++) for (int y = 19; y <= 22; y++) w.H[x, y] = 1;
        Anchor(w, World.Evil, 26, 26, 20);
        Assert.True(w.Buildable(2, 20));
        w.Gods[World.Good].Mana = Sim.Cost(Power.Flood);
        Assert.True(Sim.Cast(w, World.Good, Power.Flood, 0, 0));
        Assert.Equal(1, w.H[16, 16]);
        Assert.DoesNotContain(low, w.Towns);
        Assert.NotNull(w.TownAt(8, 8));
        Assert.Equal(0, w.Gods[World.Good].Mana);
    }

    [Fact]
    public void The_volcano_throws_up_rock_and_burns_the_towns_under_it()
    {
        var w = Plain();
        Anchor(w, World.Good, 4, 4);
        var t = Anchor(w, World.Evil, 16, 16, 50);
        Anchor(w, World.Evil, 26, 26);
        w.Gods[World.Good].Mana = Sim.Cost(Power.Volcano);
        Assert.True(Sim.Cast(w, World.Good, Power.Volcano, 16, 16));
        Assert.Equal(6, w.H[16, 16]);
        Assert.True(Smooth(w));
        Assert.DoesNotContain(t, w.Towns);
        Assert.True(w.Rock[15, 15] || !w.Buildable(15, 15));
    }

    [Fact]
    public void The_earthquake_shakes_the_land_out_of_true_and_the_towns_there_fall()
    {
        var w = Plain();
        Anchor(w, World.Good, 4, 4);
        for (int i = 14; i <= 18; i += 2) Anchor(w, World.Evil, i, 16, 30);
        Anchor(w, World.Evil, 26, 26);
        w.Gods[World.Good].Mana = Sim.Cost(Power.Earthquake);
        Assert.True(Sim.Cast(w, World.Good, Power.Earthquake, 16, 16));
        Assert.True(Smooth(w));
        Assert.True(w.Towns.Count(t => t.Side == World.Evil) < 4);
    }

    [Fact]
    public void Armageddon_sends_everyone_to_the_middle_to_fight_it_out()
    {
        var w = Plain();
        Anchor(w, World.Good, 4, 4, 120);
        Anchor(w, World.Good, 8, 4, 120);
        Anchor(w, World.Evil, 26, 26, 100);
        w.Gods[World.Good].Mana = Sim.Cost(Power.Armageddon);
        Assert.True(Sim.Cast(w, World.Good, Power.Armageddon, 0, 0));
        Assert.Empty(w.Towns);
        Assert.True(w.Armageddon);
        for (int i = 0; i < 60 * 60 && w.Winner < 0; i++) Sim.Step(w);
        Assert.Equal(World.Good, w.Winner);
        Assert.InRange(w.Population(World.Good), 100, 140);
    }

    [Fact]
    public void The_evil_god_gets_his_powers_world_by_world_and_the_good_god_has_them_all()
    {
        var w = Plain(1);
        Assert.True(Sim.Allowed(w, World.Good, Power.Volcano));
        Assert.True(Sim.Allowed(w, World.Evil, Power.Magnet));
        Assert.False(Sim.Allowed(w, World.Evil, Power.Earthquake));
        Assert.False(Sim.Allowed(w, World.Evil, Power.Knight));
        w.Level = 6;
        Assert.True(Sim.Allowed(w, World.Evil, Power.Volcano));
        Assert.False(Sim.Allowed(w, World.Evil, Power.Flood));
        Assert.True(Sim.EvilPace(6) < Sim.EvilPace(1));
    }

    // ───────────────────────────── the gods' minds ─────────────────────────────

    private static int PlayWorld(int level, bool lazy, double maxSeconds = 720)
    {
        var w = new World();
        Sim.Start(w, level);
        var p = new Pilot();
        for (int t = 0; t < maxSeconds * 60 && w.Winner < 0; t++)
        {
            if (!lazy)
            {
                var c = p.Decide(w);
                if (c.Raise || c.Lower || c.Cast != Power.None) Sim.Apply(w, World.Good, new Deed(c.Point?.X ?? 0, c.Point?.Y ?? 0, c.Raise, c.Lower, c.Cast));
            }
            Sim.Step(w);
        }
        return w.Winner;
    }

    [Fact]
    public void The_computer_god_wins_the_first_five_worlds()
    {
        for (int level = 1; level <= 5; level++) Assert.Equal(World.Good, PlayWorld(level, lazy: false));
    }

    [Fact]
    public void A_god_who_does_nothing_loses_his_people()
    {
        Assert.Equal(World.Evil, PlayWorld(1, lazy: true));
        Assert.Equal(World.Evil, PlayWorld(2, lazy: true));
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void The_cursor_moves_the_way_the_arrows_point_on_screen_and_goes_where_the_mouse_points()
    {
        var g = Engine();
        g.Go();
        int x = g.CursorX, y = g.CursorY;
        g.Tick(new Controls(Dy: -1));
        Assert.Equal((x - 1, y - 1), (g.CursorX, g.CursorY));
        var (sx0, _) = Field.Screen(x, y, 0);
        var (sx1, _) = Field.Screen(g.CursorX, g.CursorY, 0);
        Assert.Equal(sx0, sx1); // straight up the screen
        g.Run(10);
        g.Tick(new Controls(Dx: 1));
        Assert.Equal((x, y - 2), (g.CursorX, g.CursorY));
        g.Tick(new Controls(Point: (20, 12)));
        Assert.Equal((20, 12), (g.CursorX, g.CursorY));
    }

    [Fact]
    public void A_power_key_casts_once_at_the_cursor_and_a_power_with_too_little_mana_is_refused()
    {
        var g = Engine();
        g.Silent = false;
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Go();
        g.World.Gods[World.Good].Mana = 50;
        g.Tick(new Controls(Point: (10, 20)));
        g.Run(30, new Controls(Cast: Power.Magnet));
        Assert.Equal((10, 20), (g.World.Gods[World.Good].MagnetX, g.World.Gods[World.Good].MagnetY));
        Assert.InRange(g.World.Gods[World.Good].Mana, 30, 32);
        g.Tick(default);
        g.Tick(new Controls(Cast: Power.Armageddon));
        Assert.Contains(Sfx.Refused, heard);
        Assert.False(g.World.Armageddon);
    }

    [Fact]
    public void Winning_a_world_moves_on_to_the_next_and_losing_ends_the_game_with_a_high_score()
    {
        var g = Engine();
        g.Go();
        foreach (var t in g.World.Towns.Where(t => t.Side == World.Evil)) t.Pop = 0;
        g.World.Towns.RemoveAll(t => t.Side == World.Evil);
        g.World.Walkers.RemoveAll(k => k.Side == World.Evil);
        g.Tick(default);
        Assert.Equal(Phase.Won, g.Phase);
        Assert.True(g.Score >= 1000);
        g.Run((int)(60 * GameEngine.WonSeconds) + 5);
        Assert.Equal(2, g.Level);
        Assert.Equal(Phase.Start, g.Phase);
        g.World.Score = 99000;
        int score = g.Score;
        g.Run((int)(60 * GameEngine.StartSeconds) + 5);
        g.World.Towns.RemoveAll(t => t.Side == World.Good);
        g.World.Walkers.RemoveAll(k => k.Side == World.Good);
        g.Tick(default);
        Assert.Equal(Phase.Lost, g.Phase);
        g.Run((int)(60 * GameEngine.LostSeconds) + 5);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "GOD") g.Initials!.Type(ch);
        g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(new HighScoreEntry("GOD", score, 2), g.HighScores.Entries[g.NewEntryIndex]);
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_the_title()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void A_copied_world_goes_on_just_as_the_original()
    {
        var a = new World();
        Sim.Start(a, 3);
        Run(a, 20);
        var b = a.Copy();
        Run(a, 30);
        Run(b, 30);
        Assert.Equal(a.Population(0), b.Population(0), 6);
        Assert.Equal(a.Population(1), b.Population(1), 6);
        Assert.Equal(a.Towns.Count, b.Towns.Count);
    }

    [Fact]
    public void The_keys()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        input.OnKeyDown(Key.Z);
        var c = input.Player;
        Assert.Equal(-1, c.Dy);
        Assert.True(c.Raise);
        Assert.Null(c.Point);
        input.OnKeyUp(Key.Up);
        input.OnKeyUp(Key.Z);
        input.OnKeyDown(Key.X);
        input.OnKeyDown(Key.D5);
        c = input.Player;
        Assert.True(c.Lower);
        Assert.Equal(Power.Volcano, c.Cast);
        // The mouse: point, and a power armed from the bar cast with the next click.
        input.PointX = 12;
        input.PointY = 9;
        input.Armed = Power.Swamp;
        input.ClickWithArmed();
        input.OnKeyUp(Key.D5);
        c = input.Player;
        Assert.Equal((12, 9), c.Point);
        Assert.Equal(Power.Swamp, c.Cast);
        Assert.Equal(Power.None, input.Armed);
    }
}
