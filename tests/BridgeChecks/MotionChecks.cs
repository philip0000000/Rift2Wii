using System.Numerics;
using System.Buffers.Binary;
using OpenXRInputExplorer.Models;
using OpenXRInputExplorer.Services;

static class MotionChecks
{
    public static void Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        static bool Near(Vector3 a, Vector3 b, float tolerance = .001f) => Vector3.Distance(a, b) < tolerance;
        var estimator = new ControllerMotionEstimator();
        var state = new ControllerState { IsTracked = true, SampleTime = 1_000_000_000, RotationW = 1,
            LinearVelocityValid = true, AngularVelocityValid = true };
        var rest = estimator.Update(state, true);
        Check(Near(rest.Acceleration, new(0,-1,0)) && Near(rest.Gyroscope, Vector3.Zero), "Rest includes gravity, no gyro");
        state.SampleTime += 10_000_000;
        state.AngularVelocity = new(MathF.PI, 0, 0);
        var spin = estimator.Update(state, true);
        Check(spin.Gyroscope.X > 50 && spin.Gyroscope.X < 180 && spin.Gyroscope.Y == 0, "Pitch axis, rad/s to deg/s and filtering");
        Check(estimator.Update(state, true) == spin, "Duplicate sample does not differentiate twice");
        state.SampleTime += 10_000_000;
        state.LinearVelocity = new(.1f,0,0);
        Check(estimator.Update(state,true).Acceleration.X < -.2f, "Rightward acceleration is negative Wii X (positive is left)");
        Check(estimator.Update(state,false) == ControllerMotion.Rest, "Lost tracking releases dynamic motion");
        state.SampleTime += 10_000_000; state.PositionX = 20;
        Check(Near(estimator.Update(state,true).Gyroscope,Vector3.Zero), "Recovery primes derivative");
        state.SampleTime += 10_000_000; state.PositionX = 0;
        Check(Near(estimator.Update(state,true).Gyroscope,Vector3.Zero), "Tracking teleport rejects gyro");
        estimator.Reset();
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI/2);
        state.RotationX=q.X; state.RotationY=q.Y; state.RotationZ=q.Z; state.RotationW=q.W;
        state.LinearVelocity=Vector3.Zero; state.AngularVelocity=Vector3.Zero;
        Check(Near(estimator.Update(state,true).Acceleration,-Vector3.UnitX), "Gravity rotates into controller-local frame with Wii left-positive X");
        // The right-hand ray uses aim orientation, which can differ from grip orientation.
        estimator.Reset();
        Check(Near(estimator.Update(state,true,Quaternion.Identity).Acceleration,new(0,-1,0)), "Aim-frame override aligns right-hand motion with the desktop ray");
        // Decode via Dolphin's DSU conventions, then check its Wii-space gravity and angular axes.
        static Vector3 WiiAccel(ControllerMotion m) => new(m.Acceleration.X,-m.Acceleration.Z,-m.Acceleration.Y);
        static Vector3 WiiGyro(ControllerMotion m) => new(-m.Gyroscope.X,-m.Gyroscope.Z,-m.Gyroscope.Y);
        foreach (var (axis, expected) in new[] { (Vector3.UnitX,-Vector3.UnitX),
                     (Vector3.UnitY,Vector3.UnitZ), (Vector3.UnitZ,Vector3.UnitY) })
        {
            estimator.Reset(); state.AngularVelocity=Vector3.Zero;
            estimator.Update(state,true,Quaternion.Identity);
            state.SampleTime+=10_000_000; state.AngularVelocity=axis;
            var sample=estimator.Update(state,true,Quaternion.Identity);
            Check(Near(Vector3.Normalize(WiiGyro(sample)),expected), "World angular velocity maps to the corresponding Wii axis");
            Check(Near(WiiAccel(sample),Vector3.UnitZ),"Level aim retains upward Wii gravity on every rotation axis");
        }
        estimator.Reset(); state.RotationZ=0; state.RotationW=1; state.LinearVelocityValid=false; state.AngularVelocityValid=false;
        estimator.Update(state,true);
        state.SampleTime+=10_000_000; state.PositionX+=.01f;
        Check(Near(estimator.Update(state,true).Acceleration,new(0,-1,0)), "Position fallback primes velocity without false acceleration");
        state.SampleTime+=10_000_000; state.PositionX+=.01f;
        Check(Near(estimator.Update(state,true).Acceleration,new(0,-1,0),.01f), "Constant velocity is not acceleration");
        q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, .01f);
        state.RotationY=q.Y; state.RotationW=q.W; state.SampleTime+=10_000_000;
        var fallbackSpin=estimator.Update(state,true);
        Check(fallbackSpin.Gyroscope.Y < -10 && fallbackSpin.Gyroscope.Y > -60, "Quaternion fallback produces yaw velocity in deg/s");
        state.SampleTime+=500_000_000;
        Check(Near(estimator.Update(state,true).Gyroscope,Vector3.Zero), "Long sample gap resets rotation filter");
        state.PositionX=float.NaN; state.SampleTime+=10_000_000;
        Check(estimator.Update(state,true)==ControllerMotion.Rest,"Invalid sample produces safe neutral");
        var packet=DsuProtocol.Pad(1,DsuPadState.Neutral with { Motion = new(123,new(1,2,3),new(4,5,6)) },1,2,.15f);
        Check(packet[22]==2 && BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(68))>0, "Full gyro and nonzero timestamp");
        for(int i=0;i<6;i++) Check(BinaryPrimitives.ReadSingleLittleEndian(packet.AsSpan(76+4*i))==i+1,"Sensor byte order and axes");
        Check(BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8))==DsuProtocol.Crc(packet),"Motion packet CRC");
        Console.WriteLine("Motion conversion checks passed.");
    }
}
