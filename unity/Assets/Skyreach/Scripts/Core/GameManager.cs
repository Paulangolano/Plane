using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM && SKYREACH_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Skyreach
{
    public enum GameState { Ready, Flying, Crashed, Finished, Ended }

    /// <summary>
    /// Builds the whole game at startup and runs the main loop (port of webxr/src/core/game.js).
    /// The scene only needs one GameObject with this component; use Skyreach ▸ Create Game Scene.
    /// Aircraft: the arcade jet and the realistic SR22-class (keys 1 / 2, or the cockpit selector).
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Shaders (assigned automatically by Skyreach ▸ Create Game Scene)")]
        public Shader LitShader;
        public Shader OceanShader;
        public Shader SkyShader;
        public Shader UnlitShader;
        public Shader VignetteShader;

        static readonly Color Gold = SkyreachMaterials.Hex(0xffd23a);

        public GameState State { get; private set; } = GameState.Ready;
        public Aircraft Aircraft { get; private set; }
        public RaceCourse Course { get; private set; }
        public WorldData World { get; private set; }

        SkyreachMaterials mats;
        Transform rig;
        Camera head;
        XRRig xr;
        readonly KeyboardInput keyboard = new KeyboardInput();
        readonly InputState input = new InputState();
        RingVisuals ringVisuals;
        MessagePanel message;
        ComfortVignette vignette;
        GameAudio audioFx;
        SettingsStore settings;
        Transform explosion;
        Material explosionMat;
        GameObject selector;
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

            // Pilot: rig (seat) → head camera + hands. The rig is re-parented to whichever aircraft is active.
            rig = new GameObject("Rig").transform;
            head = new GameObject("Head").AddComponent<Camera>();
            head.tag = "MainCamera";
            head.transform.SetParent(rig, false);
            head.nearClipPlane = 0.05f;
            head.farClipPlane = 60000f;
            head.clearFlags = CameraClearFlags.Skybox;
            head.fieldOfView = 72;
            head.gameObject.AddComponent<AudioListener>();
            xr = rig.gameObject.AddComponent<XRRig>();
            xr.Init(head, rig, mats);

            message = MessagePanel.Create(rig, head);
            vignette = ComfortVignette.Create(head, VignetteShader ? VignetteShader : Shader.Find("Skyreach/Vignette"), settings.ComfortLevel);
            audioFx = gameObject.AddComponent<GameAudio>();
            audioFx.SetMuted(settings.Muted);

            SelectAircraft(settings.Aircraft == "sr22" ? "sr22" : "jet");
            message.Show(Aircraft.StartMessage[0], Aircraft.StartMessage[1], 7);
        }

        // ------------------------------------------------------------------ aircraft
        GameContext Context() => new GameContext
        {
            Game = this, Input = input, Course = Course, World = World, Message = message, Audio = audioFx, Settings = settings, Time = Time.time,
        };

        public void SelectAircraft(string id)
        {
            if (Aircraft != null && Aircraft.Id == id) return;
            rig.SetParent(null, true);
            Aircraft?.Dispose();
            Aircraft = id == "sr22"
                ? new Sr22Aircraft(mats, transform, head, World, settings)
                : (Aircraft)new JetAircraft(mats, transform, head);
            Aircraft.Ctx = Context();
            settings.Aircraft = id;
            settings.Save();

            rig.SetParent(Aircraft.Root, false);
            rig.localPosition = Vector3.zero;
            rig.localRotation = Quaternion.identity;
            message.transform.SetParent(Aircraft.Root, false);
            message.transform.localPosition = new Vector3(0, 0.2f, 2.6f);
            xr.SetLayout(Aircraft.Layout, Aircraft.Root);
            keyboard.SetKeyMap(Aircraft.Keys);
            BuildSelector();

            Aircraft.Takeoff += OnTakeoff;
            Aircraft.Landed += (t, s, secs, c) =>
            {
                if (State != GameState.Crashed && State != GameState.Ended) message.Show(t, s, secs, c);
            };
            Aircraft.Crash += OnCrash;
            ResetGame();
            if (XRRig.Active) calibrateIn = 0.3f;
        }

        /// <summary>A small panel you can touch while parked on the runway to switch aircraft.</summary>
        void BuildSelector()
        {
            selector = new GameObject("AircraftSelector");
            selector.transform.SetParent(Aircraft.Root, false);
            var bg = mats.MakeUnlit("SelectorBg", new Color(0.05f, 0.08f, 0.12f, 0.85f), transparent: true);
            var on = mats.MakeLit("SelectorOn", SkyreachMaterials.Hex(0xffb21a));
            var off = mats.MakeLit("SelectorOff", SkyreachMaterials.Hex(0x2c333b));
            var center = new Vector3(0.42f, -0.06f, 0.5f);
            MeshFactory.Spawn("Bg", MeshFactory.Box(new Vector3(0.26f, 0.15f, 0.005f)), bg, selector.transform, center + new Vector3(0, 0, 0.01f));
            Sr22Builder.Label(selector.transform, "AIRCRAFT", center + new Vector3(0, 0.055f, -0.002f), Quaternion.identity, 0.0018f, Color.white);
            var opts = new[] { ("jet", "ARCADE JET", "selectJet", -0.065f), ("sr22", "SR22", "selectSr22", 0.065f) };
            foreach (var (id, label, action, dx) in opts)
            {
                var pos = center + new Vector3(dx, -0.015f, -0.005f);
                MeshFactory.Spawn(id, MeshFactory.Box(new Vector3(0.11f, 0.06f, 0.012f)), Aircraft.Id == id ? on : off, selector.transform, pos);
                Sr22Builder.Label(selector.transform, label, pos + new Vector3(0, 0, -0.008f), Quaternion.identity, 0.0016f, Color.white);
                if (!Aircraft.Layout.PokeTargets.Exists(t => t.Id == "sel-" + id))
                    Aircraft.Layout.PokeTargets.Add(new PokeTarget { Id = "sel-" + id, Action = action, Position = pos + new Vector3(0, 0, -0.012f), Radius = 0.04f });
            }
        }

        public void EndFlight() => State = GameState.Ended;

        // ------------------------------------------------------------------ game flow
        public void ResetGame()
        {
            var spawn = new Vector3(0, RunwayConfig.Height + Aircraft.GearHeight, -(WorldData.RunwayHalfLength - 40));
            Aircraft.Reset(spawn);
            input.Throttle = 0;
            Course.Reset();
            ringVisuals.Refresh();
            State = GameState.Ready;
            explosion.gameObject.SetActive(false);
            SyncAircraft();
            prevPos = Aircraft.Position;
            prevRot = Aircraft.Rotation;
            message.Show(Aircraft.ReadyMessage[0], Aircraft.ReadyMessage[1], 6);
        }

        void OnTakeoff()
        {
            if (State != GameState.Ready) return;
            State = GameState.Flying;
            Course.Start();
            message.Show("Airborne!", "Fly through the glowing rings in order.", 3);
        }

        void OnCrash(string reason)
        {
            State = GameState.Crashed;
            Course.Running = false;
            explosion.position = Aircraft.Position;
            explosion.gameObject.SetActive(true);
            explosionT = 0;
            audioFx.Crash();
            xr.Pulse(XRNode.LeftHand, 1, 0.4f);
            xr.Pulse(XRNode.RightHand, 1, 0.4f);
            string how = Aircraft.Id == "sr22" ? "Hold Y (or Enter) to restart." : "Press A (or Enter) to restart.";
            message.Show("Crashed!", reason + " " + how, float.PositiveInfinity, SkyreachMaterials.Hex(0xff7a5a));
        }

        void OnRing(RingEvent ev)
        {
            ringVisuals.Refresh();
            xr.Pulse(XRNode.RightHand, 0.6f, 0.08f);
            if (ev.Type == RingEventType.Finish)
            {
                State = GameState.Finished;
                audioFx.Chime(true);
                float best = settings.GetBest(Aircraft.Id);
                bool isBest = best < 0 || ev.Time < best;
                if (isBest) settings.SetBest(Aircraft.Id, ev.Time);
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
            Aircraft.Ctx = Context();

            if (inVR && calibrateIn > 0)
            {
                calibrateIn -= dt;
                if (calibrateIn <= 0) CalibrateSeat();
            }
            if (!(inVR && xr.ReadInput(dt, input))) keyboard.ReadInput(dt, input);
            HandleButtons(inVR);

            if (State != GameState.Crashed)
            {
                prevPos = Aircraft.Position;
                prevRot = Aircraft.Rotation;
                Aircraft.Step(dt, input, World.Sample);
                if (State == GameState.Flying || State == GameState.Finished)
                {
                    Course.Tick(dt);
                    var ev = Course.Check(prevPos, Aircraft.Position);
                    if (ev.Type != RingEventType.None) OnRing(ev);
                }
            }

            SyncAircraft();
            if (!inVR) UpdateDesktopCamera();
            selector.SetActive(State == GameState.Ready && Aircraft.OnGround && Aircraft.Speed < 1);

            float angSpeed = State == GameState.Crashed ? 0 : Quaternion.Angle(prevRot, Aircraft.Rotation) * Mathf.Deg2Rad / Mathf.Max(dt, 1e-4f);
            vignette.Tick(dt, angSpeed);
            Aircraft.UpdateVisuals(dt, input);
            UpdateExplosion(dt);
            audioFx.Tick(Aircraft.Throttle, Aircraft.Speed, Aircraft.Boosting, State == GameState.Crashed, Aircraft.AudioProfile, Aircraft.Rpm);
            input.ClearEdges();
        }

        void HandleButtons(bool inVR)
        {
            if (input.Restart && (State != GameState.Flying || Aircraft.OnGround)) ResetGame();
            else if (input.Restart && State == GameState.Flying)
            {
                if (Time.time - lastRestartPress < 2) ResetGame();
                else message.Show("Restart?", Aircraft.Id == "sr22" ? "Hold again to go back to the runway." : "Press A again to go back to the runway.", 2);
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
            bool canSwitch = Aircraft.OnGround && State != GameState.Flying || State == GameState.Crashed || State == GameState.Ended;
            if (canSwitch && input.Actions.Contains("selectJet")) { SelectAircraft("jet"); return; }
            if (canSwitch && input.Actions.Contains("selectSr22")) { SelectAircraft("sr22"); return; }
            if (State != GameState.Crashed)
                foreach (var action in input.Actions) Aircraft.HandleAction(action);
        }

        /// <summary>Move the rig so the player's real head sits at the pilot's eye point, facing the nose.</summary>
        void CalibrateSeat()
        {
            xr.GetHeadLocal(out var headPos, out var headRot);
            float yaw = headRot.eulerAngles.y;
            rig.localRotation = Quaternion.Euler(0, -yaw, 0);
            rig.localPosition = -(rig.localRotation * headPos);
        }

        void SyncAircraft() => Aircraft.Root.SetPositionAndRotation(Aircraft.Position, Aircraft.Rotation);

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
            Aircraft.SetCockpitView(!chaseView);
            if (chaseView)
            {
                rig.localPosition = new Vector3(Aircraft.Id == "sr22" ? Sr22Layout.CX : 0, 4.5f, -20f);
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
    }
}
