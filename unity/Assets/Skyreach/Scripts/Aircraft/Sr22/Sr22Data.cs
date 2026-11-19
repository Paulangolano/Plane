using UnityEngine;

namespace Skyreach
{
    /// <summary>
    /// SR22-class performance data (based on the Cirrus SR22 G7, IO-550-N 310 hp).
    /// Mirrors webxr/src/aircraft/sr22/sr22Data.js. SI units inside; displays use kt / ft / fpm.
    /// </summary>
    public static class Sr22Data
    {
        public const float Kt = 0.514444f; // m/s per knot
        public const float Ft = 0.3048f;   // m per foot
        public const float Fpm = Ft / 60f; // m/s per foot-per-minute

        public const float Mass = 1550f;
        public const float WingArea = 13.46f;
        public const float WingSpan = 11.68f;
        public const float ClAlpha = 5.2f;
        public const float Cl0 = 0.25f;
        public static readonly float[] ClMax = { 1.42f, 1.68f, 1.94f };
        public static readonly float[] FlapCl = { 0f, 0.3f, 0.6f };
        public const float Cd0 = 0.029f;
        public const float InducedK = 0.042f;
        public static readonly float[] FlapCd = { 0f, 0.012f, 0.045f };
        public const float PropIdleDragCd = 0.009f;
        public const float MaxPower = 231000f;
        public const float StaticThrust = 3300f;
        public const float IdleRpm = 700f;
        public const float MaxRpm = 2700f;
        public const float PitchGain = 3.2f;
        public const float ElevatorAlpha = 0.25f;
        public static readonly float[] TrimAlpha = { -0.06f, 0.16f };
        public const float MaxRollRate = 1.0f;
        public const float MaxYawRate = 0.35f;
        public const float Weathervane = 2.2f;
        public const float Dihedral = 0.9f;
        public const float SideForce = 0.9f;
        public const float RefSpeed = 60f;
        public const float GearHeight = 1.45f;
        public const float RollingFriction = 0.025f;
        public const float BrakeFriction = 0.45f;
        public const float RotateMinSpeed = 25f;
        public const float TailStrikePitch = 0.21f;
        public const float Vso = 60, Vs1 = 70, Vr = 72, Vx = 85, Vy = 108, Vfe50 = 150, Vfe100 = 110, Vno = 177, Vne = 205, Vpd = 140;
        public const float StructuralLimit = 240f;
        public const float SafeSink = 3.0f;
        public const float HardSink = 4.5f;
        public const float ChuteCdA = 335f;
        public const float ChuteInflateTime = 3f;
        public const float ChuteMaxDeploySpeed = 165f;
        public static readonly float[] FlapDetents = { 0f, 0.5f, 1f };
        public const float FlapRate = 0.12f;

        public static int FlapIndex(float f) => f < 0.25f ? 0 : f < 0.75f ? 1 : 2;

        static float AtFlaps(float[] table, float flaps)
        {
            float t = Mathf.Clamp01(flaps) * 2f;
            return t <= 1 ? table[0] + (table[1] - table[0]) * t : table[1] + (table[2] - table[1]) * (t - 1);
        }

        public static float StallAlpha(float flaps) => (AtFlaps(ClMax, flaps) - Cl0 - AtFlaps(FlapCl, flaps)) / ClAlpha;

        public static float LiftCoefficient(float alpha, float flaps)
        {
            float clMax = AtFlaps(ClMax, flaps);
            float baseCl = Cl0 + AtFlaps(FlapCl, flaps);
            float aStall = (clMax - baseCl) / ClAlpha;
            const float aNegStall = -0.17f;
            if (alpha > aStall)
            {
                float over = alpha - aStall;
                if (over < 0.3f) return Mathf.Max(clMax - over * 2.6f, 0.62f * clMax);
                return Mathf.Max(Mathf.Sin(2 * alpha), 0.3f);
            }
            if (alpha < aNegStall)
            {
                float clMin = baseCl + ClAlpha * aNegStall;
                return Mathf.Min(clMin + (aNegStall - alpha) * 2.6f, 0.6f * clMin);
            }
            return baseCl + ClAlpha * alpha;
        }

        public static float DragCoefficient(float cl, float flaps, float power)
        {
            float idle = PropIdleDragCd * Mathf.Max(0, 1 - power * 4);
            return Cd0 + InducedK * cl * cl + AtFlaps(FlapCd, flaps) + idle;
        }

        public static float DensityRatio(float alt) => Mathf.Exp(-Mathf.Max(alt, 0) / 8500f);

        public static float SmoothStep01(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }
    }
}
