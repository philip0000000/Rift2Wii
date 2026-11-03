using OpenXRInputExplorer.Helpers;
using Silk.NET.OpenXR;
using System.Numerics;

namespace OpenXRInputExplorer.Services;

public unsafe partial class OpenXRService
{
    private readonly ScreenPlacement _screenPlacement = new();
    private Space _viewSpace;
    private bool _screenRecenterPending = true;
    private long _screenRecenterDue;
    public string ScreenPlacementStatus { get; private set; } = "Waiting for headset tracking to place the screen.";
    public bool IsScreenPlacementReady => !_screenRecenterPending;

    public void SetScreenDimensions(float width, float distance) => _screenPlacement.SetDimensions(width, distance);

    public void RequestScreenRecenter()
    {
        // Give the user time to return to the headset after clicking on the desktop.
        _screenRecenterPending = true;
        _screenRecenterDue = Environment.TickCount64 + 3000;
        ScreenPlacementStatus = "Recentering in 3 seconds. Put on the headset and look forward.";
    }

    private void CreateViewSpace()
    {
        var info = new ReferenceSpaceCreateInfo
        {
            Type = StructureType.ReferenceSpaceCreateInfo,
            ReferenceSpaceType = ReferenceSpaceType.View,
            PoseInReferenceSpace = new Posef { Orientation = new Quaternionf(0, 0, 0, 1) }
        };
        fixed (Space* space = &_viewSpace)
            CheckXr(_xr.CreateReferenceSpace(_session, &info, space), "xrCreateReferenceSpace(view)");
    }

    private void UpdateScreenPlacement()
    {
        if (!_screenRecenterPending) return;
        long remaining = _screenRecenterDue - Environment.TickCount64;
        if (remaining > 0)
        {
            ScreenPlacementStatus = $"Recentering in {(remaining + 999) / 1000} seconds. Put on the headset and look forward.";
            return;
        }

        var location = new SpaceLocation { Type = StructureType.SpaceLocation };
        CheckXr(_xr.LocateSpace(_viewSpace, _localSpace, _predictedDisplayTime, &location), "xrLocateSpace(headset)");
        var required = SpaceLocationFlags.PositionValidBit | SpaceLocationFlags.OrientationValidBit |
            SpaceLocationFlags.PositionTrackedBit | SpaceLocationFlags.OrientationTrackedBit;
        if ((location.LocationFlags & required) != required)
        {
            ScreenPlacementStatus = "Waiting for headset tracking to recenter. Put on the headset.";
            return;
        }

        var p = location.Pose.Position;
        var q = location.Pose.Orientation;
        if (!_screenPlacement.TryRecenter(new Vector3(p.X, p.Y, p.Z), new Quaternion(q.X, q.Y, q.Z, q.W)))
        {
            ScreenPlacementStatus = "Look forward rather than straight up or down to recenter.";
            return;
        }

        _screenRecenterPending = false;
        ScreenPlacementStatus = "Screen placed. It stays fixed until you recenter again.";
    }
}
