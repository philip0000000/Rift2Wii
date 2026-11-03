using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenXRInputExplorer.Models;
using OpenXRInputExplorer.Services;

namespace OpenXRInputExplorer;

public partial class ViewerWindow
{
    private MappingConfigurationStore? _configurationStore;
    private MappingConfiguration _configuration = new();
    private DispatcherTimer? _configurationSaveTimer;
    private bool _loadingConfiguration, _configurationReady, _configurationCanSave;
    private bool _configurationDirty;

    private void InitializeConfiguration()
    {
        _configurationStore = new MappingConfigurationStore(Path.Combine(AppContext.BaseDirectory, "controller-mapping.json"),
            _keyMappings[0].Keys.Select(key => key.Label));
        var loaded = _configurationStore.LoadOrCreate();
        _configuration = loaded.Configuration;
        _configurationCanSave = loaded.CanSave;
        _loadingConfiguration = true;
        _desktopControlsReady = false;
        try
        {
            foreach (var row in _keyMappings)
                row.SelectedKey = row.Keys.Single(key => key.Label == _configuration.Bindings[row.Control.ToString()]);
            KeyboardOutputCheckBox.IsChecked = _configuration.KeyboardEnabled;
            StickKeysCheckBox.IsChecked = _configuration.StickKeysEnabled;
            MouseOutputCheckBox.IsChecked = _configuration.MouseEnabled;
            MouseClickCheckBox.IsChecked = _configuration.MouseClickEnabled;
            TriggerThresholdSlider.Value = _configuration.TriggerThreshold;
            StickDeadzoneSlider.Value = _configuration.StickDeadzone;
            DsuPortTextBox.Text = _configuration.DsuPort.ToString();
            DsuOutputCheckBox.IsChecked = _configuration.DsuEnabled;
            MotionOutputCheckBox.IsChecked = _configuration.MotionEnabled;
            ScreenWidthSlider.Value = _configuration.ScreenWidth;
            ScreenDistanceSlider.Value = _configuration.ScreenDistance;
            PointerYawSlider.Value = _configuration.PointerYaw;
            PointerPitchSlider.Value = _configuration.PointerPitch;
            PointerOffsetSlider.Value = _configuration.PointerVerticalOffset;
            DirectPointerCheckBox.IsChecked = _configuration.DirectPointerEnabled;
        }
        finally { _desktopControlsReady = true; _loadingConfiguration = false; }
        _configurationReady = true;
        _configurationSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _configurationSaveTimer.Tick += (_, _) => SaveConfiguration();
        AddStatus(loaded.Message);
        ConfigurationStatusText.Text = loaded.Message;
        ConfigurationStatusText.ToolTip = _configurationStore.Path;
    }

    private void RestoreConfiguredOutput()
    {
        _loadingConfiguration = true;
        try
        {
            ConfigureDesktopOutput();
            if (DsuOutputCheckBox.IsChecked == true) DsuOutputChanged(this, new RoutedEventArgs());
        }
        finally { _loadingConfiguration = false; }
    }

    private void ConfigurationValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => QueueConfigurationSave();
    private void ConfigurationTextChanged(object sender, TextChangedEventArgs e) => QueueConfigurationSave();

    private void QueueConfigurationSave()
    {
        if (!_configurationReady || _loadingConfiguration || !_configurationCanSave) return;
        _configurationDirty = true;
        ConfigurationStatusText.Text = "Configuration changed — saving…";
        _configurationSaveTimer!.Stop();
        _configurationSaveTimer.Start();
    }

    private void SaveConfiguration()
    {
        _configurationSaveTimer?.Stop();
        if (!_configurationReady || !_configurationCanSave || !_configurationDirty || _configurationStore is null) return;
        CaptureConfiguration();
        try
        {
            _configurationStore.Save(_configuration);
            _configurationDirty = false;
            ConfigurationStatusText.Text = $"Configuration saved: {_configurationStore.Path}";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ConfigurationStatusText.Text = $"Configuration could not be saved: {error.Message}";
            AddStatus(ConfigurationStatusText.Text);
        }
    }

    private MappingConfiguration CaptureConfiguration()
    {
        foreach (var row in _keyMappings) _configuration.Bindings[row.Control.ToString()] = row.SelectedKey.Label;
        _configuration.KeyboardEnabled = KeyboardOutputCheckBox.IsChecked == true;
        _configuration.StickKeysEnabled = StickKeysCheckBox.IsChecked == true;
        _configuration.MouseEnabled = MouseOutputCheckBox.IsChecked == true;
        _configuration.MouseClickEnabled = MouseClickCheckBox.IsChecked == true;
        _configuration.TriggerThreshold = TriggerThresholdSlider.Value;
        _configuration.StickDeadzone = StickDeadzoneSlider.Value;
        _configuration.DsuEnabled = DsuOutputCheckBox.IsChecked == true;
        _configuration.MotionEnabled = MotionOutputCheckBox.IsChecked == true;
        if (int.TryParse(DsuPortTextBox.Text, out int port) && port is >= 1 and <= 65535) _configuration.DsuPort = port;
        _configuration.ScreenWidth = ScreenWidthSlider.Value;
        _configuration.ScreenDistance = ScreenDistanceSlider.Value;
        _configuration.PointerYaw = PointerYawSlider.Value;
        _configuration.PointerPitch = PointerPitchSlider.Value;
        _configuration.PointerVerticalOffset = PointerOffsetSlider.Value;
        _configuration.DirectPointerEnabled = DirectPointerCheckBox.IsChecked == true;
        return _configuration;
    }
}
