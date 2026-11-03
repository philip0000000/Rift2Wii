using System.Text.Json;
using OpenXRInputExplorer.Models;
using OpenXRInputExplorer.Services;

internal static class ConfigurationChecks
{
    public static void Run()
    {
        static void Check(bool value, string name) { if (!value) throw new Exception(name); }
        string root = Path.Combine(AppContext.BaseDirectory, "OpenXR-ConfigurationChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "controller-mapping.json");
        string[] names = ["None", "A", "B", "C", "D", "E", "Q", "S", "W", "X", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "Up", "Down", "Left", "Right", "LeftCtrl", "Space"];
        var store = new MappingConfigurationStore(path, names);
        try
        {
            var first = store.LoadOrCreate();
            Check(first.CanSave && File.Exists(path), "Missing file creates defaults beside the selected executable path: " + first.Message);
            Check(first.Configuration.Bindings.Count == Enum.GetValues<ControllerControl>().Length &&
                first.Configuration.Bindings["RightA"] == "2" && first.Configuration.Bindings["RightB"] == "1" &&
                first.Configuration.Bindings["LeftX"] == "E" && first.Configuration.Bindings["LeftY"] == "Q" &&
                first.Configuration.Bindings["RightTrigger"] == "A" && first.Configuration.Bindings["RightGrip"] == "B" &&
                !first.Configuration.KeyboardEnabled, "Complete Wii default mappings and switches");
            using (var json = JsonDocument.Parse(File.ReadAllText(path)))
                Check(json.RootElement.GetProperty("Bindings").GetProperty("RightA").GetString() == "2", "Human-readable named keys");

            var configuration = first.Configuration;
            configuration.Bindings["LeftX"] = "LeftCtrl";
            configuration.KeyboardEnabled = true;
            configuration.MouseEnabled = true;
            configuration.MouseClickEnabled = false;
            configuration.StickKeysEnabled = true;
            configuration.DsuEnabled = true;
            configuration.MotionEnabled = false;
            configuration.DsuPort = 26761;
            configuration.TriggerThreshold = 0.8;
            configuration.StickDeadzone = 0.25;
            configuration.ScreenWidth = 2.4;
            configuration.ScreenDistance = 3.2;
            configuration.PointerPitch = 22.5;
            configuration.PointerYaw = 27;
            configuration.PointerVerticalOffset = -4;
            configuration.DirectPointerEnabled = false;
            configuration.DolphinConfigDirectory = @"C:\Dolphin\User\Config";
            store.Save(configuration);
            var second = new MappingConfigurationStore(path, names).LoadOrCreate().Configuration;
            Check(second.Bindings["LeftX"] == "LeftCtrl" && second.KeyboardEnabled && second.MouseEnabled &&
                !second.MouseClickEnabled && second.StickKeysEnabled && second.DsuEnabled && !second.MotionEnabled && second.DsuPort == 26761 &&
                second.TriggerThreshold == 0.8 && second.StickDeadzone == 0.25 && second.ScreenWidth == 2.4 && second.ScreenDistance == 3.2 &&
                second.PointerPitch == 22.5 && second.PointerYaw == 27 && second.PointerVerticalOffset == -4 && !second.DirectPointerEnabled &&
                second.DolphinConfigDirectory == @"C:\Dolphin\User\Config",
                "All settings survive a fresh store/startup");

            const string partial = """
                { // Hand-editing supports comments and trailing commas.
                  "Bindings": { "righta": "x", "LeftY": "UnknownKey", "FutureGrip": "Space", },
                  "KeyboardEnabled": true, "TriggerThreshold": 9, "DsuPort": 0,
                  "ScreenWidth": -1, "ScreenDistance": 4,
                  "PointerPitch": 999, "PointerYaw": -5, "PointerVerticalOffset": -999,
                  "FutureOption": { "keep": true }
                }
                """;
            File.WriteAllText(path, partial);
            var repaired = store.LoadOrCreate();
            Check(repaired.Configuration.Bindings["RightA"] == "X" && repaired.Configuration.Bindings["LeftY"] == "Q" &&
                repaired.Configuration.Bindings["LeftStickUp"] == "W", "Case-insensitive keys and defaults for invalid/missing mappings");
            Check(repaired.Configuration.TriggerThreshold == 0.65 && repaired.Configuration.DsuPort == 26760 &&
                repaired.Configuration.ScreenWidth == 1.6 && repaired.Configuration.ScreenDistance == 4 &&
                repaired.Configuration.KeyboardEnabled && repaired.Configuration.MotionEnabled, "Invalid settings repaired without discarding valid settings; older configs enable motion by default");
            Check(File.ReadAllText(path) == partial, "Loading valid JSON does not overwrite hand edits");
            Check(repaired.Configuration.PointerPitch == 20 && repaired.Configuration.PointerYaw == 25 &&
                repaired.Configuration.PointerVerticalOffset == 0, "Invalid pointer calibration repaired");
            store.Save(repaired.Configuration);
            var preserved = store.LoadOrCreate().Configuration;
            Check(preserved.Bindings["FutureGrip"] == "Space" && preserved.Extra!["FutureOption"].GetProperty("keep").GetBoolean(),
                "Unknown fields and future mappings survive saving");

            File.WriteAllText(path, "{bad json");
            var recovered = store.LoadOrCreate();
            string backup = Directory.GetFiles(root, "controller-mapping.json.invalid-*").Single();
            Check(recovered.CanSave && File.ReadAllText(backup) == "{bad json" && recovered.Configuration.Bindings["RightA"] == "2",
                "Malformed file backed up before creating defaults");

            const string future = "{\"Version\":999,\"Bindings\":{\"RightA\":\"X\"}}";
            File.WriteAllText(path, future);
            Check(!store.LoadOrCreate().CanSave && File.ReadAllText(path) == future, "Unsupported versions preserved without automatic overwrite");

            File.Delete(path);
            Directory.CreateDirectory(path);
            Check(!store.LoadOrCreate().CanSave && Directory.Exists(path), "Unwritable/conflicting path reports failure and remains intact");
            Directory.Delete(path);
            store.Save(new MappingConfiguration());
            if (OperatingSystem.IsWindows())
            {
                string before = File.ReadAllText(path);
                File.SetAttributes(path, FileAttributes.ReadOnly);
                try
                {
                    bool failed = false;
                    try { store.Save(second); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failed = true; }
                    Check(failed && File.ReadAllText(path) == before, "Failed replacement preserves the previous configuration");
                }
                finally { File.SetAttributes(path, FileAttributes.Normal); }
            }
            Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Temporary save files cleaned up");
            Console.WriteLine("PASS: configuration creation, restart round-trip, validation, future fields, corrupt-file backup and failed-save preservation.");
        }
        finally
        {
            string full = Path.GetFullPath(root);
            string temporaryRoot = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (Directory.GetParent(full)!.FullName.TrimEnd(Path.DirectorySeparatorChar) != temporaryRoot ||
                !Path.GetFileName(full).StartsWith("OpenXR-ConfigurationChecks-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test cleanup path.");
            Directory.Delete(full, true);
        }
    }
}
