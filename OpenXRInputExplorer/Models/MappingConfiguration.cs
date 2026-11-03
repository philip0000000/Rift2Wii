using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenXRInputExplorer.Models;

public sealed class MappingConfiguration
{
    public int Version { get; set; } = 1;
    public Dictionary<string, string> Bindings { get; set; } = DefaultBindings();
    public bool KeyboardEnabled { get; set; }
    public bool StickKeysEnabled { get; set; }
    public bool MouseEnabled { get; set; }
    public bool MouseClickEnabled { get; set; } = true;
    public double TriggerThreshold { get; set; } = 0.65;
    public double StickDeadzone { get; set; } = 0.15;
    public bool DsuEnabled { get; set; }
    public bool MotionEnabled { get; set; } = true;
    public int DsuPort { get; set; } = 26760;
    public double ScreenWidth { get; set; } = 1.6;
    public double ScreenDistance { get; set; } = 2;
    public double PointerYaw { get; set; } = 25;
    public double PointerPitch { get; set; } = 20;
    public double PointerVerticalOffset { get; set; }
    public bool DirectPointerEnabled { get; set; } = true;
    public string DolphinConfigDirectory { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public static Dictionary<string, string> DefaultBindings() => Enum.GetValues<ControllerControl>()
        .ToDictionary(control => control.ToString(), control => control switch
        {
            ControllerControl.RightTrigger => "A", ControllerControl.RightGrip => "B",
            ControllerControl.RightB => "1", ControllerControl.RightA => "2",
            ControllerControl.LeftY => "Q", ControllerControl.LeftX => "E",
            ControllerControl.LeftStickUp => "W", ControllerControl.LeftStickDown => "S",
            ControllerControl.LeftStickLeft => "A", ControllerControl.LeftStickRight => "D",
            ControllerControl.RightStickUp => "Up", ControllerControl.RightStickDown => "Down",
            ControllerControl.RightStickLeft => "Left", ControllerControl.RightStickRight => "Right",
            _ => "None"
        });
}
