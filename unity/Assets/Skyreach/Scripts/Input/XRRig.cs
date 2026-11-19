using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Skyreach
{
    /// <summary>
    /// The pilot's seat: head camera and two hands, tracked through Unity's XR input API (OpenXR on Quest).
    /// Controls follow the active aircraft's <see cref="ControlLayout"/>: grabbing a stick/yoke and a
    /// throttle/power lever, thumbstick fallbacks, a trim hat, triggers, tap and hold buttons,
    /// fingertip "poke" buttons and pullable handles (port of webxr/src/input/xrInput.js).
    /// </summary>
    public sealed class XRRig : MonoBehaviour
    {
        const float GrabThreshold = 0.6f;
        const float NearDistance = 0.16f;
        const float FingertipReach = 0.04f;

        public Camera Head;
        public Transform LeftHand, RightHand;
        public Transform CockpitSpace;
        public ControlLayout Layout;

        Transform leftFingers, rightFingers;
        readonly Dictionary<XRNode, Grab> grabs = new Dictionary<XRNode, Grab>();
        readonly Dictionary<string, bool> prevButtons = new Dictionary<string, bool>();
        readonly Dictionary<string, float> holdTimes = new Dictionary<string, float>();
        readonly Dictionary<string, float> touchTimes = new Dictionary<string, float>();
        static readonly InputFeatureUsage<Vector3> PointerPosition = new InputFeatureUsage<Vector3>("PointerPosition");
        static readonly InputFeatureUsage<Quaternion> PointerRotation = new InputFeatureUsage<Quaternion>("PointerRotation");

        sealed class Grab
        {
            public string Kind; // stick | throttle | target
            public Vector3 Origin;
            public float Start;
            public GrabTarget Target;
            public bool Fired;
        }

        public static bool Active => XRSettings.isDeviceActive;

        public void Init(Camera head, Transform cockpitSpace, SkyreachMaterials mats)
        {
            Head = head;
            CockpitSpace = cockpitSpace;
            LeftHand = CreateHand("LeftHand", mats, SkyreachMaterials.Hex(0xf08a24), out leftFingers);
            RightHand = CreateHand("RightHand", mats, SkyreachMaterials.Hex(0x3a8dde), out rightFingers);
            Application.onBeforeRender += UpdatePoses;
            SetSeatedTracking();
        }

        public void SetLayout(ControlLayout layout, Transform cockpitSpace)
        {
            Layout = layout;
            CockpitSpace = cockpitSpace;
            grabs.Clear();
            holdTimes.Clear();
            touchTimes.Clear();
        }

        void OnDestroy() => Application.onBeforeRender -= UpdatePoses;

        static void SetSeatedTracking()
        {
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var s in subsystems) s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
        }

        Transform CreateHand(string name, SkyreachMaterials mats, Color colour, out Transform fingers)
        {
            var hand = new GameObject(name).transform;
            hand.SetParent(transform, false);
            var mat = mats.MakeLit(name + "Glove", colour);
            MeshFactory.Spawn("Palm", MeshFactory.Box(new Vector3(0.07f, 0.03f, 0.09f)), mat, hand, new Vector3(0, -0.01f, -0.02f));
            fingers = new GameObject("Fingers").transform;
            fingers.SetParent(hand, false);
            var finger = MeshFactory.Box(new Vector3(0.014f, 0.018f, 0.06f));
            for (int i = 0; i < 4; i++)
                MeshFactory.Spawn("Finger", finger, mat, fingers, new Vector3(-0.025f + i * 0.017f, -0.01f, 0.05f));
            MeshFactory.Spawn("Thumb", MeshFactory.Box(new Vector3(0.016f, 0.018f, 0.05f)), mat, hand,
                new Vector3(name == "LeftHand" ? 0.045f : -0.045f, -0.005f, 0), Quaternion.Euler(0, name == "LeftHand" ? 34 : -34, 0));
            hand.gameObject.SetActive(false);
            return hand;
        }

        /// <summary>Runs right before rendering for the lowest possible tracking latency.</summary>
        void UpdatePoses()
        {
            if (!Active) return;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!head.isValid) head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.TryGetFeatureValue(CommonUsages.centerEyePosition, out var hp) ||
                head.TryGetFeatureValue(CommonUsages.devicePosition, out hp))
                Head.transform.localPosition = hp;
            if (head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out var hr) ||
                head.TryGetFeatureValue(CommonUsages.deviceRotation, out hr))
                Head.transform.localRotation = hr;
            UpdateHand(XRNode.LeftHand, LeftHand);
            UpdateHand(XRNode.RightHand, RightHand);
        }

        static void UpdateHand(XRNode node, Transform hand)
        {
            var dev = InputDevices.GetDeviceAtXRNode(node);
            Vector3 pos = default;
            bool tracked = dev.isValid && dev.TryGetFeatureValue(CommonUsages.devicePosition, out pos);
            hand.gameObject.SetActive(tracked);
            if (!tracked) return;
            dev.TryGetFeatureValue(CommonUsages.deviceRotation, out var rot);
            hand.localPosition = pos;
            hand.localRotation = rot;
        }

        public void GetHeadLocal(out Vector3 pos, out Quaternion rot)
        {
            UpdatePoses();
            pos = Head.transform.localPosition;
            rot = Head.transform.localRotation;
        }

        bool Pressed(InputDevice dev, InputFeatureUsage<bool> usage, string key, out bool down)
        {
            dev.TryGetFeatureValue(usage, out down);
            prevButtons.TryGetValue(key, out bool was);
            prevButtons[key] = down;
            return down && !was;
        }

        /// <summary>Index fingertip in the aircraft's frame: a few cm along the controller's pointing ray.</summary>
        Vector3 Fingertip(XRNode node, InputDevice dev, Transform hand)
        {
            if (dev.TryGetFeatureValue(PointerPosition, out var pp) && dev.TryGetFeatureValue(PointerRotation, out var pr))
                return CockpitSpace.InverseTransformPoint(transform.TransformPoint(pp + pr * Vector3.forward * FingertipReach));
            return CockpitSpace.InverseTransformPoint(hand.TransformPoint(new Vector3(0, -0.02f, 0.08f)));
        }

        public bool ReadInput(float dt, InputState s)
        {
            if (!Active || Layout == null) return false;
            UpdatePoses();
            s.StickGrabbed = s.ThrottleGrabbed = s.StickNear = s.ThrottleNear = false;
            s.TrimRate = s.RollTrimRate = 0;
            s.CapsPull = 0;
            s.Touching.Clear();
            bool any = false;

            foreach (var node in new[] { XRNode.LeftHand, XRNode.RightHand })
            {
                var dev = InputDevices.GetDeviceAtXRNode(node);
                if (!dev.isValid) continue;
                any = true;
                string side = node == XRNode.LeftHand ? "left" : "right";
                Transform handT = node == XRNode.LeftHand ? LeftHand : RightHand;
                dev.TryGetFeatureValue(CommonUsages.grip, out float squeeze);
                Vector3 pos = CockpitSpace.InverseTransformPoint(handT.position);
                if (Layout.StickHand == node) s.StickNear = Vector3.Distance(pos, Layout.StickGrip) < NearDistance;
                if (Layout.ThrottleHand == node) s.ThrottleNear = Vector3.Distance(pos, Layout.ThrottleGrip) < NearDistance;

                // Start / end grabs
                grabs.TryGetValue(node, out var grab);
                if (squeeze > GrabThreshold)
                {
                    if (grab == null)
                    {
                        var target = Layout.GrabTargets.Find(t => Vector3.Distance(pos, t.Position) < t.Radius);
                        if (target != null) grab = new Grab { Kind = "target", Target = target, Origin = pos };
                        else if (Layout.StickHand == node) grab = new Grab { Kind = "stick", Origin = pos };
                        else if (Layout.ThrottleHand == node) grab = new Grab { Kind = "throttle", Origin = pos, Start = s.Throttle };
                        if (grab != null)
                        {
                            grabs[node] = grab;
                            Pulse(node, 0.25f, 0.03f);
                        }
                    }
                }
                else
                {
                    grabs.Remove(node);
                    grab = null;
                }

                dev.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis);
                if (grab?.Kind == "stick")
                {
                    Vector3 d = pos - grab.Origin;
                    s.Roll = Mathf.Clamp(d.x / Layout.StickRange, -1, 1);
                    s.Pitch = Mathf.Clamp(-d.z / Layout.StickRange, -1, 1); // pulling toward you (-Z) = nose up
                    s.StickGrabbed = true;
                    if (Layout.TrimHat)
                    {
                        s.TrimRate = InputState.Deadzone(-axis.y); // hat back = nose-up trim
                        s.RollTrimRate = InputState.Deadzone(axis.x);
                    }
                }
                else if (grab?.Kind == "throttle")
                {
                    s.Throttle = Mathf.Clamp01(grab.Start + (pos.z - grab.Origin.z) / Layout.ThrottleRange);
                    s.ThrottleGrabbed = true;
                }
                else if (grab?.Kind == "target")
                {
                    float pull = Mathf.Max(0, grab.Origin.y - pos.y);
                    s.CapsPull = Mathf.Min(pull, 0.2f);
                    if (!grab.Fired && pull > grab.Target.PullDistance)
                    {
                        grab.Fired = true;
                        s.Fire(grab.Target.Action);
                        Pulse(node, 1, 0.2f);
                    }
                }

                // Thumbsticks: right flies (unless a hand is on the stick), left = power + rudder
                // (unless it's the trim hat).
                bool usingHat = grab?.Kind == "stick" && Layout.TrimHat;
                bool stickHeld = grabs.TryGetValue(Layout.StickHand, out var sg) && sg.Kind == "stick";
                if (node == XRNode.RightHand && !usingHat && !stickHeld)
                {
                    s.Roll = InputState.Deadzone(axis.x);
                    s.Pitch = InputState.Deadzone(-axis.y);
                }
                if (node == XRNode.LeftHand && !usingHat)
                {
                    bool throttleHeld = grabs.TryGetValue(Layout.ThrottleHand, out var tg) && tg.Kind == "throttle";
                    if (!throttleHeld) s.Throttle = Mathf.Clamp01(s.Throttle + InputState.Deadzone(axis.y) * 0.55f * dt);
                    s.Yaw = InputState.Deadzone(axis.x);
                }

                // Triggers
                dev.TryGetFeatureValue(CommonUsages.trigger, out float trig);
                string trigAction = node == XRNode.LeftHand ? Layout.LeftTrigger : Layout.RightTrigger;
                if (trigAction == "boost") s.Boost = trig > 0.5f;
                else if (trigAction == "brake") s.Brake = trig > 0.5f;
                else if (trigAction == "brakeLeft") s.BrakeLeft = trig;
                else if (trigAction == "brakeRight") s.BrakeRight = trig;

                // Buttons: tap and hold actions
                Button(dev, side, "primary", CommonUsages.primaryButton, dt, s, node);
                Button(dev, side, "secondary", CommonUsages.secondaryButton, dt, s, node);
                Button(dev, side, "stick", CommonUsages.primary2DAxisClick, dt, s, node);

                // Fingertip pokes on cockpit buttons
                if (grab == null && Layout.PokeTargets.Count > 0)
                {
                    Vector3 tip = Fingertip(node, dev, handT);
                    foreach (var t in Layout.PokeTargets)
                    {
                        string key = side + "-" + t.Id;
                        if (Vector3.Distance(tip, t.Position) < t.Radius)
                        {
                            s.Touching.Add(t.Id);
                            if (!touchTimes.ContainsKey(key))
                            {
                                touchTimes[key] = 0;
                                if (t.Hold <= 0) { s.Fire(t.Action); Pulse(node, 0.4f, 0.04f); }
                            }
                            else
                            {
                                float before = touchTimes[key];
                                touchTimes[key] = before + dt;
                                if (t.Hold > 0 && before < t.Hold && touchTimes[key] >= t.Hold) { s.Fire(t.Action); Pulse(node, 0.9f, 0.15f); }
                            }
                        }
                        else touchTimes.Remove(key);
                    }
                }

                var fingers = node == XRNode.LeftHand ? leftFingers : rightFingers;
                fingers.localRotation = Quaternion.Euler(squeeze * 70f, 0, 0);
            }
            return any;
        }

        void Button(InputDevice dev, string side, string name, InputFeatureUsage<bool> usage, float dt, InputState s, XRNode node)
        {
            string key = side + "." + name;
            bool tapped = Pressed(dev, usage, key, out bool down);
            if (tapped && Layout.Buttons.TryGetValue(key, out var action)) s.Fire(action);
            if (Layout.HoldButtons.TryGetValue(key, out var hold))
            {
                if (down)
                {
                    holdTimes.TryGetValue(key, out float before);
                    holdTimes[key] = before + dt;
                    if (before < hold.seconds && holdTimes[key] >= hold.seconds) { s.Fire(hold.action); Pulse(node, 0.8f, 0.12f); }
                }
                else holdTimes[key] = 0;
            }
        }

        public void Pulse(XRNode node, float amplitude, float seconds)
        {
            var dev = InputDevices.GetDeviceAtXRNode(node);
            if (dev.isValid && dev.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                dev.SendHapticImpulse(0, amplitude, seconds);
        }
    }
}
