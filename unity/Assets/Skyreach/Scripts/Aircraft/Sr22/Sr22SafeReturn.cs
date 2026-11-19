using System;
using UnityEngine;

namespace Skyreach
{
    public struct Sr22Command
    {
        public float Pitch, Roll, Yaw, Throttle, Brake, Flaps;
    }

    /// <summary>
    /// Emergency autoland in the spirit of the SR22 G7+'s Safe Return (port of safeReturn.js):
    /// flies to the island runway, sets up a stabilised 3° approach, lands, brakes and shuts down.
    /// Unity frame: the runway runs along Z and you land toward +Z; the threshold is at z = -450.
    /// </summary>
    public sealed class Sr22SafeReturn
    {
        const float GlideTan = 0.0524078f; // tan(3°)
        const float IafDist = 5500;
        const float IafHeight = 330;

        readonly WorldData world;
        readonly FlightDirector fd = new FlightDirector();
        float powerInt;
        bool savedAssists;
        public bool Active;
        public string Phase = "OFF"; // OFF | ENROUTE | APPROACH | FLARE | ROLLOUT | DONE

        public event Action<string> PhaseChanged;
        public event Action<bool> Complete; // stopped on the runway?

        public Sr22SafeReturn(WorldData world) { this.world = world; }

        float Threshold => -WorldData.RunwayHalfLength;
        float AimZ => Threshold + 150;
        static float RunwayHeight => RunwayConfig.Height;

        public bool Activate(Sr22Physics m)
        {
            if (Active || m.OnGround || m.Crashed || m.Chute != "stowed") return false;
            Active = true;
            savedAssists = m.Assists;
            m.Assists = true;
            fd.Reset();
            powerInt = 0;
            SetPhase(OnFinal(m) ? "APPROACH" : "ENROUTE");
            return true;
        }

        public void Cancel(Sr22Physics m)
        {
            if (!Active) return;
            Active = false;
            if (m != null) m.Assists = savedAssists;
            SetPhase("OFF");
        }

        void SetPhase(string phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        bool OnFinal(Sr22Physics m)
        {
            float dz = Threshold - m.Position.z;
            return dz > 1200 && Mathf.Abs(m.Position.x) < dz * 0.35f && Mathf.Abs(FlightDirector.Wrap(m.Heading)) < 0.7f;
        }

        float TerrainAhead(Sr22Physics m)
        {
            Vector3 f = m.Forward;
            float max = 0;
            foreach (float d in new[] { 0f, 300f, 700f, 1200f, 2000f })
                max = Mathf.Max(max, world.HeightAt(m.Position.x + f.x * d, m.Position.z + f.z * d));
            return max;
        }

        float PowerForSpeed(Sr22Physics m, float kt, float dt)
        {
            float err = kt - m.IasKt;
            powerInt = Mathf.Clamp(powerInt + err * 0.004f * dt, -0.3f, 0.4f);
            return Mathf.Clamp01(0.45f + err * 0.04f + powerInt);
        }

        public Sr22Command? Update(float dt, Sr22Physics m)
        {
            if (!Active) return null;
            if (m.Crashed || m.Chute != "stowed")
            {
                Cancel(m);
                return null;
            }
            Vector3 p = m.Position;
            var c = new Sr22Command();
            switch (Phase)
            {
                case "ENROUTE":
                {
                    float iafZ = Threshold - IafDist;
                    float toX = -p.x, toZ = iafZ - p.z;
                    float dist = Mathf.Sqrt(toX * toX + toZ * toZ);
                    float bearing = Mathf.Atan2(toX, toZ);
                    float targetAlt = Mathf.Max(RunwayHeight + IafHeight, TerrainAhead(m) + 180);
                    c.Roll = fd.RollFor(m, fd.BankForHeading(m, bearing, 0.44f));
                    c.Pitch = fd.PitchForVs(m, Mathf.Clamp((targetAlt - m.Altitude) * 0.08f, -5, 6), dt);
                    c.Throttle = m.Altitude < targetAlt - 40 ? 1 : PowerForSpeed(m, 120, dt);
                    if (dist < 900 || OnFinal(m)) SetPhase("APPROACH");
                    break;
                }
                case "APPROACH":
                {
                    float distToAim = Mathf.Max(0, AimZ - p.z);
                    float hdgCmd = -Mathf.Clamp(p.x * 0.005f + m.Velocity.x * 0.02f, -0.6f, 0.6f);
                    c.Roll = fd.RollFor(m, fd.BankForHeading(m, hdgCmd, 0.35f));
                    float gpAlt = RunwayHeight + distToAim * GlideTan;
                    float vsCmd = Mathf.Clamp(-m.Speed * GlideTan + (gpAlt - m.Altitude) * 0.15f, -6, 3);
                    c.Pitch = fd.PitchForVs(m, vsCmd, dt);
                    bool final = distToAim < 3500;
                    c.Flaps = final ? 1 : 0.5f;
                    c.Throttle = PowerForSpeed(m, final ? 80 : 100, dt);
                    if (m.Altitude - RunwayHeight < 9 && p.z > Threshold - 300) SetPhase("FLARE");
                    break;
                }
                case "FLARE":
                    c.Flaps = 1;
                    c.Roll = fd.RollFor(m, 0);
                    c.Pitch = fd.PitchForVs(m, -0.6f, dt);
                    c.Yaw = Mathf.Clamp(-FlightDirector.Wrap(m.Heading) * 3 - p.x * 0.02f, -1, 1);
                    if (m.OnGround) SetPhase("ROLLOUT");
                    break;
                case "ROLLOUT":
                    c.Flaps = 1;
                    c.Pitch = -0.3f;
                    c.Yaw = Mathf.Clamp(-FlightDirector.Wrap(m.Heading) * 4 - p.x * 0.06f, -1, 1);
                    c.Brake = m.GroundSpeed > 25 ? 0.4f : 0.8f;
                    if (m.GroundSpeed < 0.3f)
                    {
                        m.EngineRunning = false;
                        m.ParkingBrake = true;
                        SetPhase("DONE");
                        Complete?.Invoke(world.IsRunway(p.x, p.z));
                    }
                    break;
                case "DONE":
                    c.Brake = 1;
                    break;
            }
            return c;
        }

        public string Status =>
            Phase == "ENROUTE" ? "SAFE RETURN · NAVIGATING TO RUNWAY" :
            Phase == "APPROACH" ? "SAFE RETURN · APPROACH" :
            Phase == "FLARE" ? "SAFE RETURN · LANDING" :
            Phase == "ROLLOUT" ? "SAFE RETURN · BRAKING" :
            Phase == "DONE" ? "SAFE RETURN · COMPLETE" : "";
    }
}
