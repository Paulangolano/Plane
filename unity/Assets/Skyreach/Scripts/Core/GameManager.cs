using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM && SKYREACH_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Skyreach
{
    public enum GameState { Ready, Flying, Crashed, Finished }

    /// <summary>
    /// Builds the whole game at startup and runs the main loop (port of webxr/src/core/game.js).
    /// The scene only needs one GameObject with this component; use Skyreach ▸ Create Game Scene.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Shaders (assigned automatically by Skyreach ▸ Create Game Scene)")]
        public Shader LitShader;
        public Shader OceanShader;
        public Shader SkyShader;
        public Shader UnlitShader;
        public Shader VignetteShader;

        static readonly int TakeoffKmh = Mathf.RoundToInt(FlightConfig.TakeoffSpeed * 3.6f);
        static readonly Color Gold = SkyreachMaterials.Hex(0xffd23a);

        public GameState State { get; private set; } = GameState.Ready;
        public FlightModel Flight { get; private set; }
        public RaceCourse Course { get; private set; }
        public WorldData World { get; private set; }

        SkyreachMaterials mats;
        Jet jet;
        Transform rig;
        Camera head;
        XRRig xr;
        readonly KeyboardInput keyboard = new KeyboardInput();
        readonly InputState input = new InputState();
        RingVisuals ringVisuals;
        CockpitHUD hud;
        MessagePanel message;
        ComfortVignette vignette;
        GameAudio audioFx;
        SettingsStore settings;
        Transform explosion;
        Material explosionMat;
        float explosionT = 99;
        Vector3 prevPos;
        Quaternion prevRot;
        float lastRestartPress = -10;
        float calibrateIn = 0.6f;
        bool chaseView;
        float lookYaw, lookPitch = -8f;

        void Start()
        {
            Application.targetFrameRate = 90;
            QualitySettings.vSyncCount = 0;
            settings = SettingsStore.Load();
            mats = new SkyreachMaterials(LitShader, OceanShader, SkyShader, UnlitShader);
            World = new WorldData();

            var worldRoot = new GameObject("World").transform;
            WorldBuilder.SetupSky(mats);
            WorldBuilder.BuildTerrain(World, mats, worldRoot);
            WorldBuilder.BuildOcean(World, mats, worldRoot);
            WorldBuilder.BuildClouds(mats, worldRoot);
            Course = new RaceCourse(World.Rings);
            ringVisuals = new GameObject("Rings").AddComponent<RingVisuals>();
            ringVisuals.transform.SetParent(worldRoot, false);
            ringVisuals.Build(Course, mats);

            explosionMat = new Material(mats.Explosion);
            explosion = MeshFactory.Spawn("Explosion", MeshFactory.Icosphere(2), explosionMat, worldRoot, Vector3.zero).transform;
            explosion.gameObject.SetActive(false);

            // Player: jet → rig (seat) → head camera + hands
            Flight = new FlightModel();
            jet = JetBuilder.Build(mats, transform);
            rig = new GameObject("Rig").transform;
            rig.SetParent(jet.transform, false);
            head = new GameObject("Head").AddComponent<Camera>();
            head.tag = "MainCamera";
            head.transform.SetParent(rig, false);
            head.nearClipPlane = 0.05f;
            head.farClipPlane = 60000f;
            head.clearFlags = CameraClearFlags.Skybox;
            head.fieldOfView = 72;
            head.gameObject.AddComponent<AudioListener>();
            xr = rig.gameObject.AddComponent<XRRig>();
            xr.Init(head, jet.transform, mats);

            hud = CockpitHUD.Create(jet.HudAnchor, head);
            message = MessagePanel.Create(jet.transform, head);
            vignette = ComfortVignette.Create(head, VignetteShader ? VignetteShader : Shader.Find("Skyreach/Vignette"), settings.ComfortLevel);
            audioFx = gameObject.AddComponent<GameAudio>();
            audioFx.SetMuted(settings.Muted);

            Flight.Takeoff += OnTakeoff;
            Flight.Landed += OnLanded;
            Flight.Crash += OnCrash;

            ResetGame();
            message.Show("Skyreach VR",
                $"Grab the throttle with your left hand and push it forward. Pull the stick back at {TakeoffKmh} km/h.", 7);
        }

        // ------------------------------------------------------------------ game flow
        public void ResetGame()
        {
            var spawn = new Vector3(0, RunwayConfig.Height + FlightConfig.GearHeight, -(WorldData.RunwayHalfLength - 40));
            Flight.Reset(spawn, 0);
            input.Throttle = 0;
            Course.Reset();
            ringVisuals.Refresh();
            State = GameState.Ready;
            explosion.gameObject.SetActive(false);
            SyncJet();
            prevPos = Flight.Position;
            prevRot = Flight.Rotation;
            message.Show("Ready for takeoff", $"Throttle up, then pull back at {TakeoffKmh} km/h.", 6);
        }

        void OnTakeoff()
        {
            if (State != GameState.Ready) return;
            State = GameState.Flying;
            Course.Start();
            message.Show("Airborne!", "Fly through the glowing rings in order.", 3);
        }

        void OnLanded(bool offRunway)
        {
            if (State == GameState.Finished)
                message.Show("Touchdown!", offRunway ? "Off-runway, but we will take it." : "Perfect landing. Press A to fly again.", 6, SkyreachMaterials.Hex(0x7fe08a));
            else
                message.Show(offRunway ? "Rough landing" : "Landed", "Throttle up to take off again.", 3);
        }

        void OnCrash(string reason)
        {
            State = GameState.Crashed;
            Course.Running = false;
            explosion.position = Flight.Position;
            explosion.gameObject.SetActive(true);
            explosionT = 0;
            audioFx.Crash();
            xr.Pulse(XRNode.LeftHand, 1, 0.4f);
            xr.Pulse(XRNode.RightHand, 1, 0.4f);
            message.Show("Crashed!", reason + " Press A (or Enter) to restart.", float.PositiveInfinity, SkyreachMaterials.Hex(0xff7a5a));
        }

        void OnRing(RingEvent ev)
        {
            ringVisuals.Refresh();
            xr.Pulse(XRNode.RightHand, 0.6f, 0.08f);
            if (ev.Type == RingEventType.Finish)
            {
                State = GameState.Finished;
                audioFx.Chime(true);
                float best = settings.BestTime;
                bool isBest = best < 0 || ev.Time < best;
                if (isBest) { settings.BestTime = ev.Time; settings.Save(); }
                string sub = "Time " + CockpitHUD.FormatTime(ev.Time) + (isBest ? "" : $"  (best {CockpitHUD.FormatTime(best)})") + ". Now land on the runway!";
                message.Show(isBest ? "New record!" : "Course complete!", sub, 8, Gold);
            }
            else
            {
                audioFx.Chime(false);
                message.Show($"Ring {ev.Index + 1} / {CourseConfig.RingCount}", "", 1.5f, Gold);
            }
        }

        // ------------------------------------------------------------------ loop
        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            bool inVR = XRRig.Active;

            if (inVR && calibrateIn > 0)
            {
                calibrateIn -= dt;
                if (calibrateIn <= 0) CalibrateSeat();
            }
            if (!(inVR && xr.ReadInput(dt, input))) keyboard.ReadInput(dt, input);
            HandleButtons(inVR);

            if (State != GameState.Crashed)
            {
                prevPos = Flight.Position;
                prevRot = Flight.Rotation;
                Flight.Update(dt, input.ToFlightInput(), World.Sample);
                if (State == GameState.Flying || State == GameState.Finished)
                {
                    Course.Tick(dt);
                    var ev = Course.Check(prevPos, Flight.Position);
                    if (ev.Type != RingEventType.None) OnRing(ev);
                }
            }

            SyncJet();
            if (!inVR) UpdateDesktopCamera();

            float angSpeed = State == GameState.Crashed ? 0 : Quaternion.Angle(prevRot, Flight.Rotation) * Mathf.Deg2Rad / Mathf.Max(dt, 1e-4f);
            vignette.Tick(dt, angSpeed);
            jet.SetControls(Flight.PitchInput, Flight.RollInput, Flight.Throttle, Flight.Boosting);
            jet.StickHighlight.SetActive(input.StickNear && !input.StickGrabbed);
            jet.ThrottleHighlight.SetActive(input.ThrottleNear && !input.ThrottleGrabbed);
            UpdateExplosion(dt);
            hud.UpdateHud(dt, BuildHud());
            audioFx.Tick(Flight.Throttle, Flight.Speed, Flight.Boosting, State == GameState.Crashed);
            input.ClearEdges();
        }

        void HandleButtons(bool inVR)
        {
            if (input.Restart && (State != GameState.Flying || Flight.OnGround)) ResetGame();
            else if (input.Restart && State == GameState.Flying)
            {
                // Mid-air restart needs a second press within 2 s, to avoid accidents.
                if (Time.time - lastRestartPress < 2) ResetGame();
                else message.Show("Restart?", "Press A again to go back to the runway.", 2);
                lastRestartPress = Time.time;
            }
            if (input.CycleComfort)
            {
                string level = vignette.Cycle();
                settings.ComfortLevel = vignette.LevelIndex;
                settings.Save();
                message.Show("Comfort vignette", level, 1.5f);
            }
            if (input.ToggleMute)
            {
                settings.Muted = !settings.Muted;
                audioFx.SetMuted(settings.Muted);
                settings.Save();
                message.Show(settings.Muted ? "Sound off" : "Sound on", "", 1.2f);
            }
            if (input.Recenter && inVR)
            {
                CalibrateSeat();
                message.Show("Seat re-centred", "", 1.2f);
            }
            if (input.ToggleView && !inVR) chaseView = !chaseView;
        }

        /// <summary>Move the rig so the player's real head sits at the pilot's eye point, facing the nose.</summary>
        void CalibrateSeat()
        {
            xr.GetHeadLocal(out var headPos, out var headRot);
            float yaw = headRot.eulerAngles.y;
            rig.localRotation = Quaternion.Euler(0, -yaw, 0);
            rig.localPosition = -(rig.localRotation * headPos);
        }

        void SyncJet()
        {
            jet.transform.SetPositionAndRotation(Flight.Position, Flight.Rotation);
        }

        void UpdateDesktopCamera()
        {
#if ENABLE_INPUT_SYSTEM && SKYREACH_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                lookYaw = Mathf.Clamp(lookYaw + d.x * 0.25f, -125, 125);
                lookPitch = Mathf.Clamp(lookPitch - d.y * 0.25f, -70, 70);
            }
#else
            if (Input.GetMouseButton(1))
            {
                lookYaw = Mathf.Clamp(lookYaw + Input.GetAxis("Mouse X") * 4f, -125, 125);
                lookPitch = Mathf.Clamp(lookPitch - Input.GetAxis("Mouse Y") * 4f, -70, 70);
            }
#endif
            if (chaseView)
            {
                rig.localPosition = new Vector3(0, 4.5f, -20f);
                rig.localRotation = Quaternion.Euler(7, 0, 0);
                head.transform.localPosition = Vector3.zero;
                head.transform.localRotation = Quaternion.identity;
            }
            else
            {
                rig.localPosition = Vector3.zero;
                rig.localRotation = Quaternion.identity;
                head.transform.localPosition = Vector3.zero;
                head.transform.localRotation = Quaternion.Euler(-lookPitch, lookYaw, 0);
            }
        }

        void UpdateExplosion(float dt)
        {
            if (!explosion.gameObject.activeSelf) return;
            explosionT += dt;
            explosion.localScale = Vector3.one * (2 + explosionT * 30);
            var c = Color.HSVToRGB(Mathf.Max(0, 0.08f - explosionT * 0.03f), 1, 1);
            c.a = Mathf.Max(0, 0.9f - explosionT * 0.6f);
            explosionMat.color = c;
            if (explosionT > 1.6f) explosion.gameObject.SetActive(false);
        }

        HudData BuildHud()
        {
            var f = Flight;
            var d = new HudData
            {
                Speed = f.Speed,
                Altitude = f.Altitude,
                VerticalSpeed = f.Velocity.y,
                Throttle = f.Throttle,
                BoostFuel = f.BoostFuel,
                Boosting = f.Boosting,
                Stalling = f.Stalling,
                OnGround = f.OnGround,
                Brake = input.Brake,
                Bank = f.Bank,
                PitchAngle = f.PitchAngle,
                Heading = f.Heading,
                RingIndex = Course.Index,
                Elapsed = Course.Elapsed,
                HasRing = !Course.Finished,
            };
            if (d.HasRing)
            {
                Vector3 toRing = Course.Current.Position - f.Position;
                d.RingDistance = toRing.magnitude;
                Vector3 local = Quaternion.Inverse(f.Rotation) * toRing;
                d.RingBearing = Mathf.Atan2(local.x, local.z);
            }
            return d;
        }
    }
}
