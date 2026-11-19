using System;
using UnityEngine;

namespace Skyreach
{
    public struct FlightInput
    {
        public float Pitch;    // +1 = nose up (stick pulled back)
        public float Roll;     // +1 = roll right
        public float Yaw;      // +1 = yaw right
        public float Throttle; // 0..1
        public bool Boost;
        public bool Brake;
    }

    /// <summary>
    /// Arcade flight model (port of webxr/src/aircraft/flightModel.js) in Unity's frame:
    /// forward = +Z, up = +Y, right = +X. Plain C#, no MonoBehaviour, so it is easy to test.
    /// </summary>
    public sealed class FlightModel
    {
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public Vector3 Velocity;
        public float Speed;
        public float Sink;
        public float Throttle;
        public float BoostFuel = FlightConfig.BoostDuration;
        public bool Boosting;
        public bool OnGround = true;
        public bool Crashed;
        public string CrashReason = "";
        public bool Stalling;
        public float PitchInput, RollInput, YawInput; // smoothed controls

        public event Action Takeoff;
        public event Action<bool> Landed; // arg: offRunway
        public event Action<string> Crash;

        public Vector3 Forward => Rotation * Vector3.forward;
        public Vector3 Right => Rotation * Vector3.right;
        public Vector3 Up => Rotation * Vector3.up;
        public float Altitude => Position.y - FlightConfig.GearHeight;

        /// <summary>Bank angle in radians, + = right wing down.</summary>
        public float Bank => Mathf.Atan2(-Right.y, Up.y);

        /// <summary>Nose pitch angle in radians, + = nose up.</summary>
        public float PitchAngle => Mathf.Asin(Mathf.Clamp(Forward.y, -1f, 1f));

        /// <summary>Compass heading in radians, 0 = +Z.</summary>
        public float Heading => Mathf.Atan2(Forward.x, Forward.z);

        public void Reset(Vector3 position, float headingRad, bool onGround = true, float speed = 0, float throttle = 0)
        {
            Position = position;
            Rotation = Quaternion.AngleAxis(headingRad * Mathf.Rad2Deg, Vector3.up);
            Speed = speed;
            Sink = 0;
            Throttle = throttle;
            BoostFuel = FlightConfig.BoostDuration;
            OnGround = onGround;
            Crashed = false;
            CrashReason = "";
            PitchInput = RollInput = YawInput = 0;
            Velocity = Vector3.zero;
        }

        // Unity is left-handed: +angle about X pitches the nose DOWN, about Z rolls LEFT.
        public void PitchUp(float rad) => Rotation *= Quaternion.AngleAxis(-rad * Mathf.Rad2Deg, Vector3.right);
        void RollRight(float rad) => Rotation *= Quaternion.AngleAxis(-rad * Mathf.Rad2Deg, Vector3.forward);
        void YawRight(float rad) => Rotation *= Quaternion.AngleAxis(rad * Mathf.Rad2Deg, Vector3.up);
        void YawRightWorld(float rad) => Rotation = Quaternion.AngleAxis(rad * Mathf.Rad2Deg, Vector3.up) * Rotation;

        public void Update(float dt, FlightInput input, Func<float, float, GroundSample> sampleGround)
        {
            if (Crashed) return;
            dt = Mathf.Min(dt, 0.05f);

            float k = 1f - Mathf.Exp(-FlightConfig.ControlSmoothing * dt);
            PitchInput += (Mathf.Clamp(input.Pitch, -1, 1) - PitchInput) * k;
            RollInput += (Mathf.Clamp(input.Roll, -1, 1) - RollInput) * k;
            YawInput += (Mathf.Clamp(input.Yaw, -1, 1) - YawInput) * k;
            Throttle = Mathf.Clamp01(input.Throttle);

            Boosting = input.Boost && BoostFuel > 0 && Throttle > 0.2f;
            if (Boosting) BoostFuel = Mathf.Max(0, BoostFuel - dt);
            else BoostFuel = Mathf.Min(FlightConfig.BoostDuration, BoostFuel + FlightConfig.BoostRecharge * dt);

            Vector3 fwd = Forward;
            float target = Boosting ? FlightConfig.BoostSpeed : Throttle * FlightConfig.MaxSpeed;
            Speed += (target - Speed) * FlightConfig.Accel * dt;
            if (!OnGround) Speed -= fwd.y * FlightConfig.GravityClimbPenalty * dt;
            if (OnGround)
            {
                float friction = input.Brake ? FlightConfig.BrakeFriction : FlightConfig.GroundFriction;
                Speed -= Speed * friction * dt * (Throttle < 0.05f || input.Brake ? 1f : 0.1f);
            }
            Speed = Mathf.Clamp(Speed, 0, FlightConfig.BoostSpeed * 1.15f);

            float authority = Mathf.Clamp(Speed / FlightConfig.MinFlySpeed, 0.12f, 1f);

            if (OnGround)
            {
                UpdateGround(dt, sampleGround);
                if (Crashed) return;
            }
            else
            {
                PitchUp(PitchInput * FlightConfig.PitchRate * authority * dt);
                RollRight(RollInput * FlightConfig.RollRate * authority * dt);
                YawRight(YawInput * FlightConfig.YawRate * authority * dt);

                // Arcade coordinated turn: banking swings the nose around.
                YawRightWorld(-Right.y * 0.9f * authority * dt);

                Stalling = Speed < FlightConfig.MinFlySpeed;
                float sinkTarget = Stalling ? (1 - Speed / FlightConfig.MinFlySpeed) * FlightConfig.StallSinkRate : 0;
                Sink += (sinkTarget - Sink) * Mathf.Min(1, dt * 2);
                if (Stalling && PitchAngle > -0.6f) PitchUp(-0.35f * (1 - authority) * dt);
            }
            Rotation = Normalize(Rotation);

            Velocity = Forward * Speed;
            Velocity.y -= Sink;
            Position += Velocity * dt;
            if (Position.y > FlightConfig.Ceiling) Position.y = FlightConfig.Ceiling;

            if (!OnGround) CheckCollision(sampleGround);
        }

        void UpdateGround(float dt, Func<float, float, GroundSample> sampleGround)
        {
            Stalling = false;
            Sink = 0;
            float steer = Mathf.Clamp(YawInput + RollInput * 0.5f, -1, 1);
            YawRightWorld(steer * 0.6f * Mathf.Clamp01(Speed / 20f) * dt);
            float heading = Heading;
            float pitch = PitchAngle;
            bool canRotate = Speed >= FlightConfig.TakeoffSpeed * 0.85f;
            float desired = canRotate ? Mathf.Clamp01(PitchInput) * 0.25f : 0;
            pitch += (desired - pitch) * Mathf.Min(1, dt * 3);
            Rotation = Quaternion.AngleAxis(heading * Mathf.Rad2Deg, Vector3.up);
            PitchUp(pitch);

            var g = sampleGround(Position.x, Position.z);
            if (g.Type == SurfaceType.Water)
            {
                DoCrash("You rolled off the island into the sea!");
                return;
            }
            Position.y = g.Height + FlightConfig.GearHeight;

            if (Speed >= FlightConfig.TakeoffSpeed && pitch > 0.08f)
            {
                OnGround = false;
                Position.y += 0.2f;
                Takeoff?.Invoke();
            }
        }

        void CheckCollision(Func<float, float, GroundSample> sampleGround)
        {
            var g = sampleGround(Position.x, Position.z);
            float surface = Mathf.Max(g.Height, 0);
            if (Position.y - FlightConfig.GearHeight > surface) return;

            float descent = -Velocity.y;
            float roll = Mathf.Abs(Bank);
            float pitch = PitchAngle;
            bool gentle = descent < FlightConfig.SafeTouchdownVSpeed && roll < FlightConfig.SafeTouchdownRoll && pitch > -0.12f && pitch < 0.4f;
            if (g.Type == SurfaceType.Runway && gentle)
            {
                OnGround = true;
                Position.y = g.Height + FlightConfig.GearHeight;
                Landed?.Invoke(false);
                return;
            }
            if (g.Type == SurfaceType.Land && gentle && Speed < 70)
            {
                OnGround = true;
                Position.y = g.Height + FlightConfig.GearHeight;
                Landed?.Invoke(true);
                return;
            }
            DoCrash(g.Type == SurfaceType.Water ? "Splashdown! You hit the ocean." : "You crashed into the island.");
        }

        void DoCrash(string reason)
        {
            Crashed = true;
            CrashReason = reason;
            Speed = 0;
            Sink = 0;
            Velocity = Vector3.zero;
            Crash?.Invoke(reason);
        }

        static Quaternion Normalize(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 0 ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }
    }
}
