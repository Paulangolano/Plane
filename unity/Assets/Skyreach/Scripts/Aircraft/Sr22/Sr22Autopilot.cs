using System;
using UnityEngine;

namespace Skyreach
{
    /// <summary>Shared inner loops: bank tracking with the ailerons, attitude / vertical speed with the elevator.</summary>
    public sealed class FlightDirector
    {
        float pitchInt;

        public void Reset() => pitchInt = 0;

        public static float Wrap(float a) => Mathf.Atan2(Mathf.Sin(a), Mathf.Cos(a));

        public float RollFor(Sr22Physics m, float bankCmd) =>
            Mathf.Clamp((bankCmd - m.Bank) * 2.2f - m.RollRateState * 0.4f, -1, 1);

        public float BankForHeading(Sr22Physics m, float heading, float maxBank = 0.44f) =>
            Mathf.Clamp(Wrap(heading - m.Heading) * 1.3f, -maxBank, maxBank);

        public float PitchForAttitude(Sr22Physics m, float thetaCmd, float dt)
        {
            float err = thetaCmd - m.PitchAngle;
            pitchInt = Mathf.Clamp(pitchInt + err * 0.9f * dt, -0.7f, 0.7f);
            return Mathf.Clamp(err * 3 + pitchInt, -1, 1);
        }

        public float PitchForVs(Sr22Physics m, float vsCmd, float dt)
        {
            float V = Mathf.Max(m.Speed, 20);
            float gamma = Mathf.Asin(Mathf.Clamp(vsCmd / V, -0.5f, 0.5f));
            float thetaCmd = gamma + m.Alpha + Mathf.Clamp((vsCmd - m.Velocity.y) * 0.02f, -0.08f, 0.08f);
            return PitchForAttitude(m, thetaCmd, dt);
        }
    }

    /// <summary>
    /// Autopilot in the style of the SR22's (port of autopilot.js): AP master, HDG, ALT hold,
    /// VS with altitude capture, PIT/ROL hold and the blue LVL button. Power stays with the pilot.
    /// </summary>
    public sealed class Sr22Autopilot
    {
        readonly FlightDirector fd = new FlightDirector();
        public bool Engaged;
        public string Lateral = "ROL";  // ROL | HDG | LVL
        public string Vertical = "PIT"; // PIT | ALT | VS | LVL
        public bool AltArmed;
        public float HdgBug;                    // rad
        public float AltSel = 1500 * Sr22Data.Ft; // m
        public float VsSel = 500 * Sr22Data.Fpm;  // m/s
        float rollHold, pitchHold;

        public event Action Engage;
        public event Action<string> Disconnected;
        public event Action Captured;

        public void Activate(Sr22Physics m)
        {
            if (Engaged) return;
            Engaged = true;
            fd.Reset();
            if (Lateral == "ROL") rollHold = Mathf.Abs(m.Bank) < 0.1f ? 0 : Mathf.Clamp(m.Bank, -0.44f, 0.44f);
            if (Vertical == "PIT") pitchHold = Mathf.Clamp(m.PitchAngle, -0.17f, 0.26f);
            Engage?.Invoke();
        }

        public void Disconnect(string reason = "")
        {
            if (!Engaged) return;
            Engaged = false;
            if (Lateral == "LVL") Lateral = "ROL";
            if (Vertical == "LVL") Vertical = "PIT";
            Disconnected?.Invoke(reason);
        }

        public void Toggle(Sr22Physics m)
        {
            if (Engaged) Disconnect();
            else Activate(m);
        }

        public void Heading(Sr22Physics m)
        {
            Lateral = Lateral == "HDG" ? "ROL" : "HDG";
            if (Lateral == "ROL") rollHold = 0;
            Activate(m);
        }

        public void AltitudeHold(Sr22Physics m)
        {
            Vertical = "ALT";
            AltSel = m.Altitude;
            AltArmed = false;
            Activate(m);
        }

        public void VerticalSpeed(Sr22Physics m)
        {
            Vertical = "VS";
            VsSel = Mathf.Clamp(m.Velocity.y, -1500 * Sr22Data.Fpm, 1500 * Sr22Data.Fpm);
            VsSel = Mathf.Round(VsSel / (100 * Sr22Data.Fpm)) * 100 * Sr22Data.Fpm;
            AltArmed = true;
            Activate(m);
        }

        public void Level(Sr22Physics m)
        {
            Lateral = "LVL";
            Vertical = "LVL";
            AltArmed = false;
            Activate(m);
            fd.Reset();
        }

        public void HdgBugStep(float deg) => HdgBug = FlightDirector.Wrap(HdgBug + deg * Mathf.Deg2Rad);

        public void AltSelStep(float feet)
        {
            AltSel = Mathf.Max(0, Mathf.Round((AltSel / Sr22Data.Ft + feet) / 100) * 100 * Sr22Data.Ft);
            if (Vertical == "VS") AltArmed = true;
        }

        public void VsStep(float fpm) => VsSel = Mathf.Clamp(VsSel + fpm * Sr22Data.Fpm, -2000 * Sr22Data.Fpm, 2000 * Sr22Data.Fpm);

        /// <summary>Stick commands while engaged (null when off).</summary>
        public (float pitch, float roll)? Update(float dt, Sr22Physics m)
        {
            if (!Engaged) return null;
            if (m.Stalling || m.OnGround || m.Crashed || m.Chute != "stowed")
            {
                Disconnect(m.Stalling ? "stall" : "auto");
                return null;
            }
            float bankCmd = Lateral == "HDG" ? fd.BankForHeading(m, HdgBug) : Lateral == "LVL" ? 0 : rollHold;
            float roll = fd.RollFor(m, bankCmd);

            if (Vertical == "VS" && AltArmed)
            {
                float toGo = AltSel - m.Altitude;
                bool closing = Mathf.Sign(toGo) == Mathf.Sign(VsSel) || VsSel == 0;
                if (closing && Mathf.Abs(toGo) < Mathf.Max(15, Mathf.Abs(m.Velocity.y) * 5))
                {
                    Vertical = "ALT";
                    AltArmed = false;
                    Captured?.Invoke();
                }
            }

            float pitch;
            if (Vertical == "ALT") pitch = fd.PitchForVs(m, Mathf.Clamp((AltSel - m.Altitude) * 0.12f, -5, 5), dt);
            else if (Vertical == "VS") pitch = fd.PitchForVs(m, VsSel, dt);
            else if (Vertical == "LVL") pitch = fd.PitchForVs(m, 0, dt);
            else pitch = fd.PitchForAttitude(m, pitchHold, dt);
            return (pitch, roll);
        }
    }
}
