namespace AVAPopulous.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>A moment to look at the new world before it starts.</summary>
    Start,
    Play,
    /// <summary>The evil people gone: a moment, then the next world.</summary>
    Won,
    /// <summary>Your people gone.</summary>
    Lost,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Raise, Lower, Build, Grow, Fight, Burn, Drown,
    Magnet, Quake, SwampSound, KnightSound, VolcanoSound, FloodSound, ArmageddonSound,
    Win, Lose, Refused,
    SayVictory, SayDefeat,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 75;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 2.5, WonSeconds = 5, LostSeconds = 4, OverSeconds = 5;
    /// <summary>How often a held key moves the cursor.</summary>
    public const double CursorRepeat = 0.09;

    private double _accumulator, _idle, _cursorWait;
    private bool _startPrev, _tunePending = true, _music;
    private Power _castPrev;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Score => World.Score;
    public int Level => World.Level;
    /// <summary>The cursor: a corner of the land.</summary>
    public int CursorX { get; private set; } = Sim.GoodHome.X;
    public int CursorY { get; private set; } = Sim.GoodHome.Y;
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    /// <summary>Each world's name.</summary>
    public static string WorldName(int level)
    {
        string[] a = ["GREEN", "AMBER", "STONE", "SILVER", "RIVER", "WINTER", "EMBER", "MEADOW", "IRON", "COPPER", "SALT", "THUNDER"];
        string[] b = ["HOLM", "DALE", "MERE", "FELL", "SHORE", "MOOR", "VALE", "HEATH", "LEA", "TOR"];
        return a[(level - 1) % a.Length] + b[(level * 7 + level / a.Length) % b.Length];
    }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
            // A cast is a press: don't repeat it within one frame's steps.
            c = c with { Cast = Power.None };
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Level);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        BeginWorld(1);
    }

    private void BeginWorld(int level)
    {
        int score = World.Score;
        World = new World { Score = score };
        Sim.Start(World, level);
        CursorX = Sim.GoodHome.X;
        CursorY = Sim.GoodHome.Y;
        SetPhase(Phase.Start);
        Message = $"WORLD {level}: {WorldName(level)}";
        MessageTime = StartSeconds;
        Pilot.Reset();
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    /// <summary>The cursor: to where the mouse points, or a corner on in the screen direction held.</summary>
    private void MoveCursor(Controls c, double dt)
    {
        if (c.Point is { } p)
        {
            CursorX = Math.Clamp(p.X, 0, Field.N);
            CursorY = Math.Clamp(p.Y, 0, Field.N);
        }
        if (c.Dx == 0 && c.Dy == 0) { _cursorWait = 0; return; }
        _cursorWait -= dt;
        if (_cursorWait > 0) return;
        _cursorWait = CursorRepeat;
        // The land is seen corner-on: up the screen is back along both axes, right is along x and back along y.
        int dx = Math.Clamp(c.Dx + c.Dy, -1, 1), dy = Math.Clamp(c.Dy - c.Dx, -1, 1);
        CursorX = Math.Clamp(CursorX + dx, 0, Field.N);
        CursorY = Math.Clamp(CursorY + dy, 0, Field.N);
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                MoveCursor(c, dt);
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
            {
                MoveCursor(c, dt);
                var cast = c.Cast != _castPrev ? c.Cast : Power.None;
                _castPrev = c.Cast;
                if (cast != Power.None)
                {
                    if (!Sim.Apply(w, World.Good, new Deed(CursorX, CursorY, Cast: cast))) Play(Sfx.Refused);
                }
                else if (c.Raise || c.Lower) Sim.Apply(w, World.Good, new Deed(CursorX, CursorY, c.Raise, c.Lower && !c.Raise));
                Sim.Step(w);
                if (w.Winner == World.Good)
                {
                    SetPhase(Phase.Won);
                    Music(false);
                    w.Score += 1000 * w.Level + (int)w.Population(World.Good);
                    Play(Sfx.Victory);
                    Play(Sfx.SayVictory);
                    Message = $"{WorldName(w.Level)} IS YOURS";
                    MessageTime = WonSeconds;
                }
                else if (w.Winner == World.Evil)
                {
                    SetPhase(Phase.Lost);
                    Music(false);
                    Play(Sfx.SayDefeat);
                    Message = "YOUR PEOPLE ARE NO MORE";
                    MessageTime = LostSeconds;
                }
                break;
            }
            case Phase.Won:
                if (PhaseTime > WonSeconds) { BeginWorld(w.Level + 1); return; }
                break;
            case Phase.Lost:
                if (PhaseTime > LostSeconds) { End(); return; }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void End()
    {
        Play(Sfx.GameOver);
        Music(false);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a world, past the opening moment.</summary>
    public void Go(int level = 1)
    {
        World = new World();
        BeginWorld(level);
        State = GameState.Playing;
        SetPhase(Phase.Play);
        Message = null;
    }
}
