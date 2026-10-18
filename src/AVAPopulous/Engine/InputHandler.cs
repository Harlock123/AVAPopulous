using Avalonia.Input;

namespace AVAPopulous.Engine;

/// <summary>
/// Keyboard, mouse and gamepad boiled down to the controls: the cursor, raise, lower, and the powers.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);
    private bool _padCyclePrev, _padCastPrev, _mouseCastPending;

    /// <summary>The corner the mouse is over (-1: none), and its buttons.</summary>
    public int PointX { get; set; } = -1;
    public int PointY { get; set; } = -1;
    public bool MouseLeft { get; set; }
    public bool MouseRight { get; set; }
    /// <summary>A power chosen from the bar (or with the gamepad), to call down with the next click (or press).</summary>
    public Power Armed { get; set; }

    /// <summary>A click on the land with a power armed: cast it there.</summary>
    public void ClickWithArmed() => _mouseCastPending = true;

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// The arrows (or WASD) move the cursor; Z or Space raises the land there, X lowers it; 1 to 7 call down the
    /// powers. The mouse points; the left button raises, the right lowers; a power clicked on the bar is cast with
    /// the next click on the land. On a gamepad: the stick or D-pad moves, A raises, B lowers, Y chooses a power and
    /// X casts it.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int dx = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            int dy = (Any(Key.Down, Key.S, Key.NumPad2) ? 1 : 0) - (Any(Key.Up, Key.W, Key.NumPad8) ? 1 : 0);
            if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
            if (dy == 0) dy = pad.DPadUp || pad.LeftStickY < -0.4f ? -1 : pad.DPadDown || pad.LeftStickY > 0.4f ? 1 : 0;
            bool raise = Any(Key.Z, Key.Space) || pad.ButtonA || MouseLeft && Armed == Power.None;
            bool lower = Any(Key.X) || pad.ButtonB || MouseRight;
            var cast = Power.None;
            for (int k = 1; k <= 7; k++)
                if (Any(Key.D0 + k, Key.NumPad0 + k)) cast = (Power)k;
            // The gamepad: Y steps through the powers, X casts the one chosen.
            bool cycle = pad.ButtonY, fire = pad.ButtonX;
            if (cycle && !_padCyclePrev) Armed = (Power)((int)Armed % 7 + 1);
            _padCyclePrev = cycle;
            if (fire && !_padCastPrev && Armed != Power.None) { cast = Armed; Armed = Power.None; }
            _padCastPrev = fire;
            if (_mouseCastPending)
            {
                _mouseCastPending = false;
                cast = Armed;
                Armed = Power.None;
            }
            return new Controls(dx, dy, raise, lower, cast, PointX >= 0 ? (PointX, PointY) : null);
        }
    }

    /// <summary>Enter.</summary>
    public bool Start => Any(Key.Enter) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
