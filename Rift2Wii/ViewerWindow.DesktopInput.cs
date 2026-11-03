using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using OpenXRInputExplorer.Models;
using OpenXRInputExplorer.Services;

namespace OpenXRInputExplorer;

public partial class ViewerWindow
{
    private readonly DesktopInputService _desktopInput = new(new NativeDesktop());
    private readonly List<MappingRow> _keyMappings = [];
    private DsuServer? _dsuServer;
    private readonly ControllerMotionEstimator _leftMotion = new(), _rightMotion = new();
    private bool _desktopControlsReady;
    private bool _desktopInputSuspended;
    private sealed record KeyChoice(string Label, ushort Code)
    { public override string ToString() => Label; }

    private sealed class MappingRow(ControllerControl control, string label, List<KeyChoice> keys,
        ushort defaultKey, Action changed) : INotifyPropertyChanged
    {
        public ControllerControl Control { get; } = control;
        public string Label { get; } = label;
        public List<KeyChoice> Keys { get; } = keys;
        private KeyChoice _selectedKey = keys.Single(key => key.Code == defaultKey);
        public KeyChoice SelectedKey
        {
            get => _selectedKey;
            set
            {
                if (value is null || value == _selectedKey) return;
                _selectedKey = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedKey)));
                changed();
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private void InitializeDesktopControls()
    {
        var keys = new List<KeyChoice> { new("None", 0) };
        for (int code = 'A'; code <= 'Z'; code++) keys.Add(new(((char)code).ToString(), (ushort)code));
        for (int code = '0'; code <= '9'; code++) keys.Add(new(((char)code).ToString(), (ushort)code));
        foreach (Key key in new[] { Key.Space, Key.Enter, Key.Escape, Key.Tab, Key.Back, Key.Up, Key.Down, Key.Left, Key.Right,
            Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt })
            keys.Add(new(key.ToString(), (ushort)KeyInterop.VirtualKeyFromKey(key)));
        for (int index = 0; index < 12; index++) keys.Add(new($"F{index + 1}", (ushort)(0x70 + index)));

        var defaults = MappingConfiguration.DefaultBindings();
        foreach (var control in Enum.GetValues<ControllerControl>())
        {
            string label = control.ToString().Replace("Left", "").Replace("Right", "");
            label = control switch
            {
                ControllerControl.LeftStickLeft or ControllerControl.RightStickLeft => "Stick left",
                ControllerControl.LeftStickRight or ControllerControl.RightStickRight => "Stick right",
                _ => label.Replace("StickClick", "Stick click").Replace("StickUp", "Stick up").Replace("StickDown", "Stick down")
            };
            ushort defaultKey = keys.Single(key => key.Label == defaults[control.ToString()]).Code;
            _keyMappings.Add(new MappingRow(control, label, keys, defaultKey, ConfigureDesktopOutput));
        }
        LeftMappingsList.ItemsSource = _keyMappings.Where(row => ControllerControls.IsLeft(row.Control)).ToList();
        RightMappingsList.ItemsSource = _keyMappings.Where(row => !ControllerControls.IsLeft(row.Control)).ToList();
        _desktopControlsReady = true;
    }

    private void DesktopInputOptionsChanged(object sender, RoutedEventArgs e) => ConfigureDesktopOutput();
    private void MotionOutputChanged(object sender, RoutedEventArgs e)
    {
        if (!_desktopControlsReady) return;
        _leftMotion.Reset(); _rightMotion.Reset();
        QueueConfigurationSave();
    }
    private void ApplyWiiDefaultsClicked(object sender, RoutedEventArgs e)
    {
        var defaults = MappingConfiguration.DefaultBindings();
        _desktopControlsReady = false;
        try
        {
            foreach (var row in _keyMappings)
                row.SelectedKey = row.Keys.Single(key => key.Label == defaults[row.Control.ToString()]);
        }
        finally { _desktopControlsReady = true; }
        ConfigureDesktopOutput();
        AddStatus("Wii key defaults applied. Close Dolphin and click Configure Dolphin to update its profile.");
    }
    private void InputThresholdChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ConfigureDesktopOutput();

    private void ConfigureDesktopOutput()
    {
        if (!_desktopControlsReady) return;
        try
        {
            _desktopInput.Configure(KeyboardOutputCheckBox.IsChecked == true, MouseOutputCheckBox.IsChecked == true,
                MouseClickCheckBox.IsChecked == true, _keyMappings.ToDictionary(row => row.Control, row => row.SelectedKey.Code),
                StickKeysCheckBox.IsChecked == true, (float)TriggerThresholdSlider.Value);
            QueueConfigurationSave();
        }
        catch (Exception error) { DisableDesktopOutput(error); }
    }

    private void DsuOutputChanged(object sender, RoutedEventArgs e)
    {
        if (!_desktopControlsReady) return;
        StopDsu();
        if (DsuOutputCheckBox.IsChecked == true)
        {
            try
            {
                if (!int.TryParse(DsuPortTextBox.Text, out int port) || port is < 1 or > 65535)
                    throw new InvalidOperationException("Choose a DSU port between 1 and 65535.");
                _dsuServer = new DsuServer(port);
                DsuPortTextBox.IsEnabled = false;
                AddStatus($"Analog DSU output enabled on 127.0.0.1:{port}. Left slot 0, right slot 1.");
            }
            catch (Exception error)
            {
                DsuOutputCheckBox.IsChecked = false;
                DsuStatusText.Text = $"Could not start DSU: {error.Message}";
                AddStatus(DsuStatusText.Text);
            }
        }
        QueueConfigurationSave();
    }

    private void StopDsu()
    {
        _dsuServer?.Dispose();
        _dsuServer = null;
        DsuPortTextBox.IsEnabled = true;
        DsuStatusText.Text = "DSU off. Add this server in Dolphin's Alternate Input Sources for analog sticks/triggers.";
    }

    private void ReleaseDesktopOutput()
    {
        _leftMotion.Reset(); _rightMotion.Reset();
        _dsuServer?.Publish(DsuPadState.Neutral, DsuPadState.Neutral, (float)StickDeadzoneSlider.Value);
        try { _desktopInput.ReleaseAll(); }
        catch (Exception error) { AddStatus($"Input release failed: {error.Message}"); }
    }

    private void DisableDesktopOutput(Exception error)
    {
        _desktopControlsReady = false;
        KeyboardOutputCheckBox.IsChecked = false;
        MouseOutputCheckBox.IsChecked = false;
        _desktopControlsReady = true;
        ReleaseDesktopOutput();
        try { _desktopInput.Configure(false, false, false, new Dictionary<ControllerControl, ushort>(), false, 0.65f); }
        catch { /* Releases will be retried on the next update. */ }
        DesktopInputStatusText.Text = $"Output stopped: {error.Message}";
        AddStatus(DesktopInputStatusText.Text);
    }

    private DsuPadState PadState(ControllerState state, bool available, bool left)
    {
        var orientation = left ? null : _openXrService.RightAimOrientation;
        var motion = (left ? _leftMotion : _rightMotion).Update(state,
            available && MotionOutputCheckBox.IsChecked == true && (left || orientation.HasValue), orientation);
        return !available ? DsuPadState.Neutral :
        new(true, new Vector2(state.ThumbstickX, state.ThumbstickY), state.Trigger, state.Grip,
            left ? state.X : state.A, left ? state.Y : state.B, state.ThumbstickClick) { Motion = motion };
    }

    private void UpdateDesktopOutput()
    {
        try
        {
            nint target = _captureService.WindowHandle;
            var left = _openXrService.GetLeftController();
            var right = _openXrService.GetRightController();
            bool active = !_desktopInputSuspended && _captureService.LatestTexture is not null &&
                _openXrService.ControllerActionsAvailable && _openXrService.IsScreenPlacementReady && NativeDesktop.IsTargetReady(target);
            bool leftAvailable = active && left.IsTracked, rightAvailable = active && right.IsTracked;
            _dsuServer?.Publish(PadState(left, leftAvailable, true), PadState(right, rightAvailable, false), (float)StickDeadzoneSlider.Value);
            if (_dsuServer is not null) DsuStatusText.Text = _dsuServer.Status +
                (MotionOutputCheckBox.IsChecked == true ? " Tracked motion on." : " Motion off (stationary sensors); buttons and sticks remain active.");
            DesktopPoint? point = null;
            if (active && _openXrService.GetRightScreenPoint() is { } uv)
            {
                var size = _captureService.LatestTexture!.Description;
                if (NativeDesktop.TryMapPoint(target, uv, size.Width, size.Height, out var mapped)) point = mapped;
            }
            if (rightAvailable && point.HasValue && _desktopInput.WantsMouseFocus(right.Trigger) && !NativeDesktop.IsFocused(target))
                NativeDesktop.SetForegroundWindow(target);
            bool focused = NativeDesktop.IsFocused(target);
            _desktopInput.Update(active, focused, left, right, leftAvailable, rightAvailable, point,
                point.HasValue && NativeDesktop.IsUnobstructed(target, point.Value));
            DesktopInputStatusText.Text = !_desktopInput.KeyboardEnabled && !_desktopInput.MouseEnabled
                ? "Keyboard/mouse output off. DSU is enabled separately."
                : !active ? "Waiting for a restored capture window and active VR tracking."
                : !focused ? "Focus the captured window to use keys. Mouse-enabled trigger clicks can focus it."
                : "Output ready. Both hands use their chosen keys; mouse aim uses the right controller.";
        }
        catch (Exception error) { DisableDesktopOutput(error); }
    }
}
