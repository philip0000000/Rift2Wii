using System.Text;
using System.IO;
using System.Text.Json;
using OpenXRInputExplorer.Models;

namespace OpenXRInputExplorer.Services;

public sealed record ConfigurationLoad(MappingConfiguration Configuration, string Message, bool CanSave);

public sealed class MappingConfigurationStore(string path, IEnumerable<string> keyNames)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    private readonly Dictionary<string, string> _keyNames = keyNames.ToDictionary(name => name, StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true
    };

    public ConfigurationLoad LoadOrCreate()
    {
        try
        {
            if (!File.Exists(Path))
            {
                var defaults = new MappingConfiguration();
                Save(defaults);
                return new(defaults, $"Created default configuration: {Path}", true);
            }
            var configuration = JsonSerializer.Deserialize<MappingConfiguration>(File.ReadAllText(Path), JsonOptions)
                ?? throw new JsonException("The configuration must be a JSON object.");
            if (configuration.Version != 1)
                return new(new(), $"Unsupported configuration version {configuration.Version}. File preserved; automatic saving disabled: {Path}", false);
            bool repaired = Normalize(configuration);
            return new(configuration, $"Loaded configuration: {Path}" +
                (repaired ? ". Invalid values were replaced with defaults in memory; the file will update on your next settings change." : ""), true);
        }
        catch (JsonException error)
        {
            try
            {
                string backup = Path + $".invalid-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
                File.Copy(Path, backup);
                var defaults = new MappingConfiguration();
                Save(defaults);
                return new(defaults, $"Invalid configuration ({error.Message}). Original saved as {backup}; created defaults.", true);
            }
            catch (Exception recoveryError) when (recoveryError is IOException or UnauthorizedAccessException)
            {
                return new(new(), $"Could not recover configuration: {recoveryError.Message}. Original preserved; saving disabled.", false);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(new(), $"Could not read/create configuration: {error.Message}. Using defaults; saving disabled: {Path}", false);
        }
    }

    private bool Normalize(MappingConfiguration configuration)
    {
        bool repaired = false;
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (configuration.Bindings is not null)
            foreach (var entry in configuration.Bindings) bindings[entry.Key] = entry.Value;
        foreach (var entry in MappingConfiguration.DefaultBindings())
        {
            if (!bindings.TryGetValue(entry.Key, out string? value) || value is null || !_keyNames.TryGetValue(value, out string? name))
            { bindings[entry.Key] = entry.Value; repaired = true; }
            else bindings[entry.Key] = name;
        }
        configuration.Bindings = bindings;
        double Number(double value, double min, double max, double fallback)
        {
            if (double.IsFinite(value) && value >= min && value <= max) return value;
            repaired = true; return fallback;
        }
        configuration.TriggerThreshold = Number(configuration.TriggerThreshold, 0.2, 0.95, 0.65);
        configuration.StickDeadzone = Number(configuration.StickDeadzone, 0, 0.5, 0.15);
        configuration.ScreenWidth = Number(configuration.ScreenWidth, 0.5, 4, 1.6);
        configuration.ScreenDistance = Number(configuration.ScreenDistance, 0.5, 5, 2);
        configuration.PointerYaw = Number(configuration.PointerYaw, 5, 60, 25);
        configuration.PointerPitch = Number(configuration.PointerPitch, 5, 60, 20);
        configuration.PointerVerticalOffset = Number(configuration.PointerVerticalOffset, -50, 50, 0);
        if (configuration.DsuPort is < 1 or > 65535) { configuration.DsuPort = 26760; repaired = true; }
        return repaired;
    }

    public void Save(MappingConfiguration configuration)
    {
        string temporary = Path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(configuration, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporary, Path, true); // Replace only after the complete file is written in the same directory.
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
