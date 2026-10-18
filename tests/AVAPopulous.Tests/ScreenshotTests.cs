using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAPopulous.Engine;
using AVAPopulous.Views;
using Controls = AVAPopulous.Engine.Controls;

namespace AVAPopulous.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set POPULOUS_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avapopulous-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("POPULOUS_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAPopulous-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 600)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 6 == 0) c.Advance(0.1);
        }
        return false;
    }

    private static void Moment(int level, string name, Func<GameEngine, bool> moment)
    {
        var (w, c, g) = Open();
        g.Go(level);
        Assert.True(PlayUntil(g, c, moment), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 14820, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 600 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Settling() => Moment(1, "settling", g => g.World.Clock > 70);

    [AvaloniaFact]
    public void Castles() => Moment(3, "castles", g => g.World.Towns.Count(t => t.Side == World.Good && t.Level == 6) >= 4 && g.World.Towns.Count(t => t.Side == World.Evil) >= 6);

    [AvaloniaFact]
    public void The_desert() => Moment(2, "desert", g => g.World.Clock > 110);

    [AvaloniaFact]
    public void A_volcano()
    {
        var (w, c, g) = Open();
        g.Go(4);
        Assert.True(PlayUntil(g, c, g => g.World.Clock > 120));
        var town = g.World.Towns.Where(t => t.Side == World.Evil).MaxBy(t => t.Pop)!;
        g.World.Gods[World.Good].Mana = 1200;
        for (int i = 0; i < 20; i++) g.Tick(new Controls(Point: (town.I, town.J)));
        g.Tick(new Controls(Cast: Power.Volcano, Point: (town.I, town.J)));
        for (int i = 0; i < 40; i++) { g.Tick(default); c.Advance(1 / 60.0); }
        Shot(w, c, "volcano");
    }

    [AvaloniaFact]
    public void Armageddon() => Moment(1, "armageddon", g => g.World.Armageddon && g.World.Marks.Count == 0 && g.World.Walkers.Count(k => Math.Abs(k.X - 16) + Math.Abs(k.Y - 16) < 5) > 25);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(3);
        for (int i = 0; i < 60 * 900 && g.State == GameState.Playing; i++) { g.Tick(default); if (i % 6 == 0) c.Advance(0.1); }
        for (int i = 0; i < 90; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(GameState.GameOver, g.State);
        Shot(w, c, "game-over");
    }
}
