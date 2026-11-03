using OpenXRInputExplorer.Models;

namespace OpenXRInputExplorer.Services;

public readonly record struct DesktopPoint(int X, int Y);

public interface IDesktopInputSink
{
    void Key(ushort virtualKey, bool down);
    void MoveMouse(DesktopPoint point);
    void LeftMouseButton(bool down);
}

/// <summary>Tracks held output and requires release before arming after interruption.</summary>
public sealed class DesktopInputService : IDisposable
{
    private readonly IDesktopInputSink _sink;
    private readonly HashSet<ushort> _heldKeys = [];
    private bool _mouseDown, _triggerArmed;
    private Dictionary<ControllerControl, ushort> _bindings = [];
    private readonly HashSet<ControllerControl> _armed = [];
    private readonly HashSet<ControllerControl> _pressed = [];
    private bool _stickKeysEnabled;
    private float _triggerThreshold = 0.65f;
    public bool KeyboardEnabled { get; private set; }
    public bool MouseEnabled { get; private set; }
    public bool MouseClickEnabled { get; private set; }

    public DesktopInputService(IDesktopInputSink sink) => _sink = sink;

    public void Configure(bool keyboardEnabled, bool mouseEnabled, bool mouseClickEnabled,
        IReadOnlyDictionary<ControllerControl, ushort> bindings, bool stickKeysEnabled, float triggerThreshold)
    {
        if (!float.IsFinite(triggerThreshold) || triggerThreshold is < 0.2f or > 0.95f)
            throw new ArgumentOutOfRangeException(nameof(triggerThreshold));
        KeyboardEnabled = MouseEnabled = MouseClickEnabled = false;
        ReleaseAll();
        KeyboardEnabled = keyboardEnabled;
        MouseEnabled = mouseEnabled;
        MouseClickEnabled = mouseClickEnabled;
        _bindings = new(bindings);
        _stickKeysEnabled = stickKeysEnabled;
        _triggerThreshold = triggerThreshold;
    }

    public bool WantsMouseFocus(float trigger) => MouseEnabled && MouseClickEnabled && _triggerArmed && !_mouseDown && trigger >= _triggerThreshold;

    public void Update(bool active, bool focused, ControllerState left, ControllerState right,
        bool leftAvailable, bool rightAvailable,
        DesktopPoint? mousePoint, bool unobstructed)
    {
        if (!active) { ReleaseAll(); return; }
        var desired = new HashSet<ushort>();
        foreach (var (control, key) in _bindings)
        {
            bool stickDirection = ControllerControls.IsStickDirection(control);
            bool available = ControllerControls.IsLeft(control) ? leftAvailable : rightAvailable;
            if (!KeyboardEnabled || !focused || !available || (stickDirection && !_stickKeysEnabled))
            {
                _armed.Remove(control);
                _pressed.Remove(control);
                continue;
            }
            float value = ControllerControls.GetValue(control, left, right);
            float on = stickDirection ? 0.55f : _triggerThreshold;
            float off = on - 0.15f;
            if (!float.IsFinite(value) || value <= off)
            { _pressed.Remove(control); _armed.Add(control); }
            else if (value >= on && _armed.Contains(control)) _pressed.Add(control);
            if (_pressed.Contains(control) && key != 0) desired.Add(key);
        }

        foreach (ushort key in _heldKeys.Except(desired).ToArray())
        {
            _sink.Key(key, false);
            _heldKeys.Remove(key);
        }
        foreach (ushort key in desired.Except(_heldKeys))
        {
            _sink.Key(key, true);
            _heldKeys.Add(key);
        }

        float trigger = right.Trigger;
        float releaseThreshold = _triggerThreshold - 0.15f;
        bool pointing = MouseEnabled && rightAvailable && focused && mousePoint.HasValue && unobstructed;
        if (pointing) _sink.MoveMouse(mousePoint!.Value);
        if (_mouseDown && (!pointing || !MouseClickEnabled || trigger <= releaseThreshold))
        {
            _sink.LeftMouseButton(false);
            _mouseDown = false;
        }
        if (MouseEnabled && MouseClickEnabled && rightAvailable && trigger <= releaseThreshold) _triggerArmed = true;
        if (pointing && MouseClickEnabled && _triggerArmed && !_mouseDown && trigger >= _triggerThreshold)
        {
            _sink.LeftMouseButton(true);
            _mouseDown = true;
            _triggerArmed = false;
        }
        if (!MouseEnabled || !MouseClickEnabled || !rightAvailable || (!pointing && trigger > releaseThreshold)) _triggerArmed = false;
    }

    public void ReleaseAll()
    {
        // Attempt every release even if one event fails; retain failures for a later retry.
        var errors = new List<Exception>();
        foreach (ushort key in _heldKeys.ToArray())
            try { _sink.Key(key, false); _heldKeys.Remove(key); } catch (Exception error) { errors.Add(error); }
        if (_mouseDown)
            try { _sink.LeftMouseButton(false); _mouseDown = false; } catch (Exception error) { errors.Add(error); }
        _armed.Clear();
        _pressed.Clear();
        _triggerArmed = false;
        if (errors.Count > 0) throw new AggregateException("Windows could not release all simulated input.", errors);
    }

    public void Dispose() => ReleaseAll();
}
