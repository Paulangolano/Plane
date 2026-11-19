namespace Skyreach
{
    /// <summary>The single shape every input source writes into. The game only reads this.</summary>
    public sealed class InputState
    {
        public float Pitch, Roll, Yaw;
        public float Throttle; // persistent 0..1
        public bool Boost, Brake;

        // One-frame "pressed" events
        public bool Restart, CycleComfort, ToggleMute, ToggleView, Recenter;

        // Grab feedback for the cockpit visuals
        public bool StickGrabbed, ThrottleGrabbed, StickNear, ThrottleNear;

        public FlightInput ToFlightInput() => new FlightInput
        {
            Pitch = Pitch, Roll = Roll, Yaw = Yaw, Throttle = Throttle, Boost = Boost, Brake = Brake,
        };

        public void ClearEdges()
        {
            Restart = CycleComfort = ToggleMute = ToggleView = Recenter = false;
        }

        public static float Deadzone(float v, float dz = 0.12f)
        {
            if (System.Math.Abs(v) < dz) return 0;
            return System.Math.Sign(v) * ((System.Math.Abs(v) - dz) / (1 - dz));
        }
    }
}
