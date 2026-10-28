# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
the downloads; this file covers how the code is put together and the rules that keep it working.

## What this is

A god game in the style of Bullfrog's 1989 Populous, in C# on .NET 10 and Avalonia 12. One self-contained
executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in code. The sounds,
tunes and the two spoken words are synthesised by `tools/make_sounds.py`, using the formant voice in
`tools/speech.py`.

## Commands

```bash
dotnet build AVAPopulous.slnx                     # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAPopulous.slnx                      # every test runs headless: no window opens
dotnet run --project src/AVAPopulous              # play it; --no-audio for silence
POPULOUS_SHOTS=$PWD/Images dotnet test tests/AVAPopulous.Tests --filter ScreenshotTests   # refresh the README's pictures
POPULOUS_PROBE=1 dotnet test tests/AVAPopulous.Tests --filter PilotProbe   # report how the computer god does
python3 tools/make_sounds.py                      # rebuild src/AVAPopulous/Sounds (standard-library Python only)
```

The probe plays worlds 1 to 6; `POPULOUS_FROM` and `POPULOUS_WORLDS` change the first and last world, and
`POPULOUS_PROBE=lazy` runs a god that does nothing.

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings
for every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAPopulous/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, initials, high scores), the worlds and the
    fixed-step loop.
  - `Sim.cs` holds the rules, one step at a time: shaping the land, settling, walkers, battles and the powers.
  - `World.cs` has `Field` (the screen and the 32x32 land), the `Dice` generator, the powers and `Controls`.
  - `Pilot.cs` is a god's mind: the evil god's, and the good god's in the demonstration.
  - `InputHandler.cs` (keys and mouse) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAPopulous/Views/`: `GameCanvas` draws the land and the power bar (`GameCanvas.Draw.cs`) and runs the
  timer.
- `src/AVAPopulous/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAPopulous/Assets/Fonts/`: the VT323 font, under the OFL.
- `tests/AVAPopulous.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay, screenshots and the
  `PilotProbe` tuning report.

## Rules that keep it working

- **Fixed steps.** `GameEngine` collects frame time (at most 0.25 s a frame) and advances in fixed 1/60 s steps
  (`Field.Step`), so tests can play deterministically. Draw random numbers from the world's `Dice` (`w.Dice`),
  never from `Random` or `Random.Shared`. Each world seeds it from its level number, so a world plays out the
  same way every time. `Dice` is a struct: copy it out, use it and store it back, as `Sim` does.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** `Controls.Dy` nudges the cursor in screen directions: -1 is up the
  screen. Keys, D-pad and stick must all keep that sign; the mouse points at a corner with `Point` instead.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up; a player's own `ALSOFT_CONF` is left alone.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAPopulous`); tests
  load the table from a fresh temp folder and set `Silent = true` on the engine.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls) to match.
- New sounds go into `tools/make_sounds.py` (and new words into its `say` calls), not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. A release also asks the portfolio site to rebuild.
