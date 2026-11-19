using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Skyreach;
using UnityEngine;

static class Tests
{
    static int pass, fail;
    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) { pass++; Console.WriteLine("ok   - " + name); }
        else { fail++; Console.WriteLine("FAIL - " + name + " " + detail); }
    }

    static readonly WorldData World = new WorldData();
    static GroundSample Ground(float x, float z) => World.Sample(x, z);
    const float DT = 1f / 72f;

    static FlightModel Spawn()
    {
        var f = new FlightModel();
        f.Reset(new Vector3(0, RunwayConfig.Height + FlightConfig.GearHeight, -(WorldData.RunwayHalfLength - 30)), 0);
        return f;
    }

    static void Run(FlightModel f, float seconds, Func<FlightModel, FlightInput> input)
    {
        for (float t = 0; t < seconds; t += DT)
        {
            f.Update(DT, input(f), Ground);
            if (f.Crashed) break;
        }
    }

    static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;

    static FlightInput Autopilot(FlightModel f, RaceCourse c)
    {
        if (f.OnGround) return new FlightInput { Throttle = 1, Pitch = f.Speed > FlightConfig.TakeoffSpeed ? 0.8f : 0 };
        var ring = c.Current;
        Vector3 toRing = ring.Position - f.Position;
        float dist = toRing.magnitude;
        float lead = Math.Min(350, dist * 0.45f);
        Vector3 aim = dist < 260 ? ring.Position : ring.Position - ring.Normal * lead;
        Vector3 local = Quaternion.Inverse(f.Rotation) * (aim - f.Position);
        float bearing = (float)Math.Atan2(local.x, local.z);
        float elevation = (float)Math.Atan2(local.y, Math.Sqrt(local.x * local.x + local.z * local.z));
        float desiredBank = Clamp(bearing * 2.2f, -1.25f, 1.25f);
        float roll = Clamp((desiredBank - f.Bank) * 2.5f, -1, 1);
        float pitch = Clamp(elevation * 4 + Math.Abs(f.Bank) * 0.45f, -1, 1);
        Vector3 ahead = f.Position + f.Forward * 250;
        float clearance = f.Position.y - Math.Max(Math.Max(World.HeightAt(ahead.x, ahead.z), World.HeightAt(f.Position.x, f.Position.z)), 0);
        if (clearance < 70) pitch = 1;
        return new FlightInput { Throttle = 1, Pitch = pitch, Roll = roll };
    }

    static int Main(string[] args)
    {
        // ---- parity with the WebXR world -------------------------------------------------
        var js = JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;
        var islands = js.GetProperty("islands");
        bool islandsMatch = islands.GetArrayLength() == World.Islands.Count;
        for (int i = 0; islandsMatch && i < World.Islands.Count; i++)
        {
            var a = islands[i];
            islandsMatch = Math.Abs(a[0].GetDouble() - World.Islands[i].X) < 1e-9 && Math.Abs(a[1].GetDouble() - World.Islands[i].Z) < 1e-9
                && Math.Abs(a[2].GetDouble() - World.Islands[i].Radius) < 1e-9 && Math.Abs(a[3].GetDouble() - World.Islands[i].Peak) < 1e-9;
        }
        Check("same island layout as WebXR (bit-exact RNG)", islandsMatch);
        double maxErr = 0;
        foreach (var p in js.GetProperty("heights").EnumerateArray())
        {
            float x = (float)p[0].GetDouble(), zWeb = (float)p[1].GetDouble();
            maxErr = Math.Max(maxErr, Math.Abs(World.HeightAt(x, -zWeb) - p[2].GetDouble()));
        }
        Check("terrain heights match WebXR at 400 points", maxErr < 1e-3, $"max error {maxErr}");
        var rings = js.GetProperty("rings");
        double ringErr = 0;
        for (int i = 0; i < rings.GetArrayLength(); i++)
        {
            var r = rings[i];
            var u = World.Rings[i];
            ringErr = Math.Max(ringErr, Math.Abs(u.Position.x - r[0].GetDouble()));
            ringErr = Math.Max(ringErr, Math.Abs(u.Position.y - r[1].GetDouble()));
            ringErr = Math.Max(ringErr, Math.Abs(u.Position.z + r[2].GetDouble())); // z mirrored
            ringErr = Math.Max(ringErr, Math.Abs(u.Normal.z + r[5].GetDouble()));
        }
        Check("ring course matches WebXR (z mirrored)", rings.GetArrayLength() == World.Rings.Count && ringErr < 1e-3, $"err {ringErr}");

        // ---- world sanity --------------------------------------------------------------
        Check("runway is runway", Ground(0, 0).Type == SurfaceType.Runway && Ground(0, 0).Height == RunwayConfig.Height);
        Check("open ocean is water", Ground(5800, 5800).Type == SurfaceType.Water);
        bool ringsClear = true;
        foreach (var r in World.Rings) ringsClear &= r.Position.y - CourseConfig.RingRadius > World.HeightAt(r.Position.x, r.Position.z) + 10;
        Check("rings clear the terrain", ringsClear);
        Check("first ring is ahead of takeoff (+Z)", World.Rings[0].Position.z > WorldData.RunwayHalfLength && World.Rings[0].Normal.z > 0.99f);

        // ---- flight model ----------------------------------------------------------------
        var idle = new FlightInput();
        var fm = Spawn();
        var start = fm.Position;
        Run(fm, 3, _ => idle);
        Check("parked jet stays put", !fm.Crashed && fm.OnGround && Vector3.Distance(fm.Position, start) < 0.5f);

        fm = Spawn();
        bool tookOff = false;
        fm.Takeoff += () => tookOff = true;
        Run(fm, 20, f => new FlightInput { Throttle = 1, Pitch = f.Speed > FlightConfig.TakeoffSpeed && f.PitchAngle < 0.25f ? 0.8f : 0 });
        Check("full throttle + pull back takes off and climbs", tookOff && !fm.Crashed && fm.Altitude > 30, $"alt {fm.Altitude} crashed {fm.CrashReason}");
        Check("takeoff run heads down the runway (+Z)", fm.Position.z > -(WorldData.RunwayHalfLength - 30));

        fm = Spawn();
        Run(fm, 6, _ => new FlightInput { Throttle = 1 });
        Check("without pulling back the jet stays on the ground", fm.OnGround);

        fm = new FlightModel();
        fm.Reset(new Vector3(0, 600, 0), 0, false, 100, 0.8f);
        Run(fm, 0.5f, _ => new FlightInput { Throttle = 0.8f, Roll = 1 });
        Check("roll right banks right", fm.Bank > 0.3f, $"bank {fm.Bank}");
        Run(fm, 3, _ => new FlightInput { Throttle = 0.8f });
        Check("bank right turns right (+X)", fm.Forward.x > 0.2f, $"fwd {fm.Forward}");

        fm = new FlightModel();
        fm.Reset(new Vector3(0, 600, 0), 0, false, 100, 0.8f);
        Run(fm, 0.6f, _ => new FlightInput { Throttle = 0.8f, Pitch = 1 });
        Check("pull back pitches nose up", fm.PitchAngle > 0.2f, $"pitch {fm.PitchAngle}");

        fm = new FlightModel();
        fm.Reset(new Vector3(0, 600, 0), 0, false, 100, 0.8f);
        Run(fm, 1f, _ => new FlightInput { Throttle = 0.8f, Yaw = 1 });
        Check("right rudder yaws right", fm.Forward.x > 0.05f, $"fwd {fm.Forward}");

        fm = new FlightModel();
        fm.Reset(new Vector3(0, 900, 0), 0, false, 20, 0);
        float y0 = fm.Position.y;
        Run(fm, 3, _ => idle);
        Check("slow flight stalls and sinks", fm.Stalling && fm.Position.y < y0 - 10);

        fm = new FlightModel();
        fm.Reset(new Vector3(3000, 80, -3000), 0, false, 110, 1);
        string reason = null;
        fm.Crash += r => reason = r;
        Run(fm, 10, _ => new FlightInput { Throttle = 1, Pitch = -1 });
        Check("diving into the sea crashes", reason != null && reason.Contains("ocean"), reason ?? "no crash");

        fm = new FlightModel();
        fm.Reset(new Vector3(0, RunwayConfig.Height + FlightConfig.GearHeight + 12, -(WorldData.RunwayHalfLength - 50)), 0, false, 50, 0.4f);
        fm.PitchUp(-0.05f);
        bool landed = false;
        fm.Landed += _ => landed = true;
        Run(fm, 8, f => new FlightInput { Throttle = 0.4f, Brake = f.OnGround });
        Check("gentle approach lands on the runway", landed && !fm.Crashed, fm.CrashReason);

        fm = new FlightModel();
        fm.Reset(new Vector3(0, 800, 0), 0, false, 100, 1);
        Run(fm, 2, _ => new FlightInput { Throttle = 1, Boost = true });
        float low = fm.BoostFuel;
        Run(fm, 2, _ => new FlightInput { Throttle = 1 });
        Check("boost drains then recharges", low < FlightConfig.BoostDuration - 1.5f && fm.BoostFuel > low);

        // ---- race course -----------------------------------------------------------------
        var course = new RaceCourse(World.Rings);
        var ring = World.Rings[0];
        var ev = course.Check(ring.Position - ring.Normal * 5, ring.Position + ring.Normal * 5);
        Check("flying through ring 1 counts", ev.Type == RingEventType.Ring && course.Index == 1);
        ev = course.Check(World.Rings[3].Position - World.Rings[3].Normal * 5, World.Rings[3].Position + World.Rings[3].Normal * 5);
        Check("skipping ahead does not count", ev.Type == RingEventType.None && course.Index == 1);
        ring = World.Rings[1];
        ev = course.Check(ring.Position + ring.Normal * 5, ring.Position - ring.Normal * 5);
        Check("flying through backwards does not count", ev.Type == RingEventType.None);
        var off = new Vector3(CourseConfig.RingRadius * 1.5f, 0, 0);
        ev = course.Check(ring.Position - ring.Normal * 5 + off, ring.Position + ring.Normal * 5 + off);
        Check("missing the ring does not count", ev.Type == RingEventType.None);
        course.Index = World.Rings.Count - 1;
        ring = World.Rings[course.Index];
        ev = course.Check(ring.Position - ring.Normal * 5, ring.Position + ring.Normal * 5);
        Check("last ring finishes the course", ev.Type == RingEventType.Finish && course.Finished);

        // ---- autopilot flies the whole course from a standing start ------------------------
        {
            var f = Spawn();
            var c = new RaceCourse(World.Rings);
            int passed = 0;
            f.Takeoff += () => c.Start();
            float t = 0;
            for (; t < 900 && !c.Finished && !f.Crashed; t += 1f / 60f)
            {
                var prev = f.Position;
                f.Update(1f / 60f, Autopilot(f, c), Ground);
                c.Tick(1f / 60f);
                if (c.Check(prev, f.Position).Type != RingEventType.None) passed++;
            }
            Check($"autopilot completes all {CourseConfig.RingCount} rings (lap {c.Elapsed:0.0} s)", c.Finished && !f.Crashed && passed == CourseConfig.RingCount,
                $"passed {passed}, crashed: {f.CrashReason}");
        }

        Console.WriteLine($"\n{pass} passed, {fail} failed");
        return fail == 0 ? 0 : 1;
    }
}
