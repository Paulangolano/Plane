using System;
using UnityEngine;

namespace Skyreach
{
    public struct Sr22Input
    {
        public float Pitch, Roll, Yaw, Throttle;
        public float Brake, BrakeLeft, BrakeRight; // 0..1
        public float TrimRate, RollTrimRate;
    }

    /// <summary>
    /// Force-based SR22-class flight model (port of webxr/src/aircraft/sr22/sr22Physics.js)
    /// in Unity's frame: forward = +Z, up = +Y, right = +X. Plain C#, unit-testable.
    /// </summary>
    public sealed class Sr22Physics
    {
        const float G = 9.81f;
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public Vector3 Velocity;
        public float GroundSpeed;
        public bool OnGround = true, Crashed, Done, Stalling, StallWarning, EspActive;
        public string CrashReason = "";
        public bool Assists = true;
        public float Throttle;
        public bool EngineRunning = true;
        public float Rpm = Sr22Data.IdleRpm;
        public float FuelGal = 81;
        public float Flaps, FlapsTarget;
        public float Trim = 0.12f, RollTrim;
        public bool ParkingBrake;
        public string Chute = "stowed"; // stowed | inflating | deployed | failed
        public float ChuteT;
        public float PitchInput, RollInput, YawInput; // smoothed
        public float RollRateState;
        public float Alpha, Beta, GLoad = 1;
        public float BrakeLeft, BrakeRight;
        float dropDir;

        public event Action Takeoff;
        public event Action<float, bool, bool> Landed; // sink m/s, hard, offRunway
        public event Action<string> Crash;
        public event Action<bool> ChuteEvent;          // ok
        public event Action<bool> ChuteLanded;         // water
        public event Action EngineOut;

        public Vector3 Forward => Rotation * Vector3.forward;
        public Vector3 Right => Rotation * Vector3.right;
        public Vector3 Up => Rotation * Vector3.up;
        public float Speed => Velocity.magnitude;
        public float Altitude => Position.y - Sr22Data.GearHeight;
        public float Sigma => Sr22Data.DensityRatio(Altitude);
        public float IasKt => Speed * Mathf.Sqrt(Sigma) / Sr22Data.Kt;
        public float Bank => Mathf.Atan2(-Right.y, Up.y);
        public float PitchAngle => Mathf.Asin(Mathf.Clamp(Forward.y, -1f, 1f));
        public float Heading => Mathf.Atan2(Forward.x, Forward.z);
        public float StallAlpha => Sr22Data.StallAlpha(Flaps);
        public float ManifoldPressure => EngineRunning ? 10 + 19.5f * Throttle * Sigma : 29.9f * Sigma;
        public float FuelFlow => EngineRunning ? 2.8f + 24 * Throttle * Sigma : 0;
        public int PercentPower => EngineRunning ? Mathf.RoundToInt(Throttle * Sigma * 100) : 0;

        public void Reset(Vector3 position, float headingRad, bool onGround = true, float speed = 0, float throttle = 0, float flaps = 0)
        {
            Position = position;
            Rotation = Quaternion.AngleAxis(headingRad * Mathf.Rad2Deg, Vector3.up);
            Velocity = Forward * speed;
            GroundSpeed = onGround ? speed : 0;
            OnGround = onGround;
            Crashed = Done = Stalling = StallWarning = EspActive = false;
            CrashReason = "";
            Throttle = throttle;
            EngineRunning = true;
            Rpm = Sr22Data.IdleRpm;
            FuelGal = 81;
            Flaps = FlapsTarget = flaps;
            Trim = 0.12f;
            RollTrim = 0;
            ParkingBrake = false;
            Chute = "stowed";
            ChuteT = 0;
            PitchInput = RollInput = YawInput = RollRateState = 0;
            Alpha = Beta = 0;
            GLoad = 1;
            dropDir = 0;
        }

        // Unity is left-handed: +angle about X pitches the nose DOWN, about Z rolls LEFT.
        public void PitchUp(float rad) => Rotation *= Quaternion.AngleAxis(-rad * Mathf.Rad2Deg, Vector3.right);
        void RollRight(float rad) => Rotation *= Quaternion.AngleAxis(-rad * Mathf.Rad2Deg, Vector3.forward);
        void YawRight(float rad) => Rotation *= Quaternion.AngleAxis(rad * Mathf.Rad2Deg, Vector3.up);

        public float FlapsStep(int dir)
        {
            int i = Array.IndexOf(Sr22Data.FlapDetents, FlapsTarget);
            if (i < 0) i = 0;
            FlapsTarget = Sr22Data.FlapDetents[Mathf.Clamp(i + dir, 0, 2)];
            return FlapsTarget;
        }

        /// <summary>Pull the parachute handle. Returns "deployed", "failed", "ground" or the current state.</summary>
        public string DeployChute()
        {
            if (Chute != "stowed" || Crashed || Done) return Chute;
            if (OnGround) return "ground";
            if (IasKt > Sr22Data.ChuteMaxDeploySpeed)
            {
                Chute = "failed";
                ChuteEvent?.Invoke(false);
                return "failed";
            }
            Chute = "inflating";
            ChuteT = 0;
            EngineRunning = false;
            Throttle = 0;
            ChuteEvent?.Invoke(true);
            return "deployed";
        }

        public void Update(float dt, Sr22Input input, Func<float, float, GroundSample> sampleGround)
        {
            if (Crashed || Done) return;
            dt = Mathf.Min(dt, 0.05f);
            float k = 1 - Mathf.Exp(-10 * dt);
            PitchInput += (Mathf.Clamp(input.Pitch, -1, 1) - PitchInput) * k;
            RollInput += (Mathf.Clamp(input.Roll, -1, 1) - RollInput) * k;
            YawInput += (Mathf.Clamp(input.Yaw, -1, 1) - YawInput) * k;
            if (EngineRunning) Throttle = Mathf.Clamp01(input.Throttle);
            if (input.TrimRate != 0) Trim = Mathf.Clamp(Trim + input.TrimRate * 0.22f * dt, -1, 1);
            if (input.RollTrimRate != 0) RollTrim = Mathf.Clamp(RollTrim + input.RollTrimRate * 0.3f * dt, -1, 1);

            float fd = FlapsTarget - Flaps;
            Flaps += Mathf.Sign(fd) * Mathf.Min(Mathf.Abs(fd), Sr22Data.FlapRate * dt);

            float rpmTarget = EngineRunning ? Sr22Data.IdleRpm + (Sr22Data.MaxRpm - Sr22Data.IdleRpm) * Mathf.Min(1, Throttle * 1.6f) : 0;
            Rpm += (rpmTarget - Rpm) * Mathf.Min(1, dt * (EngineRunning ? 3 : 0.8f));
            if (EngineRunning)
            {
                FuelGal = Mathf.Max(0, FuelGal - FuelFlow / 3600f * dt);
                if (FuelGal <= 0)
                {
                    EngineRunning = false;
                    EngineOut?.Invoke();
                }
            }

            if (Chute == "inflating" || Chute == "deployed")
            {
                ChuteT += dt;
                if (Chute == "inflating" && ChuteT >= Sr22Data.ChuteInflateTime) Chute = "deployed";
            }

            BrakeLeft = ParkingBrake ? 1 : Mathf.Max(input.Brake, input.BrakeLeft);
            BrakeRight = ParkingBrake ? 1 : Mathf.Max(input.Brake, input.BrakeRight);

            int steps = Mathf.CeilToInt(dt / (1f / 120f));
            float h = dt / steps;
            for (int i = 0; i < steps && !Crashed && !Done; i++)
            {
                if (OnGround) StepGround(h, sampleGround);
                else StepAir(h, sampleGround);
            }
        }

        void ProtectedInputs(out float pitch, out float roll)
        {
            pitch = PitchInput;
            roll = RollInput;
            EspActive = false;
            if (!Assists || Chute != "stowed" || OnGround) return;
            float bank = Bank;
            float limit = 45 * Mathf.Deg2Rad;
            if (Mathf.Abs(bank) > limit)
            {
                roll -= Mathf.Sign(bank) * Mathf.Min(1, (Mathf.Abs(bank) - limit) / (15 * Mathf.Deg2Rad)) * 1.2f;
                EspActive = true;
            }
            float margin = StallAlpha - 0.045f;
            if (Alpha > margin)
            {
                pitch -= Mathf.Min(2, (Alpha - margin) * 30);
                EspActive = true;
            }
            if (IasKt > Sr22Data.Vne)
            {
                pitch += Mathf.Min(0.6f, (IasKt - Sr22Data.Vne) * 0.03f);
                EspActive = true;
            }
            float pa = PitchAngle;
            if (pa > 0.44f) { pitch -= (pa - 0.44f) * 3; EspActive = true; }
            else if (pa < -0.35f) { pitch += (-0.35f - pa) * 3; EspActive = true; }
            pitch = Mathf.Clamp(pitch, -1.5f, 1.5f);
            roll = Mathf.Clamp(roll, -1.5f, 1.5f);
        }

        struct Aero { public float V, Qd, Lift, Drag, Thrust, Qf, QfTail, Rho; }

        Aero ComputeAero()
        {
            float sigma = Sigma;
            float rho = 1.225f * sigma;
            float V = Velocity.magnitude;
            Vector3 vb = Quaternion.Inverse(Rotation) * Velocity;
            float u = Mathf.Max(vb.z, 0.5f);
            Alpha = V > 2 ? Mathf.Atan2(-vb.y, u) : 0;
            Beta = V > 2 ? Mathf.Atan2(vb.x, u) : 0;
            float qd = 0.5f * rho * V * V;
            float cl = Sr22Data.LiftCoefficient(Alpha, Flaps);
            float cd = Sr22Data.DragCoefficient(cl, Flaps, EngineRunning ? Throttle : 0);
            float thrust = 0;
            if (EngineRunning)
            {
                float power = Sr22Data.MaxPower * Throttle * Mathf.Pow(sigma, 1.1f);
                float eta = 0.55f + 0.3f * Sr22Data.SmoothStep01(20, 80, V);
                thrust = Mathf.Min(Sr22Data.StaticThrust * Throttle * Mathf.Sqrt(sigma), eta * power / Mathf.Max(V, 1));
            }
            float qRef = 0.5f * 1.225f * Sr22Data.RefSpeed * Sr22Data.RefSpeed;
            float qf = Mathf.Clamp(qd / qRef, 0.03f, 1.8f);
            float wash = EngineRunning ? Throttle * 0.55f * (1 - Sr22Data.SmoothStep01(20, 50, V)) : 0;
            Stalling = Alpha > StallAlpha && V > 5;
            StallWarning = Alpha > StallAlpha - 0.06f && V > 5 && !OnGround;
            return new Aero
            {
                V = V, Qd = qd, Lift = qd * Sr22Data.WingArea * cl, Drag = qd * Sr22Data.WingArea * cd,
                Thrust = thrust, Qf = qf, QfTail = Mathf.Min(1.8f, qf + wash), Rho = rho,
            };
        }

        void StepAir(float h, Func<float, float, GroundSample> sampleGround)
        {
            var a = ComputeAero();
            ProtectedInputs(out float pitch, out float roll);
            bool chuteOut = Chute == "inflating" || Chute == "deployed";

            if (!chuteOut)
            {
                float trimAlpha = Mathf.Lerp(Sr22Data.TrimAlpha[0], Sr22Data.TrimAlpha[1], (Trim + 1) / 2);
                float alphaCmd = trimAlpha + pitch * Sr22Data.ElevatorAlpha;
                float pitchRate = Sr22Data.PitchGain * (alphaCmd - Alpha) * a.QfTail;
                if (Stalling) pitchRate -= (Alpha - StallAlpha) * 1.5f;
                pitchRate = Mathf.Clamp(pitchRate, -1.2f, 1.2f);

                float rollCmd = (roll + RollTrim * 0.1f) * Sr22Data.MaxRollRate * Mathf.Min(a.Qf, 1.3f);
                RollRateState += (rollCmd - RollRateState) * (1 - Mathf.Exp(-4 * h));
                float rollRate = RollRateState - Sr22Data.Dihedral * Beta * a.Qf;
                if (Stalling && !Assists)
                {
                    if (dropDir == 0) dropDir = Beta != 0 ? -Mathf.Sign(Beta) : 1;
                    rollRate += dropDir * Mathf.Min(1.2f, (Alpha - StallAlpha) * 8);
                }
                else if (!Stalling) dropDir = 0;

                float yawRate = YawInput * Sr22Data.MaxYawRate * a.QfTail + Sr22Data.Weathervane * Beta * a.Qf;
                yawRate -= 0.12f * RollInput * a.Qf;
                float torque = EngineRunning ? Throttle * 0.1f * (1 - Sr22Data.SmoothStep01(30, 80, a.V)) : 0;
                yawRate -= Assists ? torque * 0.2f : torque;
                if (Assists) yawRate += 1.4f * Beta * a.Qf;

                PitchUp(pitchRate * h);
                RollRight(rollRate * h);
                YawRight(yawRate * h);
            }
            else
            {
                var target = Quaternion.AngleAxis(Heading * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(0.1f * Mathf.Rad2Deg, Vector3.right);
                Rotation = Quaternion.Slerp(Rotation, target, Mathf.Min(1, h * 1.2f));
            }
            Rotation = Normalize(Rotation);

            float V = a.V;
            Vector3 fwd = Forward, right = Right;
            Vector3 dir = V > 0.5f ? Velocity / V : fwd;
            Vector3 liftDir = Vector3.Cross(dir, right); // left-handed: velocity × right points up
            liftDir = liftDir.magnitude < 1e-3f ? Vector3.up : liftDir.normalized;
            Vector3 force = new Vector3(0, -Sr22Data.Mass * G, 0)
                            + liftDir * a.Lift - dir * a.Drag + fwd * a.Thrust
                            - right * (Sr22Data.SideForce * Beta * a.Qd * Sr22Data.WingArea);
            if (chuteOut)
            {
                float inflation = Chute == "deployed" ? 1 : Sr22Data.SmoothStep01(0, 1, ChuteT / Sr22Data.ChuteInflateTime);
                force -= dir * (0.5f * a.Rho * V * V * Sr22Data.ChuteCdA * inflation);
            }
            GLoad = a.Lift / (Sr22Data.Mass * G);
            Velocity += force * (h / Sr22Data.Mass);
            Position += Velocity * h;

            if (IasKt > Sr22Data.StructuralLimit)
            {
                DoCrash($"Airframe overstressed at {Mathf.RoundToInt(IasKt)} kt (never exceed is {Sr22Data.Vne} kt).");
                return;
            }
            CheckContact(sampleGround);
        }

        void StepGround(float h, Func<float, float, GroundSample> sampleGround)
        {
            var a = ComputeAero();
            var g = sampleGround(Position.x, Position.z);
            if (g.Type == SurfaceType.Water)
            {
                DoCrash("You rolled off the island into the sea.");
                return;
            }
            float weight = Sr22Data.Mass * G;
            float normal = Mathf.Max(0, weight - a.Lift);
            float rolling = g.Type == SurfaceType.Runway ? Sr22Data.RollingFriction : 0.07f;
            float brake = (BrakeLeft + BrakeRight) / 2;
            float s = GroundSpeed;
            float accel = (a.Thrust - a.Drag) / Sr22Data.Mass;
            if (s > 0.05f || accel > 0) accel -= (rolling + brake * Sr22Data.BrakeFriction) * normal / Sr22Data.Mass;
            s = Mathf.Max(0, s + accel * h);
            if (s < 0.05f && a.Thrust < (rolling + brake * Sr22Data.BrakeFriction) * normal) s = 0;
            GroundSpeed = s;

            float yawRate = (BrakeRight - BrakeLeft) * 0.7f * Mathf.Min(1, s / 2);
            yawRate += YawInput * Sr22Data.MaxYawRate * a.QfTail * 0.8f;
            if (Assists) yawRate += YawInput * 0.45f * Mathf.Min(1, s / 4);
            yawRate = Mathf.Clamp(yawRate, -0.6f, 0.6f);

            ProtectedInputs(out float pitch, out _);
            float trimAlpha = Mathf.Lerp(Sr22Data.TrimAlpha[0], Sr22Data.TrimAlpha[1], (Trim + 1) / 2);
            float alphaCmd = trimAlpha + pitch * Sr22Data.ElevatorAlpha;
            float theta = PitchAngle;
            float rot = Sr22Data.SmoothStep01(Sr22Data.RotateMinSpeed, 36, s);
            if (alphaCmd > theta) theta += Mathf.Min(0.5f, Sr22Data.PitchGain * (alphaCmd - theta) * rot) * h;
            else theta -= Mathf.Min(0.5f, (theta - Mathf.Max(alphaCmd, 0)) * 2 + 0.05f) * h;
            theta = Mathf.Clamp(theta, 0, Sr22Data.TailStrikePitch);

            float heading = Heading + yawRate * h;
            Rotation = Quaternion.AngleAxis(heading * Mathf.Rad2Deg, Vector3.up);
            PitchUp(theta);
            Vector3 fwdH = new Vector3(Mathf.Sin(heading), 0, Mathf.Cos(heading));
            Velocity = fwdH * s;
            Position += Velocity * h;
            Position.y = g.Height + Sr22Data.GearHeight;
            StallWarning = false;

            if (a.Lift > weight * 1.02f && theta > 0.03f)
            {
                OnGround = false;
                Velocity.y = 0.4f;
                Position.y += 0.05f;
                Takeoff?.Invoke();
            }
        }

        void CheckContact(Func<float, float, GroundSample> sampleGround)
        {
            var g = sampleGround(Position.x, Position.z);
            float surface = Mathf.Max(g.Height, 0);
            if (Position.y - Sr22Data.GearHeight > surface) return;
            float sink = -Velocity.y;

            if (Chute == "inflating" || Chute == "deployed")
            {
                Done = true;
                Position.y = surface + Sr22Data.GearHeight;
                Velocity = Vector3.zero;
                ChuteLanded?.Invoke(g.Type == SurfaceType.Water);
                return;
            }
            if (g.Type == SurfaceType.Water) { DoCrash("Ditched in the ocean."); return; }
            if (sink > Sr22Data.HardSink) { DoCrash($"Landing gear collapsed (sink rate {Mathf.RoundToInt(sink / Sr22Data.Fpm)} fpm)."); return; }
            if (Mathf.Abs(Bank) > 0.2f) { DoCrash("A wingtip hit the ground. Keep the wings level when you land."); return; }
            if (PitchAngle < -0.07f) { DoCrash("Nose-first impact. Raise the nose (flare) just before touchdown."); return; }
            OnGround = true;
            Position.y = surface + Sr22Data.GearHeight;
            Vector3 f = Forward;
            f.y = 0;
            GroundSpeed = Mathf.Max(0, Vector3.Dot(Velocity, f.normalized));
            Velocity.y = 0;
            Landed?.Invoke(sink, sink > Sr22Data.SafeSink, g.Type != SurfaceType.Runway);
        }

        void DoCrash(string reason)
        {
            Crashed = true;
            CrashReason = reason;
            Velocity = Vector3.zero;
            GroundSpeed = 0;
            Crash?.Invoke(reason);
        }

        static Quaternion Normalize(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 0 ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }
    }
}
