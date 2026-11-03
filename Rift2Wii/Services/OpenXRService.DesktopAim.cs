using OpenXRInputExplorer.Helpers;
using Silk.NET.OpenXR;
using System.Numerics;

namespace OpenXRInputExplorer.Services;

public unsafe partial class OpenXRService
{
    private Silk.NET.OpenXR.Action _rightAimAction;
    private Space _rightAimSpace;
    private Posef _rightAimPose;
    public bool RightInputAvailable { get; private set; }
    public Quaternion? RightAimOrientation => RightInputAvailable
        ? new Quaternion(_rightAimPose.Orientation.X, _rightAimPose.Orientation.Y,
            _rightAimPose.Orientation.Z, _rightAimPose.Orientation.W) : null;

    private void CreateRightAimAction()
    {
        ulong hand = _rightHandPath;
        var info = new ActionCreateInfo
        {
            Type = StructureType.ActionCreateInfo, ActionType = ActionType.PoseInput,
            CountSubactionPaths = 1, SubactionPaths = &hand
        };
        WriteFixedString(info.ActionName, "right_desktop_aim");
        WriteFixedString(info.LocalizedActionName, "Right desktop aim");
        fixed (Silk.NET.OpenXR.Action* action = &_rightAimAction)
            CheckXr(_xr.CreateAction(_actionSet, &info, action), "xrCreateAction(right aim)");
    }

    private void CreateRightAimSpace()
    {
        var info = new ActionSpaceCreateInfo
        {
            Type = StructureType.ActionSpaceCreateInfo, Action = _rightAimAction,
            SubactionPath = _rightHandPath,
            PoseInActionSpace = new Posef { Orientation = new Quaternionf(0, 0, 0, 1) }
        };
        fixed (Space* space = &_rightAimSpace)
            CheckXr(_xr.CreateActionSpace(_session, &info, space), "xrCreateActionSpace(right aim)");
    }

    private void UpdateRightAim()
    {
        var info = new ActionStateGetInfo
        { Type = StructureType.ActionStateGetInfo, Action = _rightAimAction, SubactionPath = _rightHandPath };
        var state = new ActionStatePose { Type = StructureType.ActionStatePose };
        CheckXr(_xr.GetActionStatePose(_session, &info, &state), "xrGetActionStatePose(right aim)");
        if (state.IsActive == 0) return;
        var location = new SpaceLocation { Type = StructureType.SpaceLocation };
        CheckXr(_xr.LocateSpace(_rightAimSpace, _localSpace, _predictedDisplayTime, &location), "xrLocateSpace(right aim)");
        const SpaceLocationFlags required = SpaceLocationFlags.PositionValidBit | SpaceLocationFlags.OrientationValidBit |
            SpaceLocationFlags.PositionTrackedBit | SpaceLocationFlags.OrientationTrackedBit;
        if ((location.LocationFlags & required) != required) return;
        _rightAimPose = location.Pose;
        RightInputAvailable = true;
    }

    public Vector2? GetRightScreenPoint()
    {
        if (!RightInputAvailable || _screenRecenterPending || _capturedScreen is null) return null;
        var p = _rightAimPose.Position;
        var q = _rightAimPose.Orientation;
        float height = _screenPlacement.Width * _screenTextureHeight / _screenTextureWidth;
        return ScreenRay.TryIntersect(new Vector3(p.X, p.Y, p.Z), new Quaternion(q.X, q.Y, q.Z, q.W),
            _screenPlacement.Position, _screenPlacement.Orientation, _screenPlacement.Width, height, out var uv) ? uv : null;
    }
}
