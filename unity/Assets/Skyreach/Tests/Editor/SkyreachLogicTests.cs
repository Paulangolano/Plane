using NUnit.Framework;
using UnityEngine;

namespace Skyreach.Tests
{
    /// <summary>
    /// Edit-mode tests for the game logic. Run them from Window ▸ General ▸ Test Runner ▸ EditMode.
    /// The same checks also run outside Unity via unity/Tests~/run-tests.sh.
    /// </summary>
    public class SkyreachLogicTests
    {
        static readonly WorldData World = new WorldData();
        const float DT = 1f / 72f;

        static GroundSample Ground(float x, float z) => World.Sample(x, z);

        static FlightModel Spawn()
        {
            var f = new FlightModel();
            f.Reset(new Vector3(0, RunwayConfig.Height + FlightConfig.GearHeight, -(WorldData.RunwayHalfLength - 30)), 0);
            return f;
        }

        static void Run(FlightModel f, float seconds, System.Func<FlightModel, FlightInput> input)
        {
            for (float t = 0; t < seconds && !f.Crashed; t += DT) f.Update(DT, input(f), Ground);
        }

        [Test]
        public void ParkedJetStaysPut()
        {
            var f = Spawn();
            var start = f.Position;
            Run(f, 3, _ => default);
            Assert.IsFalse(f.Crashed);
            Assert.Less(Vector3.Distance(start, f.Position), 0.5f);
        }

        [Test]
        public void FullThrottleAndPullBackTakesOff()
        {
            var f = Spawn();
            bool tookOff = false;
            f.Takeoff += () => tookOff = true;
            Run(f, 20, x => new FlightInput { Throttle = 1, Pitch = x.Speed > FlightConfig.TakeoffSpeed && x.PitchAngle < 0.25f ? 0.8f : 0 });
            Assert.IsTrue(tookOff);
            Assert.IsFalse(f.Crashed, f.CrashReason);
            Assert.Greater(f.Altitude, 30f);
        }

        [Test]
        public void RollRightTurnsRight()
        {
            var f = new FlightModel();
            f.Reset(new Vector3(0, 600, 0), 0, false, 100, 0.8f);
            Run(f, 0.5f, _ => new FlightInput { Throttle = 0.8f, Roll = 1 });
            Assert.Greater(f.Bank, 0.3f);
            Run(f, 3, _ => new FlightInput { Throttle = 0.8f });
            Assert.Greater(f.Forward.x, 0.2f);
        }

        [Test]
        public void DivingIntoTheSeaCrashes()
        {
            var f = new FlightModel();
            f.Reset(new Vector3(3000, 80, -3000), 0, false, 110, 1);
            Run(f, 10, _ => new FlightInput { Throttle = 1, Pitch = -1 });
            Assert.IsTrue(f.Crashed);
            StringAssert.Contains("ocean", f.CrashReason);
        }

        [Test]
        public void RingsMustBeFlownInOrder()
        {
            var c = new RaceCourse(World.Rings);
            var r0 = World.Rings[0];
            var r3 = World.Rings[3];
            Assert.AreEqual(RingEventType.None, c.Check(r3.Position - r3.Normal * 5, r3.Position + r3.Normal * 5).Type);
            Assert.AreEqual(RingEventType.Ring, c.Check(r0.Position - r0.Normal * 5, r0.Position + r0.Normal * 5).Type);
            Assert.AreEqual(1, c.Index);
        }

        [Test]
        public void RingsClearTheTerrain()
        {
            Assert.AreEqual(CourseConfig.RingCount, World.Rings.Count);
            foreach (var r in World.Rings)
                Assert.Greater(r.Position.y - CourseConfig.RingRadius, World.HeightAt(r.Position.x, r.Position.z) + 10);
        }

        [Test]
        public void AutopilotCompletesTheCourse()
        {
            var f = Spawn();
            var c = new RaceCourse(World.Rings);
            f.Takeoff += () => c.Start();
            for (float t = 0; t < 900 && !c.Finished && !f.Crashed; t += 1f / 60f)
            {
                var prev = f.Position;
                f.Update(1f / 60f, Autopilot(f, c), Ground);
                c.Tick(1f / 60f);
                c.Check(prev, f.Position);
            }
            Assert.IsFalse(f.Crashed, f.CrashReason);
            Assert.IsTrue(c.Finished, $"stopped at ring {c.Index + 1}");
        }

        static FlightInput Autopilot(FlightModel f, RaceCourse c)
        {
            if (f.OnGround) return new FlightInput { Throttle = 1, Pitch = f.Speed > FlightConfig.TakeoffSpeed ? 0.8f : 0 };
            var ring = c.Current;
            float dist = Vector3.Distance(ring.Position, f.Position);
            Vector3 aim = dist < 260 ? ring.Position : ring.Position - ring.Normal * Mathf.Min(350, dist * 0.45f);
            Vector3 local = Quaternion.Inverse(f.Rotation) * (aim - f.Position);
            float bearing = Mathf.Atan2(local.x, local.z);
            float elevation = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude);
            float roll = Mathf.Clamp((Mathf.Clamp(bearing * 2.2f, -1.25f, 1.25f) - f.Bank) * 2.5f, -1, 1);
            float pitch = Mathf.Clamp(elevation * 4 + Mathf.Abs(f.Bank) * 0.45f, -1, 1);
            Vector3 ahead = f.Position + f.Forward * 250;
            float ground = Mathf.Max(World.HeightAt(ahead.x, ahead.z), World.HeightAt(f.Position.x, f.Position.z), 0);
            if (f.Position.y - ground < 70) pitch = 1;
            return new FlightInput { Throttle = 1, Pitch = pitch, Roll = roll };
        }
    }
}
