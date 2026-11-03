using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using OpenXRInputExplorer.Services;

namespace OpenXRInputExplorer;

public partial class ViewerWindow
{
    private void DirectPointerChanged(object sender, RoutedEventArgs e)
    {
        if (!_configurationReady || _loadingConfiguration) return;
        PointerSettingsStatusText.Text = "Pointing mode changed. Close Dolphin and click Apply pointer settings.";
        QueueConfigurationSave();
    }
    private void PointerValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_configurationReady || _loadingConfiguration) return;
        PointerSettingsStatusText.Text = "Changed in the viewer. Close Dolphin and click Apply pointer settings to use these values.";
        QueueConfigurationSave();
    }

    private void ResetPointerSettingsClicked(object sender, RoutedEventArgs e)
    {
        DirectPointerCheckBox.IsChecked = true;
        PointerYawSlider.Value = 25;
        PointerPitchSlider.Value = 20;
        PointerOffsetSlider.Value = 0;
        PointerSettingsStatusText.Text = "Defaults selected. Close Dolphin and click Apply pointer settings.";
        QueueConfigurationSave();
    }

    private void ApplyPointerSettingsClicked(object sender, RoutedEventArgs e)
    {
        _desktopInputSuspended = true;
        ReleaseDesktopOutput();
        try
        {
            if (DolphinIsRunning()) throw new InvalidOperationException("Close Dolphin, then click Apply pointer settings. No Dolphin settings were changed.");
            string? directory = DetectDolphinConfig() ?? PickDolphinConfig();
            if (directory is null) return;
            var configuration = CaptureConfiguration();
            var result = DolphinSetupService.Apply(DolphinSetupService.PreparePointerCalibration(directory, configuration), DolphinIsRunning);
            _configuration.DolphinConfigDirectory = directory;
            QueueConfigurationSave(); SaveConfiguration();
            PointerSettingsStatusText.Text = "Pointer settings applied. Restart Dolphin to test the screen edges. Buttons and motion mappings were preserved.";
            if (configuration.DirectPointerEnabled)
                PointerSettingsStatusText.Text = "Direct mouse-to-IR pointing applied. Restart Dolphin. Point (Passthrough) now follows the mouse; buttons and motion mappings were preserved.";
            PointerSettingsStatusText.ToolTip = $"Backup: {result.BackupDirectory}";
            if (!_configurationCanSave || _configurationDirty)
                PointerSettingsStatusText.Text += " Viewer settings could not be saved; check the configuration status.";
            AddStatus($"Dolphin pointer calibrated. Backup: {result.BackupDirectory}");
        }
        catch (Exception error)
        {
            PointerSettingsStatusText.Text = error.Message;
            AddStatus(error.Message);
        }
        finally { _desktopInputSuspended = false; }
    }

    private string? _dolphinExecutableHint;

    private bool DolphinIsRunning()
    {
        bool running = false;
        foreach (string name in new[] { "Dolphin", "DolphinQt" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process)
                {
                    running = true;
                    try { _dolphinExecutableHint = process.MainModule?.FileName ?? _dolphinExecutableHint; }
                    catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                }
        return running;
    }

    private string? DetectDolphinConfig()
    {
        var saved = DolphinSetupService.NormalizeConfigDirectory(_configuration.DolphinConfigDirectory);
        if (saved is not null) return saved;
        using var registry = Registry.CurrentUser.OpenSubKey(@"Software\Dolphin Emulator");
        bool local = registry?.GetValue("LocalUserConfig") is int value && value != 0;
        if (_dolphinExecutableHint is not null)
        {
            string executableDirectory = Path.GetDirectoryName(_dolphinExecutableHint)!;
            if (local || File.Exists(Path.Combine(executableDirectory, "portable.txt")))
                return DolphinSetupService.NormalizeConfigDirectory(Path.Combine(executableDirectory, "User"));
        }
        if (local) return null; // Do not guess a roaming folder for a known portable installation.
        if (registry?.GetValue("UserConfigPath") is string custom && !string.IsNullOrWhiteSpace(custom))
            return DolphinSetupService.NormalizeConfigDirectory(custom);
        string documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Dolphin Emulator");
        if (Directory.Exists(documents)) return DolphinSetupService.NormalizeConfigDirectory(documents);
        return DolphinSetupService.NormalizeConfigDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dolphin Emulator"));
    }

    private string? PickDolphinConfig()
    {
        var picker = new OpenFolderDialog
        {
            Title = "Choose Dolphin's User or Config folder (Dolphin: File → Open User Folder)",
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return null;
        return DolphinSetupService.NormalizeConfigDirectory(picker.FolderName)
            ?? throw new InvalidOperationException("This folder has no Dolphin configuration. Choose the User or Config folder, or start Dolphin once and close it first.");
    }

    private void ChooseDolphinFolderClicked(object sender, RoutedEventArgs e)
    {
        _desktopInputSuspended = true;
        ReleaseDesktopOutput();
        try
        {
            string? folder = PickDolphinConfig();
            if (folder is null) return;
            _configuration.DolphinConfigDirectory = folder;
            QueueConfigurationSave(); SaveConfiguration();
            DolphinSetupStatusText.Text = $"Selected: {folder}. Close Dolphin and click Configure Dolphin to apply.";
        }
        catch (Exception error) { DolphinSetupStatusText.Text = error.Message; AddStatus(error.Message); }
        finally { _desktopInputSuspended = false; }
    }

    private void ConfigureDolphinClicked(object sender, RoutedEventArgs e)
    {
        _desktopInputSuspended = true;
        ReleaseDesktopOutput();
        bool dsuWasEnabled = DsuOutputCheckBox.IsChecked == true;
        bool applied = false;
        try
        {
            if (DolphinIsRunning())
                throw new InvalidOperationException("Close Dolphin, then click Configure Dolphin again. No Dolphin settings were changed.");
            string? directory = DetectDolphinConfig() ?? PickDolphinConfig();
            if (directory is null) return;
            var configuration = CaptureConfiguration();
            var plan = DolphinSetupService.Prepare(directory, configuration);
            if (DsuOutputCheckBox.IsChecked != true) DsuOutputCheckBox.IsChecked = true;
            else if (_dsuServer?.IsRunning != true) DsuOutputChanged(this, new RoutedEventArgs());
            if (_dsuServer?.IsRunning != true)
                throw new InvalidOperationException("Could not start the DSU server. Check its port/status and retry. No Dolphin settings were changed.");
            var result = DolphinSetupService.Apply(plan, DolphinIsRunning);
            applied = true;
            KeyboardOutputCheckBox.IsChecked = true;
            StickKeysCheckBox.IsChecked = false; // Nunchuk and D-pad now use analog DSU axes.
            MouseOutputCheckBox.IsChecked = true;
            MouseClickCheckBox.IsChecked = true; // Wii A no longer uses Click 0, so desktop clicks do not duplicate it.
            _configuration.DolphinConfigDirectory = directory;
            QueueConfigurationSave(); SaveConfiguration();
            DolphinSetupStatusText.Text = "Configured Wii Remote 1 + Nunchuk. Start Dolphin, choose its game window, and play. Right-hand Wii motion and left-hand Nunchuk motion are configured.";
            PointerSettingsStatusText.Text = "Pointer settings applied by Configure Dolphin. Restart Dolphin to test the screen edges.";
            if (!_configurationCanSave || _configurationDirty)
                DolphinSetupStatusText.Text += " App settings were not saved; check the configuration status before restarting.";
            DolphinSetupStatusText.ToolTip = $"Profile: {result.ProfilePath}{Environment.NewLine}Backup: {result.BackupDirectory}";
            AddStatus($"Dolphin configured: {directory}. Backup: {result.BackupDirectory}");
        }
        catch (Exception error)
        {
            DolphinSetupStatusText.Text = error is AggregateException aggregate
                ? aggregate.Message + " " + aggregate.InnerExceptions[0].Message : error.Message;
            AddStatus(DolphinSetupStatusText.Text);
        }
        finally
        {
            if (!applied && !dsuWasEnabled && DsuOutputCheckBox.IsChecked == true) DsuOutputCheckBox.IsChecked = false;
            _desktopInputSuspended = false;
        }
    }
}
