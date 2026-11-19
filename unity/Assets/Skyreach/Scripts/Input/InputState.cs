using System.Collections.Generic;

namespace Skyreach
{
    /// <summary>The single shape every input source writes into. The game and aircraft only read this.</summary>
    public sealed class InputState
    {
        public float Pitch, Roll, Yaw;
        public float Throttle; // persistent 0..1
        public bool Boost, Brake;
        public float BrakeLeft, BrakeRight; // differential toe brakes
        public float TrimRate, RollTrimRate; // trim hat

        // One-frame events: named actions (e.g. "flapsDown", "apToggle") plus the classic ones.
        public readonly HashSet<string> Actions = new HashSet<string>();
        public bool Restart, CycleComfort, ToggleMute, ToggleView, Recenter;

        // Grab / touch feedback for cockpit visuals
        public bool StickGrabbed, ThrottleGrabbed, StickNear, ThrottleNear;
        public float CapsPull;
        public readonly HashSet<string> Touching = new HashSet<string>();

        public FlightInput ToFlightInput() => new FlightInput
        {
            Pitch = Pitch, Roll = Roll, Yaw = Yaw, Throttle = Throttle, Boost = Boost, Brake = Brake,
        };

        /// <summary>Raise a one-frame action. Classic actions also set their flag.</summary>
        public void Fire(string action)
        {
            Actions.Add(action);
            switch (action)
            {
                case "restart": Restart = true; break;
                case "cycleComfort": CycleComfort = true; break;
                case "toggleMute": ToggleMute = true; break;
                case "toggleView": ToggleView = true; break;
                case "recenter": Recenter = true; break;
            }
        }

        public void ClearEdges()
        {
            Actions.Clear();
            Restart = CycleComfort = ToggleMute = ToggleView = Recenter = false;
        }

        public static float Deadzone(float v, float dz = 0.12f)
        {
            if (System.Math.Abs(v) < dz) return 0;
            return System.Math.Sign(v) * ((System.Math.Abs(v) - dz) / (1 - dz));
        }
    }
}
