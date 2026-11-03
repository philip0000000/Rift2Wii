using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using OpenXRInputExplorer.Models;

namespace OpenXRInputExplorer.Services;

public sealed record DolphinSetupFile(string Path, byte[]? Before, byte[] After);
public sealed record DolphinSetupPlan(string ConfigDirectory, IReadOnlyList<DolphinSetupFile> Files);
public sealed record DolphinSetupResult(string BackupDirectory, string ProfilePath);

/// <summary>Generates and installs an offline Dolphin profile. Never starts/stops Dolphin.</summary>
public static class DolphinSetupService
{
    public const string ServerName = "Rift2Wii";
    public const string ProfileName = "Rift2Wii";
    private const string LegacyName = "OpenXR Input Explorer";
    public const string KeyboardDevice = "DInput/0/Keyboard Mouse";

    public static Dictionary<string, string> PointerSettings(MappingConfiguration configuration)
    {
        static string Number(double value, double min, double max)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                throw new InvalidOperationException("Pointer calibration is outside the supported range.");
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
        var values = new Dictionary<string, string>
        {
            ["IR/Total Yaw"] = Number(configuration.PointerYaw, 5, 60),
            ["IR/Total Pitch"] = Number(configuration.PointerPitch, 5, 60),
            ["IR/Vertical Offset"] = Number(configuration.PointerVerticalOffset, -50, 50),
            ["IR/Dead Zone"] = "0",
            // A full square mouse range; discard any hand-recorded stick calibration.
            ["IR/Calibration"] = "100 141.42 100 141.42 100 141.42 100 141.42"
        };
        values["IMUIR/Enabled"] = "False";
        values["IRPassthrough/Enabled"] = configuration.DirectPointerEnabled ? "True" : "False";
        // Dolphin exposes each signed mouse axis as two non-negative half axes.
        // Mirror X for the camera image; Y follows the mouse's top-to-bottom direction.
        // Two synthetic sensor-bar dots bypass EmulatePoint's rotation/FOV conversion.
        string x = $"(`{KeyboardDevice}:Cursor X+` - `{KeyboardDevice}:Cursor X-`)";
        string y = $"(`{KeyboardDevice}:Cursor Y+` - `{KeyboardDevice}:Cursor Y-`)";
        string spanX = (configuration.PointerYaw / 25 * 0.4).ToString("0.####", CultureInfo.InvariantCulture);
        string spanY = (configuration.PointerPitch / 20 * 0.4).ToString("0.####", CultureInfo.InvariantCulture);
        string offset = (configuration.PointerVerticalOffset / 100).ToString("0.####", CultureInfo.InvariantCulture);
        for (int i = 1; i <= 4; i++)
        {
            bool enabled = configuration.DirectPointerEnabled && i <= 2;
            values[$"IRPassthrough/Object {i} X"] = enabled
                ? $"(0.5 - {x} * {spanX} {(i == 1 ? "-" : "+")} 0.04)" : "";
            values[$"IRPassthrough/Object {i} Y"] = enabled ? $"(0.5 + {y} * {spanY} + ({offset}))" : "";
            values[$"IRPassthrough/Object {i} Size"] = enabled ? "0.2" : "";
        }
        return values;
    }

    public static DolphinSetupPlan PreparePointerCalibration(string directory, MappingConfiguration configuration)
    {
        string config = NormalizeConfigDirectory(directory) ?? throw new InvalidOperationException("Choose Dolphin's existing User or Config folder.");
        string active = Path.Combine(config, "WiimoteNew.ini");
        if (!File.Exists(active)) throw new InvalidOperationException("Run Configure Dolphin once before applying pointer settings.");
        var settings = PointerSettings(configuration);
        var files = new List<DolphinSetupFile>();
        foreach (var (path, section) in new[] { (active, "Wiimote1"),
            (Path.Combine(config, "Profiles", "Wiimote", LegacyName + ".ini"), "Profile"),
            (Path.Combine(config, "Profiles", "Wiimote", ProfileName + ".ini"), "Profile") })
        {
            // Do not create a partial reusable profile if this installation has none.
            if (!File.Exists(path)) continue;
            byte[] before = File.ReadAllBytes(path);
            using var reader = new StreamReader(new MemoryStream(before), Encoding.UTF8, true);
            var ini = new Ini(reader.ReadToEnd());
            foreach (var (key, value) in settings) ini.Set(section, key, value);
            files.Add(new(path, before, new UTF8Encoding(false).GetBytes(ini.ToString())));
        }
        return new(config, files);
    }

    public static string? NormalizeConfigDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        string full = Path.GetFullPath(directory);
        if (Directory.Exists(Path.Combine(full, "Config"))) full = Path.Combine(full, "Config");
        return Directory.Exists(full) && new[] { "Dolphin.ini", "WiimoteNew.ini", "DSUClient.ini" }
            .Any(file => File.Exists(Path.Combine(full, file))) ? full : null;
    }

    public static Dictionary<string, string> Profile(MappingConfiguration configuration)
    {
        static string Dsu(int hand, string input) => $"`DSUClient/{hand}/{ServerName}:{input}`";
        string Button(ControllerControl control, int hand, string fallback, bool trigger = false)
        {
            if (configuration.Bindings.TryGetValue(control.ToString(), out var key) && key != "None")
                return $"`{KeyboardDevice}:{DolphinKey(key)}`";
            string expression = Dsu(hand, fallback);
            // Dolphin rounds button values at 0.5. Scaling places that boundary at our trigger threshold.
            return trigger ? $"({expression} / {(2 * configuration.TriggerThreshold).ToString("0.####", CultureInfo.InvariantCulture)})" : expression;
        }
        var values = new Dictionary<string, string>
        {
            ["Device"] = KeyboardDevice,
            ["Buttons/A"] = Button(ControllerControl.RightTrigger, 1, "L2", true),
            ["Buttons/B"] = Button(ControllerControl.RightGrip, 1, "L1", true),
            ["Buttons/1"] = Button(ControllerControl.RightB, 1, "Circle"),
            ["Buttons/2"] = Button(ControllerControl.RightA, 1, "Cross"),
            ["Buttons/-"] = Button(ControllerControl.LeftY, 0, "Circle"),
            ["Buttons/+"] = Button(ControllerControl.LeftX, 0, "Cross"),
            ["Buttons/Home"] = "",
            ["Extension"] = "Nunchuk",
            ["Extension/Attach MotionPlus"] = "True",
            ["Nunchuk/Buttons/C"] = Button(ControllerControl.LeftGrip, 0, "L1", true),
            ["Nunchuk/Buttons/Z"] = Button(ControllerControl.LeftTrigger, 0, "L2", true),
            ["IMUIR/Enabled"] = "False",
            ["IRPassthrough/Enabled"] = "False",
            ["IR/Relative Input"] = "False",
            ["IR/Auto-Hide"] = "False",
            ["IR/Vertical Offset"] = "0",
            ["Options/Sideways Wiimote"] = "False",
            ["Options/Upright Wiimote"] = "False"
        };
        foreach (var (direction, axis) in new[] { ("Up", "Left Y-"), ("Down", "Left Y+"), ("Left", "Left X-"), ("Right", "Left X+") })
        {
            values[$"Nunchuk/Stick/{direction}"] = Dsu(0, axis);
            values[$"D-Pad/{direction}"] = Dsu(1, axis);
        }
        foreach (var (direction, axis) in new[] { ("Up", "Cursor Y-"), ("Down", "Cursor Y+"), ("Left", "Cursor X-"), ("Right", "Cursor X+") })
            values[$"IR/{direction}"] = $"`{KeyboardDevice}:{axis}`";
        foreach (string prefix in new[] { "", "Nunchuk/" })
        {
            foreach (string axis in new[] { "X", "Y", "Z" }) values[$"{prefix}Shake/{axis}"] = "";
            foreach (string direction in new[] { "Forward", "Backward", "Left", "Right" }) values[$"{prefix}Tilt/{direction}"] = "";
            foreach (string direction in new[] { "Up", "Down", "Left", "Right", "Forward", "Backward" })
            {
                values[$"{prefix}Swing/{direction}"] = "";
                values[$"{prefix}IMUAccelerometer/{direction}"] = Dsu(prefix.Length == 0 ? 1 : 0, $"Accel {direction}");
            }
        }
        foreach (string direction in new[] { "Pitch Up", "Pitch Down", "Roll Left", "Roll Right", "Yaw Left", "Yaw Right" })
            values[$"IMUGyroscope/{direction}"] = Dsu(1, $"Gyro {direction}");
        values["IMUGyroscope/Calibration Period"] = "0";
        foreach (var (key, value) in PointerSettings(configuration)) values[key] = value;
        return values;
    }

    public static string DolphinKey(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) return key.ToUpperInvariant();
        if (key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out int number) && number is >= 1 and <= 12) return key;
        return key switch
        {
            "Enter" or "Return" => "RETURN", "Space" => "SPACE", "Back" => "BACK", "Escape" => "ESCAPE", "Tab" => "TAB",
            "Up" => "UP", "Down" => "DOWN", "Left" => "LEFT", "Right" => "RIGHT",
            "LeftCtrl" => "LCONTROL", "RightCtrl" => "RCONTROL", "LeftShift" => "LSHIFT", "RightShift" => "RSHIFT",
            "LeftAlt" => "LMENU", "RightAlt" => "RMENU",
            _ => throw new InvalidOperationException($"Unsupported Dolphin keyboard key: {key}")
        };
    }

    public static DolphinSetupPlan Prepare(string directory, MappingConfiguration configuration)
    {
        string config = NormalizeConfigDirectory(directory) ?? throw new InvalidOperationException("Choose Dolphin's existing User or Config folder. Start Dolphin once first if it has no configuration yet.");
        if (configuration.DsuPort is < 1 or > 65534) throw new InvalidOperationException("Dolphin's DSU client requires a port between 1 and 65534.");
        if (!double.IsFinite(configuration.TriggerThreshold) || configuration.TriggerThreshold is < 0.2 or > 0.95)
            throw new InvalidOperationException("Invalid trigger threshold.");
        var profile = Profile(configuration);
        var files = new List<DolphinSetupFile>();
        void Change(string relative, Func<Ini, string> update)
        {
            string path = Path.Combine(config, relative);
            byte[]? before = File.Exists(path) ? File.ReadAllBytes(path) : null;
            string original = "";
            if (before is not null)
            {
                using var reader = new StreamReader(new MemoryStream(before), Encoding.UTF8, true);
                original = reader.ReadToEnd();
            }
            files.Add(new(path, before, new UTF8Encoding(false).GetBytes(update(new Ini(original)))));
        }
        Change("WiimoteNew.ini", ini =>
        {
            var active = new Dictionary<string, string>(profile) { ["Source"] = "1" };
            ini.ReplaceSection("Wiimote1", active);
            return ini.ToString();
        });
        Change("DSUClient.ini", ini =>
        {
            string entries = ini.Get("Server", "Entries") ?? "";
            var retained = entries.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(entry => !entry.StartsWith(ServerName + ":", StringComparison.Ordinal) &&
                    !entry.StartsWith(LegacyName + ":", StringComparison.Ordinal));
            ini.Set("Server", "Entries", string.Join(';', retained.Append($"{ServerName}:127.0.0.1:{configuration.DsuPort}")) + ";");
            ini.Set("Server", "Enabled", "True");
            return ini.ToString();
        });
        Change("Dolphin.ini", ini =>
        {
            ini.Set("Input", "BackgroundInput", "True");
            ini.Set("BluetoothPassthrough", "Enabled", "False");
            return ini.ToString();
        });
        Change(Path.Combine("Profiles", "Wiimote", ProfileName + ".ini"), ini =>
        {
            ini.ReplaceSection("Profile", profile);
            return ini.ToString();
        });
        return new(config, files);
    }

    public static DolphinSetupResult Apply(DolphinSetupPlan plan, Func<bool> dolphinRunning,
        Action<string, byte[]>? write = null)
    {
        if (dolphinRunning()) throw new InvalidOperationException("Close Dolphin, then click Configure Dolphin again. It must be closed while settings are written.");
        bool Matches(DolphinSetupFile file, byte[]? expected) => expected is null ? !File.Exists(file.Path) :
            File.Exists(file.Path) && File.ReadAllBytes(file.Path).AsSpan().SequenceEqual(expected);
        foreach (var file in plan.Files)
        {
            string relative = Path.GetRelativePath(plan.ConfigDirectory, Path.GetFullPath(file.Path));
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new InvalidOperationException("Setup file is outside the selected Dolphin configuration folder.");
            if (!Matches(file, file.Before)) throw new IOException("Dolphin settings changed while preparing setup. Please retry.");
        }
        string backup = Path.Combine(plan.ConfigDirectory, "Rift2Wii Backups", $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backup);
        foreach (var file in plan.Files)
            if (file.Before is not null)
            {
                string path = Path.Combine(backup, Path.GetRelativePath(plan.ConfigDirectory, file.Path));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, file.Before);
            }
        File.WriteAllText(Path.Combine(backup, "manifest.json"), JsonSerializer.Serialize(plan.Files.Select(file =>
            new { File = Path.GetRelativePath(plan.ConfigDirectory, file.Path), Existed = file.Before is not null }), new JsonSerializerOptions { WriteIndented = true }));
        var written = new List<DolphinSetupFile>();
        try
        {
            foreach (var file in plan.Files)
            {
                if (dolphinRunning()) throw new IOException("Dolphin started during setup.");
                if (!Matches(file, file.Before)) throw new IOException("Dolphin configuration changed during setup.");
                (write ?? AtomicWrite)(file.Path, file.After);
                written.Add(file);
            }
        }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            foreach (var file in written.AsEnumerable().Reverse())
            {
                try
                {
                    if (!Matches(file, file.After)) throw new IOException($"File changed externally; retained it: {file.Path}");
                    if (file.Before is null) File.Delete(file.Path);
                    else AtomicWrite(file.Path, file.Before);
                }
                catch (Exception restoreError) { errors.Add(restoreError); }
            }
            throw new AggregateException($"Dolphin setup failed. Backup: {backup}", errors);
        }
        return new(backup, plan.Files[^1].Path);
    }

    private static void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed class Ini
    {
        private readonly List<string> _lines;
        public Ini(string text) => _lines = text.Replace("\r\n", "\n").TrimStart('\uFEFF').Split('\n').ToList();
        private static string? Section(string line) => line.Trim() is var value && value.StartsWith('[') && value.EndsWith(']') ? value[1..^1] : null;
        public string? Get(string section, string key)
        {
            string? current = null;
            foreach (string line in _lines)
            {
                if (Section(line) is { } found) { current = found; continue; }
                int equal = line.IndexOf('=');
                if (current == section && equal > 0 && line[..equal].Trim() == key) return line[(equal + 1)..].Trim();
            }
            return null;
        }
        public void Set(string section, string key, string value)
        {
            string? current = null; int insert = -1;
            for (int i = 0; i < _lines.Count; i++)
            {
                if (Section(_lines[i]) is { } found)
                {
                    if (current == section && insert < 0) insert = i;
                    current = found;
                    continue;
                }
                int equal = _lines[i].IndexOf('=');
                if (current == section && equal > 0 && _lines[i][..equal].Trim() == key)
                { _lines[i] = $"{key} = {value}"; return; }
            }
            if (current == section) _lines.Add($"{key} = {value}");
            else if (insert >= 0) _lines.Insert(insert, $"{key} = {value}");
            else { _lines.Add($"[{section}]"); _lines.Add($"{key} = {value}"); }
        }
        public void ReplaceSection(string section, Dictionary<string, string> values)
        {
            bool skip = false;
            var retained = new List<string>();
            foreach (string line in _lines)
            {
                if (Section(line) is { } found) skip = found == section;
                if (!skip) retained.Add(line);
            }
            _lines.Clear(); _lines.AddRange(retained);
            _lines.Add($"[{section}]");
            _lines.AddRange(values.Select(pair => $"{pair.Key} = {pair.Value}"));
        }
        public override string ToString() => string.Join(Environment.NewLine, _lines).TrimEnd() + Environment.NewLine;
    }
}
