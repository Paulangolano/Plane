using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Skyreach
{
    /// <summary>
    /// The realistic SR22-class aircraft (port of webxr/src/aircraft/sr22/sr22Aircraft.js):
    /// side yoke + power lever, flaps, trim, toe brakes, glass cockpit, autopilot, parachute
    /// and Safe Return emergency autoland.
    /// </summary>
    public sealed class Sr22Aircraft : Aircraft
    {
        readonly Sr22Physics model = new Sr22Physics();
        readonly Sr22Visual visual;
        readonly Sr22Avionics avionics;
        public readonly Sr22Autopilot Autopilot = new Sr22Autopilot();
        public readonly Sr22SafeReturn SafeReturn;
        readonly Dictionary<string, Renderer> leds = new Dictionary<string, Renderer>();
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        readonly SettingsStore settings;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        struct ButtonDef { public string Id, Label, Action; public float X, Y, Hold; public int Color; }
        static readonly ButtonDef[] Buttons =
        {
            new ButtonDef { Id = "ap", Label = "AP", X = -0.1f, Y = -0.54f, Action = "apToggle" },
            new ButtonDef { Id = "hdg", Label = "HDG", X = -0.03f, Y = -0.54f, Action = "apHdg" },
            new ButtonDef { Id = "alt", Label = "ALT", X = 0.04f, Y = -0.54f, Action = "apAlt" },
            new ButtonDef { Id = "vs", Label = "VS", X = 0.11f, Y = -0.54f, Action = "apVs" },
            new ButtonDef { Id = "lvl", Label = "LVL", X = 0.18f, Y = -0.54f, Action = "apLvl", Color = 0x1f6fe0 },
            new ButtonDef { Id = "hdgDec", Label = "HDG ◀", X = 0.27f, Y = -0.54f, Action = "hdgDec" },
            new ButtonDef { Id = "hdgInc", Label = "HDG ▶", X = 0.33f, Y = -0.54f, Action = "hdgInc" },
            new ButtonDef { Id = "altDec", Label = "ALT ▼", X = 0.4f, Y = -0.54f, Action = "altDec" },
            new ButtonDef { Id = "altInc", Label = "ALT ▲", X = 0.46f, Y = -0.54f, Action = "altInc" },
            new ButtonDef { Id = "vsDec", Label = "VS ▼", X = 0.53f, Y = -0.54f, Action = "vsDec" },
            new ButtonDef { Id = "vsInc", Label = "VS ▲", X = 0.59f, Y = -0.54f, Action = "vsInc" },
            new ButtonDef { Id = "flapsUp", Label = "FLAP ▲", X = 0.7f, Y = -0.5f, Action = "flapsUp" },
            new ButtonDef { Id = "flapsDown", Label = "FLAP ▼", X = 0.7f, Y = -0.62f, Action = "flapsDown" },
            new ButtonDef { Id = "park", Label = "PARK BRK", X = -0.1f, Y = -0.61f, Action = "parkBrake" },
            new ButtonDef { Id = "assist", Label = "ASSIST", X = -0.02f, Y = -0.61f, Action = "toggleAssists" },
            new ButtonDef { Id = "comfort", Label = "COMFORT", X = 0.06f, Y = -0.61f, Action = "cycleComfort" },
            new ButtonDef { Id = "sound", Label = "SOUND", X = 0.14f, Y = -0.61f, Action = "toggleMute" },
            new ButtonDef { Id = "restart", Label = "RESTART", X = 0.22f, Y = -0.61f, Action = "restart", Hold = 1, Color = 0x8a2a2a },
        };

        public override string Id => "sr22";
        public override string DisplayName => "SR22-class";
        public override float GearHeight => Sr22Data.GearHeight;
        public override string AudioProfile => "prop";
        public override string[] StartMessage => new[] { "SR22 ready", "Right hand: power lever. Left hand: side yoke. Full power, rotate at 72 kt with gentle back pressure." };
        public override string[] ReadyMessage => new[] { "Ready for take-off", "Power up, rotate at 72 kt, climb at 108 kt (Vy)." };
        public override Vector3 Position => model.Position;
        public override Quaternion Rotation => model.Rotation;
        public override bool OnGround => model.OnGround;
        public override bool Crashed => model.Crashed;
        public override float Speed => model.Speed;
        public override float Throttle => model.Throttle;
        public override float Rpm => model.Rpm;
        public Sr22Physics Model => model;

        public Sr22Aircraft(SkyreachMaterials mats, Transform parent, Camera head, WorldData world, SettingsStore settings)
        {
            this.settings = settings;
            model.Assists = settings.Assists;
            visual = Sr22Builder.Build(mats, parent);
            Root = visual.transform;
            avionics = Sr22Avionics.Create(visual, world, head);
            SafeReturn = new Sr22SafeReturn(world);
            BuildButtons(mats);

            Layout.StickHand = XRNode.LeftHand;
            Layout.StickGrip = Sr22Layout.YokeGrip;
            Layout.StickRange = 0.08f;
            Layout.ThrottleHand = XRNode.RightHand;
            Layout.ThrottleGrip = Sr22Layout.PowerGrip;
            Layout.ThrottleRange = 0.14f;
            Layout.TrimHat = true;
            Layout.LeftTrigger = "brakeLeft";
            Layout.RightTrigger = "brakeRight";
            Layout.Buttons["right.primary"] = "flapsDown";
            Layout.Buttons["right.secondary"] = "flapsUp";
            Layout.Buttons["left.primary"] = "apToggle";
            Layout.Buttons["left.stick"] = "recenter";
            Layout.HoldButtons["left.secondary"] = ("restart", 1.2f);
            Layout.PokeTargets.Add(new PokeTarget { Id = "safeReturn", Action = "safeReturn", Hold = 1, Radius = 0.035f, Position = Sr22Layout.SafeReturnButton + new Vector3(0, -0.012f, 0) });
            Layout.GrabTargets.Add(new GrabTarget { Id = "caps", Action = "caps", Position = Sr22Layout.CapsHandle, Radius = 0.12f, PullDistance = 0.1f });

            Keys.Keys["LeftBracket"] = "flapsUp";
            Keys.Keys["RightBracket"] = "flapsDown";
            Keys.Keys["Z"] = "apToggle";
            Keys.Keys["H"] = "apHdg";
            Keys.Keys["K"] = "apAlt";
            Keys.Keys["U"] = "apVs";
            Keys.Keys["L"] = "apLvl";
            Keys.Keys["Minus"] = "hdgDec";
            Keys.Keys["Equals"] = "hdgInc";
            Keys.Keys["PageDown"] = "altDec";
            Keys.Keys["PageUp"] = "altInc";
            Keys.Keys["N"] = "vsDec";
            Keys.Keys["J"] = "vsInc";
            Keys.Keys["P"] = "parkBrake";
            Keys.Keys["O"] = "toggleAssists";
            Keys.HoldKeys["Digit9"] = ("caps", 1f);
            Keys.HoldKeys["Digit0"] = ("safeReturn", 1f);

            model.Takeoff += RaiseTakeoff;
            model.Crash += RaiseCrash;
            model.Landed += (sink, hard, offRunway) =>
            {
                int fpm = Mathf.RoundToInt(sink / Sr22Data.Fpm);
                if (hard) RaiseLanded("Hard landing", $"{fpm} fpm at touchdown. Flare a little more next time.", 3, SkyreachMaterials.Hex(0xffb21a));
                else if (CourseFinished) RaiseLanded("Touchdown!", $"{fpm} fpm{(offRunway ? " on the grass" : "")}. Brake to a stop, or hold Y to fly again.", 6, SkyreachMaterials.Hex(0x7fe08a));
                else RaiseLanded(offRunway ? "Landed on grass" : "Smooth landing", $"{fpm} fpm at touchdown.", 3, Color.white);
            };
            model.ChuteEvent += ok =>
            {
                Ctx?.Audio.Whoosh();
                if (ok) Say("PARACHUTE DEPLOYED", "Engine off. Brace — you will drift down at about 1,700 fpm.", 6, SkyreachMaterials.Hex(0xffb21a));
                else Say("Parachute failed", $"Deployed far too fast ({Mathf.RoundToInt(model.IasKt)} kt). It is only certified up to {Sr22Data.Vpd} kt.", 6, SkyreachMaterials.Hex(0xff7a5a));
                Autopilot.Disconnect("chute");
                SafeReturn.Cancel(model);
            };
            model.ChuteLanded += water =>
            {
                Ctx?.Game.EndFlight();
                Say(water ? "Splashdown under canopy" : "Down safely",
                    water ? "You landed in the water under the parachute. The aircraft floats and rescue is on the way. Hold Y to fly again."
                          : "You landed under the parachute and everyone walks away. Hold Y to fly again.",
                    float.PositiveInfinity, SkyreachMaterials.Hex(0x7fe08a));
            };
            model.EngineOut += () => Say("ENGINE FAILURE", "Out of fuel. Glide at 88 kt toward land, or pull the parachute.", 6, SkyreachMaterials.Hex(0xff7a5a));
            Autopilot.Disconnected += reason =>
            {
                Ctx?.Audio.ApDisconnect();
                Say("Autopilot off", reason == "stall" ? "Disconnected: stall." : reason == "override" ? "You took the controls." : "", 1.6f);
            };
            Autopilot.Captured += () => Say("Altitude captured", "", 1.2f, SkyreachMaterials.Hex(0x3be36a));
            SafeReturn.PhaseChanged += phase =>
            {
                string sub = phase == "ENROUTE" ? "Hands off. Flying to the island runway." : phase == "APPROACH" ? "Approach: flaps, slowing down, glidepath." :
                             phase == "FLARE" ? "Landing." : phase == "ROLLOUT" ? "Touchdown. Braking." : null;
                if (sub != null) Say("SAFE RETURN", sub, 3, SkyreachMaterials.Hex(0x3be36a));
            };
            SafeReturn.Complete += _ =>
            {
                Ctx?.Game.EndFlight();
                Say("Safe Return complete", "Landed and stopped on the runway. Engine off. Hold Y to fly again.", float.PositiveInfinity, SkyreachMaterials.Hex(0x3be36a));
            };
        }

        void Say(string title, string sub, float secs, Color? c = null) => Ctx?.Message.Show(title, sub, secs, c);

        void BuildButtons(SkyreachMaterials mats)
        {
            var group = new GameObject("PanelButtons").transform;
            group.SetParent(Root, false);
            var capMesh = MeshFactory.Box(new Vector3(0.052f, 0.03f, 0.014f));
            var ledMesh = MeshFactory.Box(new Vector3(0.02f, 0.004f, 0.004f));
            var capMats = new Dictionary<int, Material>();
            var ledMat = mats.MakeUnlit("PanelLed", SkyreachMaterials.Hex(0x1a2a1a));
            foreach (var b in Buttons)
            {
                Vector3 pos = Sr22Layout.PanelPoint(b.X, b.Y, 0.007f);
                int color = b.Color != 0 ? b.Color : 0x2c333b;
                if (!capMats.TryGetValue(color, out var mat)) capMats[color] = mat = mats.MakeLit("PanelButton", SkyreachMaterials.Hex(color));
                var cap = MeshFactory.Spawn(b.Id, capMesh, mat, group, pos, Sr22Layout.PanelRotation);
                Sr22Builder.Label(cap.transform, b.Label, new Vector3(0, -0.002f, -0.0075f), Quaternion.identity, 0.0012f, Color.white);
                var led = MeshFactory.Spawn("Led", ledMesh, ledMat, cap.transform, new Vector3(0, 0.0115f, -0.007f));
                leds[b.Id] = led.GetComponent<Renderer>();
                Layout.PokeTargets.Add(new PokeTarget
                {
                    Id = b.Id, Action = b.Action, Hold = b.Hold, Radius = 0.028f,
                    Position = Sr22Layout.PanelPoint(b.X, b.Y, 0.017f),
                });
            }
        }

        public override void Reset(Vector3 spawn)
        {
            model.Reset(spawn, 0);
            model.Assists = settings.Assists;
            Autopilot.Disconnect();
            Autopilot.Lateral = "ROL";
            Autopilot.Vertical = "PIT";
            Autopilot.AltSel = 1500 * Sr22Data.Ft;
            Autopilot.HdgBug = 0;
            SafeReturn.Cancel(model);
        }

        public override bool HandleAction(string action)
        {
            var m = model;
            switch (action)
            {
                case "flapsUp":
                case "flapsDown":
                {
                    float t = m.FlapsStep(action == "flapsDown" ? 1 : -1);
                    Say("Flaps " + new[] { "UP", "50%", "100%" }[Sr22Data.FlapIndex(t)], "", 1);
                    return true;
                }
                case "apToggle":
                    if (SafeReturn.Active) { SafeReturn.Cancel(m); Say("Safe Return cancelled", "You have the controls.", 2); }
                    else if (!m.OnGround) Autopilot.Toggle(m);
                    return true;
                case "apHdg":
                    if (!m.OnGround)
                    {
                        if (Autopilot.Lateral != "HDG") Autopilot.HdgBug = Mathf.Round(m.Heading / (Mathf.PI / 36)) * (Mathf.PI / 36);
                        Autopilot.Heading(m);
                    }
                    return true;
                case "apAlt": if (!m.OnGround) Autopilot.AltitudeHold(m); return true;
                case "apVs": if (!m.OnGround) Autopilot.VerticalSpeed(m); return true;
                case "apLvl": if (!m.OnGround) Autopilot.Level(m); return true;
                case "hdgDec": Autopilot.HdgBugStep(-5); return true;
                case "hdgInc": Autopilot.HdgBugStep(5); return true;
                case "altDec": Autopilot.AltSelStep(-100); return true;
                case "altInc": Autopilot.AltSelStep(100); return true;
                case "vsDec": Autopilot.VsStep(-100); return true;
                case "vsInc": Autopilot.VsStep(100); return true;
                case "parkBrake":
                    m.ParkingBrake = !m.ParkingBrake;
                    Say(m.ParkingBrake ? "Parking brake ON" : "Parking brake OFF", "", 1);
                    return true;
                case "toggleAssists":
                    m.Assists = !m.Assists;
                    settings.Assists = m.Assists;
                    settings.Save();
                    Say(m.Assists ? "Assists ON" : "Assists OFF", m.Assists ? "Envelope protection, yaw damper, rudder steering." : "Full manual. Watch your airspeed!", 2);
                    return true;
                case "caps":
                    if (m.DeployChute() == "ground") Say("Parachute", "Not available on the ground.", 1.5f);
                    return true;
                case "safeReturn":
                    if (!SafeReturn.Activate(m)) Say("Safe Return", m.OnGround ? "Not available on the ground." : "Not available now.", 1.5f);
                    else Autopilot.Disconnect("safeReturn");
                    return true;
            }
            return false;
        }

        public override void Step(float dt, InputState input, Func<float, float, GroundSample> ground)
        {
            var inp = new Sr22Input
            {
                Pitch = input.Pitch, Roll = input.Roll, Yaw = input.Yaw, Throttle = input.Throttle,
                Brake = input.Brake ? 1 : 0, BrakeLeft = input.BrakeLeft, BrakeRight = input.BrakeRight,
                TrimRate = input.TrimRate, RollTrimRate = input.RollTrimRate,
            };
            var sr = SafeReturn.Update(dt, model);
            if (sr.HasValue)
            {
                var c = sr.Value;
                model.FlapsTarget = c.Flaps;
                input.Throttle = c.Throttle; // keep the lever in sync for when you take back control
                inp = new Sr22Input { Pitch = c.Pitch, Roll = c.Roll, Yaw = c.Yaw, Throttle = c.Throttle, Brake = c.Brake };
            }
            else
            {
                var cmd = Autopilot.Update(dt, model);
                if (cmd.HasValue)
                {
                    if (Mathf.Abs(input.Pitch) > 0.4f || Mathf.Abs(input.Roll) > 0.4f) Autopilot.Disconnect("override");
                    else
                    {
                        inp.Pitch = cmd.Value.pitch;
                        inp.Roll = cmd.Value.roll;
                    }
                }
            }
            model.Update(dt, inp, ground);
        }

        public override void UpdateVisuals(float dt, InputState input)
        {
            visual.Animate(dt, model, input.CapsPull);
            var ap = Autopilot;
            foreach (var b in Buttons)
            {
                bool on = b.Id == "ap" ? ap.Engaged : b.Id == "hdg" ? ap.Engaged && ap.Lateral == "HDG" : b.Id == "alt" ? ap.Engaged && ap.Vertical == "ALT"
                        : b.Id == "vs" ? ap.Engaged && ap.Vertical == "VS" : b.Id == "lvl" ? ap.Engaged && ap.Lateral == "LVL"
                        : b.Id == "park" ? model.ParkingBrake : b.Id == "assist" && model.Assists;
                Color c = input.Touching.Contains(b.Id) ? Color.white : on ? (b.Id == "park" ? SkyreachMaterials.Hex(0xffb21a) : SkyreachMaterials.Hex(0x3be36a)) : SkyreachMaterials.Hex(0x1a2a1a);
                mpb.SetColor(ColorId, c);
                leds[b.Id].SetPropertyBlock(mpb);
            }
            avionics.Tick(dt, BuildAvionics());
            Ctx.Audio.SetStallHorn(model.StallWarning && !model.OnGround);
        }

        AvionicsData BuildAvionics()
        {
            var m = model;
            var course = Ctx.Course;
            var rings = new Vector3[course.Rings.Count];
            for (int i = 0; i < rings.Length; i++) rings[i] = course.Rings[i].Position;
            float ringDist = 0;
            if (!course.Finished)
            {
                Vector3 to = course.Current.Position - m.Position;
                ringDist = new Vector2(to.x, to.z).magnitude;
            }
            float ias = m.IasKt;
            return new AvionicsData
            {
                Time = Ctx.Time, Pitch = m.PitchAngle, Bank = m.Bank, Beta = m.Beta, Ias = ias, Tas = m.Speed / Sr22Data.Kt,
                AltFt = m.Altitude / Sr22Data.Ft, AltSelFt = Autopilot.AltSel / Sr22Data.Ft, VsFpm = m.Velocity.y / Sr22Data.Fpm,
                VsSelFpm = Autopilot.Vertical == "VS" ? Autopilot.VsSel / Sr22Data.Fpm : (float?)null,
                Heading = m.Heading, HdgBug = Autopilot.HdgBug, ApEngaged = Autopilot.Engaged, Lateral = Autopilot.Lateral,
                Vertical = Autopilot.Vertical, AltArmed = Autopilot.AltArmed, EspActive = m.EspActive, Flaps = m.Flaps,
                FlapsTarget = m.FlapsTarget, Trim = m.Trim,
                FlapOverspeed = (m.Flaps > 0.6f && ias > Sr22Data.Vfe100 + 5) || (m.Flaps > 0.25f && ias > Sr22Data.Vfe50 + 5),
                StallWarning = m.StallWarning, SafeReturn = SafeReturn.Status, Chute = m.Chute, EngineRunning = m.EngineRunning,
                ParkingBrake = m.ParkingBrake, OnGround = m.OnGround, Rpm = m.Rpm, PercentPower = m.PercentPower,
                Manifold = m.ManifoldPressure, FuelFlow = m.FuelFlow, FuelGal = m.FuelGal, GLoad = m.GLoad, Assists = m.Assists,
                GroundSpeedKt = new Vector2(m.Velocity.x, m.Velocity.z).magnitude / Sr22Data.Kt, Position = m.Position,
                Rings = rings, RingIndex = course.Index, RingCount = course.Rings.Count, RingDistance = ringDist,
                TimeText = CockpitHUD.FormatTime(course.Elapsed),
            };
        }

        public override void SetCockpitView(bool inCockpit) => visual.SetCockpitView(inCockpit);

        public override void Dispose()
        {
            Ctx?.Audio.SetStallHorn(false);
            if (Root) UnityEngine.Object.Destroy(Root.gameObject);
        }
    }
}
