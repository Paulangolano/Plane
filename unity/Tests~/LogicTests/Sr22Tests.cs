using System;
using Skyreach;
using UnityEngine;

/// <summary>SR22-class checks for the Unity port (same scenarios as webxr/tests/unit/sr22).</summary>
static class Sr22Tests
{
    const float DT = 1f / 60f;
    static WorldData world;
    static GroundSample Ground(float x, float z) => world.Sample(x, z);
    static GroundSample Sky(float x, float z) => new GroundSample { Height = -1e6f, Type = SurfaceType.Land };
    static float Clamp(float v) => Math.Max(-1, Math.Min(1, v));

    static Sr22Physics Aloft(float alt = 1500, float kt = 110, float throttle = 0.6f, float flaps = 0, bool assists = false, float heading = 0, float x = 0, float z = 0)
    {
        var p = new Sr22Physics { Assists = assists };
        p.Reset(new Vector3(x, alt, z), heading, false, kt * Sr22Data.Kt, throttle, flaps);
        return p;
    }

    static void Fly(Sr22Physics p, float seconds, Func<Sr22Physics, Sr22Input> input, Func<float, float, GroundSample> sample)
    {
        for (float t = 0; t < seconds && !p.Crashed && !p.Done; t += DT) p.Update(DT, input(p), sample);
    }

    public static void Run(WorldData w, Action<string, bool, string> check)
    {
        world = w;

        // Take-off
        {
            var p = new Sr22Physics();
            p.Reset(new Vector3(0, RunwayConfig.Height + Sr22Data.GearHeight, -(WorldData.RunwayHalfLength - 40)), 0);
            float liftKt = -1;
            p.Takeoff += () => liftKt = p.IasKt;
            Fly(p, 60, q => new Sr22Input { Throttle = 1, Pitch = q.IasKt > Sr22Data.Vr ? 0.5f : 0, Roll = Clamp(-q.Bank * 2) }, Ground);
            check($"SR22 lifts off at 65-80 kt (got {liftKt:0})", liftKt > 65 && liftKt < 80 && !p.Crashed, p.CrashReason);
        }

        // Stall speeds per flap setting
        float[] expected = { 70, 65, 60 };
        float[] flaps = { 0, 0.5f, 1 };
        for (int i = 0; i < 3; i++)
        {
            var p = Aloft(kt: 90, throttle: 0, flaps: flaps[i]);
            float stallKt = -1;
            Fly(p, 120, q =>
            {
                if (q.Stalling && stallKt < 0) stallKt = q.IasKt;
                return new Sr22Input { Pitch = Clamp((1500 - q.Position.y) * 0.01f - q.Velocity.y * 0.08f + 0.05f), Roll = Clamp(-q.Bank * 2) };
            }, Sky);
            check($"SR22 stalls near {expected[i]} kt with flaps {flaps[i]} (got {stallKt:0.0})", Math.Abs(stallKt - expected[i]) < 3, "");
        }

        // Climb at Vy
        {
            var p = Aloft(alt: 300, kt: 108, throttle: 1);
            float vs = 0; int n = 0;
            Fly(p, 60, q => { vs += q.Velocity.y; n++; return new Sr22Input { Throttle = 1, Pitch = Clamp((q.IasKt - Sr22Data.Vy) * 0.04f), Roll = Clamp(-q.Bank * 2) }; }, Sky);
            float fpm = vs / n / Sr22Data.Fpm;
            check($"SR22 climbs 1,100-1,500 fpm at Vy (got {fpm:0})", fpm > 1100 && fpm < 1500, "");
        }

        // Banking turns right
        {
            var p = Aloft(kt: 120, throttle: 0.6f);
            Fly(p, 1.2f, q => new Sr22Input { Throttle = 0.6f, Roll = 1 }, Sky);
            bool banked = p.Bank > 0.3f;
            Fly(p, 8, q => new Sr22Input { Throttle = 0.6f, Roll = Clamp((0.5f - q.Bank) * 2), Pitch = 0.15f }, Sky);
            check($"SR22 roll right banks and turns right (fwd.x {p.Forward.x:0.00})", banked && p.Forward.x > 0.3f, "");
        }

        // Envelope protection
        {
            var p = Aloft(kt: 90, throttle: 0, assists: true);
            bool stalled = false;
            Fly(p, 40, q => { stalled |= q.Stalling; return new Sr22Input { Pitch = 1 }; }, Sky);
            check("SR22 envelope protection prevents the stall", !stalled, "");
        }

        // Parachute
        {
            var p = Aloft(alt: 1200, kt: 100, throttle: 0.5f);
            string r = p.DeployChute();
            Fly(p, 25, q => new Sr22Input(), Sky);
            float fpm = -p.Velocity.y / Sr22Data.Fpm;
            check($"SR22 parachute descends at 1,500-2,000 fpm (got {fpm:0})", r == "deployed" && fpm > 1500 && fpm < 2000, "");
        }

        // Autopilot HDG + ALT
        {
            var p = Aloft(alt: 900, kt: 120, throttle: 0.65f);
            var ap = new Sr22Autopilot { HdgBug = Mathf.PI / 2 };
            ap.AltitudeHold(p);
            ap.Heading(p);
            float maxDev = 0;
            for (float t = 0; t < 60; t += DT)
            {
                var cmd = ap.Update(DT, p);
                p.Update(DT, new Sr22Input { Throttle = 0.65f, Pitch = cmd?.pitch ?? 0, Roll = cmd?.roll ?? 0 }, Sky);
                if (t > 5) maxDev = Math.Max(maxDev, Math.Abs(p.Altitude - ap.AltSel));
            }
            bool onHeading = Math.Abs(FlightDirector.Wrap(p.Heading - Mathf.PI / 2)) < 0.05f;
            check($"SR22 autopilot turns to the heading bug and holds altitude (±{maxDev / Sr22Data.Ft:0} ft)", onHeading && maxDev < 50 * Sr22Data.Ft, "");
        }

        // Safe Return from several starting points
        var starts = new (string name, float alt, float x, float z, float heading, float kt)[]
        {
            ("already on final", 350, 150, -(WorldData.RunwayHalfLength + 4000), -0.1f, 120),
            ("downwind, heading away", 500, 1500, 2000, Mathf.PI, 120),
            ("north-east, low", 250, 2500, -1500, Mathf.PI - 2.5f, 120),
            ("just after take-off", 120, 0, 900, Mathf.PI, 90),
        };
        foreach (var s in starts)
        {
            var p = Aloft(alt: s.alt, kt: s.kt, throttle: 0.65f, x: s.x, z: s.z, heading: s.heading, assists: true);
            var sr = new Sr22SafeReturn(world);
            bool? onRunway = null;
            sr.Complete += ok => onRunway = ok;
            sr.Activate(p);
            for (float t = 0; t < 600 && !p.Crashed && onRunway == null; t += DT)
            {
                var c = sr.Update(DT, p).Value;
                p.FlapsTarget = c.Flaps;
                p.Update(DT, new Sr22Input { Pitch = c.Pitch, Roll = c.Roll, Yaw = c.Yaw, Throttle = c.Throttle, Brake = c.Brake }, Ground);
            }
            check($"SR22 Safe Return lands and stops on the runway: {s.name}",
                !p.Crashed && onRunway == true && !p.EngineRunning && Math.Abs(p.Position.x) < 12,
                $"phase {sr.Phase}, crashed: {p.CrashReason}, x={p.Position.x:0.0} z={p.Position.z:0}");
        }
    }
}
