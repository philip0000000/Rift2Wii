using System.Numerics;
using OpenXRInputExplorer.Models;

namespace OpenXRInputExplorer.Services;

public readonly record struct ControllerMotion(ulong Timestamp, Vector3 Acceleration, Vector3 Gyroscope)
{
    // DSU gravity at rest; Dolphin converts this to Wii +Z gravity.
    public static ControllerMotion Rest => new(0, new(0, -1, 0), Vector3.Zero);
}

/// <summary>Converts timestamped OpenXR grip tracking into a synthetic DSU IMU.</summary>
public sealed class ControllerMotionEstimator
{
    private long _time;
    private Vector3 _position, _velocity, _acceleration, _gyro;
    private Quaternion _rotation;
    private bool _velocityReady;
    private ControllerMotion _last = ControllerMotion.Rest;
    public void Reset() { _time = 0; _velocityReady = false; _acceleration = _gyro = Vector3.Zero; _last = ControllerMotion.Rest; }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    public ControllerMotion Update(ControllerState state, bool available, Quaternion? orientation = null)
    {
        var p = new Vector3(state.PositionX, state.PositionY, state.PositionZ);
        var q = orientation ?? new Quaternion(state.RotationX, state.RotationY, state.RotationZ, state.RotationW);
        if (!available || state.SampleTime <= 0 || !Finite(p) || !float.IsFinite(q.LengthSquared()) || q.LengthSquared() < .5f)
        { Reset(); return _last; }
        q = Quaternion.Normalize(q);
        if (_time == state.SampleTime) return _last;
        float dt = (state.SampleTime - _time) / 1_000_000_000f;
        bool continuous = _time != 0 && dt >= .001f && dt <= .1f && Vector3.Distance(p, _position) < .5f && MathF.Abs(Quaternion.Dot(q, _rotation)) > .7f;
        Vector3 velocity = state.LinearVelocityValid ? state.LinearVelocity : continuous ? (p - _position) / dt : Vector3.Zero;
        if (!Finite(velocity) || velocity.Length() > 15) { continuous = false; velocity = Vector3.Zero; }
        Vector3 acceleration = continuous && _velocityReady ? (velocity - _velocity) / dt : Vector3.Zero;
        Vector3 angular = Vector3.Zero;
        if (continuous)
        {
            if (state.AngularVelocityValid) angular = state.AngularVelocity;
            else
            {
                var delta = Quaternion.Normalize(q * Quaternion.Conjugate(_rotation));
                if (delta.W < 0) delta = new(-delta.X, -delta.Y, -delta.Z, -delta.W);
                var axis = new Vector3(delta.X, delta.Y, delta.Z);
                float length = axis.Length();
                if (length > .000001f) angular = axis / length * (2 * MathF.Atan2(length, delta.W) / dt);
            }
        }
        // Tracking corrections must not become violent virtual swings.
        if (!Finite(acceleration) || acceleration.Length() > 100) acceleration = Vector3.Zero;
        if (!Finite(angular) || angular.Length() > 35) angular = Vector3.Zero;
        float blend = continuous ? 1 - MathF.Exp(-dt / .025f) : 1;
        _acceleration = Vector3.Lerp(continuous ? _acceleration : Vector3.Zero, acceleration, blend);
        _gyro = Vector3.Lerp(continuous ? _gyro : Vector3.Zero, angular, blend);
        var inverse = Quaternion.Conjugate(q);
        var force = Vector3.Transform(_acceleration / 9.80665f + Vector3.UnitY, inverse);
        var spin = Vector3.Transform(_gyro, inverse) * (180 / MathF.PI);
        // XR: +X right, +Y up, +Z backward. Wii: +X left, +Y backward, +Z up.
        // Dolphin converts DSU accel to (ax, -az, -ay), gyro to (-pitch, -roll, -yaw).
        _last = new((ulong)(state.SampleTime / 1000), new(-force.X, -force.Y, -force.Z), new(spin.X, -spin.Y, -spin.Z));
        _time = state.SampleTime; _position = p; _rotation = q; _velocity = velocity; _velocityReady = continuous || state.LinearVelocityValid;
        return _last;
    }
}
