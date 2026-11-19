using System;
using UnityEngine;
using UnityEngine.XR;

namespace Skyreach
{
    /// <summary>The arcade jet: forgiving controls and an afterburner (the original Skyreach aircraft).</summary>
    public sealed class JetAircraft : Aircraft
    {
        static readonly int TakeoffKmh = Mathf.RoundToInt(FlightConfig.TakeoffSpeed * 3.6f);
        readonly FlightModel model = new FlightModel();
        readonly Jet jet;
        readonly CockpitHUD hud;

        public override string Id => "jet";
        public override string DisplayName => "Arcade Jet";
        public override float GearHeight => FlightConfig.GearHeight;
        public override string AudioProfile => "jet";
        public override string[] StartMessage => new[] { "Skyreach VR", $"Grab the throttle with your left hand and push it forward. Pull the stick back at {TakeoffKmh} km/h." };
        public override string[] ReadyMessage => new[] { "Ready for takeoff", $"Throttle up, then pull back at {TakeoffKmh} km/h." };
        public override Vector3 Position => model.Position;
        public override Quaternion Rotation => model.Rotation;
        public override bool OnGround => model.OnGround;
        public override bool Crashed => model.Crashed;
        public override float Speed => model.Speed;
        public override float Throttle => model.Throttle;
        public override bool Boosting => model.Boosting;
        public FlightModel Model => model;

        public JetAircraft(SkyreachMaterials mats, Transform parent, Camera head)
        {
            jet = JetBuilder.Build(mats, parent);
            Root = jet.transform;
            hud = CockpitHUD.Create(jet.HudAnchor, head);
            Layout = new ControlLayout
            {
                StickHand = XRNode.RightHand,
                StickGrip = CockpitLayout.StickGrip,
                ThrottleHand = XRNode.LeftHand,
                ThrottleGrip = CockpitLayout.ThrottleGrip,
                StickRange = 0.09f,
                ThrottleRange = 0.2f,
                LeftTrigger = "brake",
                RightTrigger = "boost",
            };
            Layout.Buttons["right.primary"] = "restart";
            Layout.Buttons["right.secondary"] = "cycleComfort";
            Layout.Buttons["left.primary"] = "cycleComfort";
            Layout.Buttons["left.secondary"] = "toggleMute";
            Layout.Buttons["left.stick"] = "recenter";

            model.Takeoff += RaiseTakeoff;
            model.Crash += RaiseCrash;
            model.Landed += offRunway =>
            {
                if (CourseFinished)
                    RaiseLanded("Touchdown!", offRunway ? "Off-runway, but we will take it." : "Perfect landing. Press A to fly again.", 6, SkyreachMaterials.Hex(0x7fe08a));
                else
                    RaiseLanded(offRunway ? "Rough landing" : "Landed", "Throttle up to take off again.", 3, Color.white);
            };
        }

        public override void Reset(Vector3 spawn) => model.Reset(spawn, 0);

        public override void Step(float dt, InputState input, Func<float, float, GroundSample> ground) =>
            model.Update(dt, input.ToFlightInput(), ground);

        public override void UpdateVisuals(float dt, InputState input)
        {
            jet.SetControls(model.PitchInput, model.RollInput, model.Throttle, model.Boosting);
            jet.StickHighlight.SetActive(input.StickNear && !input.StickGrabbed);
            jet.ThrottleHighlight.SetActive(input.ThrottleNear && !input.ThrottleGrabbed);
            hud.UpdateHud(dt, BuildHud());
        }

        HudData BuildHud()
        {
            var f = model;
            var course = Ctx.Course;
            var d = new HudData
            {
                Speed = f.Speed, Altitude = f.Altitude, VerticalSpeed = f.Velocity.y, Throttle = f.Throttle,
                BoostFuel = f.BoostFuel, Boosting = f.Boosting, Stalling = f.Stalling, OnGround = f.OnGround,
                Brake = Ctx.Input.Brake, Bank = f.Bank, PitchAngle = f.PitchAngle, Heading = f.Heading,
                RingIndex = course.Index, Elapsed = course.Elapsed, HasRing = !course.Finished,
            };
            if (d.HasRing)
            {
                Vector3 toRing = course.Current.Position - f.Position;
                d.RingDistance = toRing.magnitude;
                Vector3 local = Quaternion.Inverse(f.Rotation) * toRing;
                d.RingBearing = Mathf.Atan2(local.x, local.z);
            }
            return d;
        }

        public override void Dispose()
        {
            if (Root) UnityEngine.Object.Destroy(Root.gameObject);
        }
    }
}
