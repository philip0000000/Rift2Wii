namespace OpenXRInputExplorer.Models;

public enum ControllerControl
{
    LeftX, LeftY, LeftStickClick, LeftTrigger, LeftGrip,
    LeftStickUp, LeftStickDown, LeftStickLeft, LeftStickRight,
    RightA, RightB, RightStickClick, RightTrigger, RightGrip,
    RightStickUp, RightStickDown, RightStickLeft, RightStickRight
}

public static class ControllerControls
{
    public static bool IsLeft(ControllerControl control) => control <= ControllerControl.LeftStickRight;
    public static bool IsStickDirection(ControllerControl control) => control is
        >= ControllerControl.LeftStickUp and <= ControllerControl.LeftStickRight or
        >= ControllerControl.RightStickUp and <= ControllerControl.RightStickRight;

    public static float GetValue(ControllerControl control, ControllerState left, ControllerState right) => control switch
    {
        ControllerControl.LeftX => left.X ? 1 : 0,
        ControllerControl.LeftY => left.Y ? 1 : 0,
        ControllerControl.LeftStickClick => left.ThumbstickClick ? 1 : 0,
        ControllerControl.LeftTrigger => left.Trigger,
        ControllerControl.LeftGrip => left.Grip,
        ControllerControl.LeftStickUp => left.ThumbstickY,
        ControllerControl.LeftStickDown => -left.ThumbstickY,
        ControllerControl.LeftStickLeft => -left.ThumbstickX,
        ControllerControl.LeftStickRight => left.ThumbstickX,
        ControllerControl.RightA => right.A ? 1 : 0,
        ControllerControl.RightB => right.B ? 1 : 0,
        ControllerControl.RightStickClick => right.ThumbstickClick ? 1 : 0,
        ControllerControl.RightTrigger => right.Trigger,
        ControllerControl.RightGrip => right.Grip,
        ControllerControl.RightStickUp => right.ThumbstickY,
        ControllerControl.RightStickDown => -right.ThumbstickY,
        ControllerControl.RightStickLeft => -right.ThumbstickX,
        ControllerControl.RightStickRight => right.ThumbstickX,
        _ => 0
    };
}
