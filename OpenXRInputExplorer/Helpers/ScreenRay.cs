using System.Numerics;

namespace OpenXRInputExplorer.Helpers;

internal static class ScreenRay
{
    public static bool TryIntersect(Vector3 origin, Quaternion aim, Vector3 screenPosition,
        Quaternion screenOrientation, float width, float height, out Vector2 uv)
    {
        uv = default;
        if (width <= 0 || height <= 0) return false;
        var inverse = Quaternion.Inverse(screenOrientation);
        var localOrigin = Vector3.Transform(origin - screenPosition, inverse);
        var direction = Vector3.Transform(Vector3.Transform(-Vector3.UnitZ, aim), inverse);
        if (localOrigin.Z <= 0 || direction.Z >= -0.00001f) return false;
        var hit = localOrigin + direction * (-localOrigin.Z / direction.Z);
        uv = new Vector2(0.5f + hit.X / width, 0.5f - hit.Y / height);
        return float.IsFinite(uv.X) && float.IsFinite(uv.Y) &&
            uv.X >= 0 && uv.X <= 1 && uv.Y >= 0 && uv.Y <= 1;
    }
}
