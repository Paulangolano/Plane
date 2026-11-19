using NUnit.Framework;
using UnityEngine;

namespace Skyreach.Tests
{
    /// <summary>
    /// Edit-mode tests for the SR22-class aircraft (Window ▸ General ▸ Test Runner ▸ EditMode).
    /// Same scenarios as webxr/tests/unit/sr22 and unity/Tests~/LogicTests/Sr22Tests.cs.
    /// </summary>
    public class Sr22LogicTests
    {
        static readonly WorldData World = new WorldData();
        const float DT = 1f / 60f;
        static GroundSample Ground(float x, float z) => World.Sample(x, z);
        static GroundSample Sky(float x, float z) => new GroundSample { Height = -1e6f, Type = SurfaceType.Land };

        static Sr22Physics Aloft(float alt, float kt, float throttle, float flaps = 0, bool assists = false)
        {
            var p = new Sr22Physics { Assists = assists };
            p.Reset(new Vector3(0, alt, 0), 0, false, kt * Sr22Data.Kt, throttle, flaps);
            return p;
        }

        [TestCase(0f, 70f)]
        [TestCase(0.5f, 65f)]
        [TestCase(1f, 60f)]
        public void StallSpeedsMatchTheBook(float flaps, float expectedKt)
        {
            var p = Aloft(1500, 90, 0, flaps);
            float stallKt = -1;
            for (float t = 0; t < 120 && stallKt < 0; t += DT)
            {
                p.Update(DT, new Sr22Input
                {
                    Pitch = Mathf.Clamp((1500 - p.Position.y) * 0.01f - p.Velocity.y * 0.08f + 0.05f, -1, 1),
                    Roll = Mathf.Clamp(-p.Bank * 2, -1, 1),
                }, Sky);
                if (p.Stalling) stallKt = p.IasKt;
            }
            Assert.AreEqual(expectedKt, stallKt, 3f);
        }

        [Test]
        public void TakesOffAroundRotationSpeed()
        {
            var p = new Sr22Physics();
            p.Reset(new Vector3(0, RunwayConfig.Height + Sr22Data.GearHeight, -(WorldData.RunwayHalfLength - 40)), 0);
            float liftKt = -1;
            p.Takeoff += () => liftKt = p.IasKt;
            for (float t = 0; t < 60 && liftKt < 0; t += DT)
                p.Update(DT, new Sr22Input { Throttle = 1, Pitch = p.IasKt > Sr22Data.Vr ? 0.5f : 0 }, Ground);
            Assert.That(liftKt, Is.InRange(65f, 80f));
            Assert.IsFalse(p.Crashed, p.CrashReason);
        }

        [Test]
        public void ParachuteDescendsAtAbout1700Fpm()
        {
            var p = Aloft(1200, 100, 0.5f);
            Assert.AreEqual("deployed", p.DeployChute());
            for (float t = 0; t < 25; t += DT) p.Update(DT, new Sr22Input(), Sky);
            Assert.That(-p.Velocity.y / Sr22Data.Fpm, Is.InRange(1500f, 2000f));
        }

        [Test]
        public void SafeReturnLandsOnTheRunwayFromDownwind()
        {
            var p = new Sr22Physics();
            p.Reset(new Vector3(1500, 500, 2000), Mathf.PI, false, 120 * Sr22Data.Kt, 0.65f);
            var sr = new Sr22SafeReturn(World);
            bool? onRunway = null;
            sr.Complete += ok => onRunway = ok;
            Assert.IsTrue(sr.Activate(p));
            for (float t = 0; t < 600 && !p.Crashed && onRunway == null; t += DT)
            {
                var c = sr.Update(DT, p).Value;
                p.FlapsTarget = c.Flaps;
                p.Update(DT, new Sr22Input { Pitch = c.Pitch, Roll = c.Roll, Yaw = c.Yaw, Throttle = c.Throttle, Brake = c.Brake }, Ground);
            }
            Assert.IsFalse(p.Crashed, p.CrashReason);
            Assert.AreEqual(true, onRunway, $"phase {sr.Phase}");
            Assert.Less(Mathf.Abs(p.Position.x), 12f);
        }
    }
}
