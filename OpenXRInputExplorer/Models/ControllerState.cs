namespace OpenXRInputExplorer.Models
{
    public class ControllerState
    {
        public bool TriggerPressed { get; set; }
        public bool GripPressed { get; set; }
        public bool A { get; set; }
        public bool B { get; set; }
        public bool X { get; set; }
        public bool Y { get; set; }
        public bool ThumbstickClick { get; set; }

        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }

        public float Pitch { get; set; }
        public float Yaw { get; set; }
        public float Roll { get; set; }

        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float RotationW { get; set; }

        public float Trigger { get; set; }
        public float Grip { get; set; }

        public float ThumbstickX { get; set; }
        public float ThumbstickY { get; set; }
    }
}
