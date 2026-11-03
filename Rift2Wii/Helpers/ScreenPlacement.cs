using System.Numerics;

namespace OpenXRInputExplorer.Helpers;

/// <summary>A level, world-fixed screen anchored to the last recentered head position.</summary>
internal sealed class ScreenPlacement
{
    public float Width { get; private set; } = 1.6f;
    public float Distance { get; private set; } = 2f;
    public float Height => Width * 9f / 16f;
    public Vector3 Anchor { get; private set; } = Vector3.Zero;
    public Quaternion Orientation { get; private set; } = Quaternion.Identity;
    public Vector3 Position => Anchor + Vector3.Transform(-Vector3.UnitZ, Orientation) * Distance;

    public void SetDimensions(float width, float distance)
    {
        if (!float.IsFinite(width) || width < 0.5f || width > 4f)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (!float.IsFinite(distance) || distance < 0.5f || distance > 5f)
            throw new ArgumentOutOfRangeException(nameof(distance));
        Width = width;
        Distance = distance;
    }

    public bool TryRecenter(Vector3 headPosition, Quaternion headOrientation)
    {
        if (!float.IsFinite(headPosition.X) || !float.IsFinite(headPosition.Y) ||
            !float.IsFinite(headPosition.Z) || !float.IsFinite(headOrientation.LengthSquared()) ||
            headOrientation.LengthSquared() < 0.0001f)
            return false;

        var forward = Vector3.Transform(-Vector3.UnitZ, Quaternion.Normalize(headOrientation));
        // Ignore head tilt and roll so the screen remains upright at eye height.
        forward.Y = 0;
        if (forward.LengthSquared() < 0.0001f)
            return false; // Looking straight up/down: wait for a usable heading.

        forward = Vector3.Normalize(forward);
        Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(-forward.X, -forward.Z));
        Anchor = headPosition;
        return true;
    }
}
