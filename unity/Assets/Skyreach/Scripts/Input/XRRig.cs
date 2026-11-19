using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Skyreach
{
    /// <summary>
    /// The pilot's seat: head camera and two hands, tracked through Unity's XR input API
    /// (works with OpenXR on Quest, no extra toolkits needed).
    ///
    ///  Right hand: grip = grab the flight stick (or thumbstick), trigger = afterburner,
    ///              A = restart, B = comfort vignette.
    ///  Left hand:  grip = grab the throttle (or thumbstick up/down), stick left/right = rudder,
    ///              trigger = brakes, X = comfort vignette, Y = mute, stick click = re-centre seat.
    /// </summary>
    public sealed class XRRig : MonoBehaviour
    {
        const float GrabThreshold = 0.6f;
        const float StickRange = 0.09f;    // metres of hand travel for full stick deflection
        const float ThrottleRange = 0.2f;  // metres of hand travel from idle to full throttle
        const float NearDistance = 0.16f;

        public Camera Head;
        public Transform LeftHand, RightHand;
        public Transform CockpitSpace;

        Transform leftFingers, rightFingers;
        bool stickGrab, throttleGrab;
        Vector3 stickOrigin, throttleOrigin;
        float throttleStart;
        readonly Dictionary<string, bool> prevButtons = new Dictionary<string, bool>();

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

        /// <summary>Head pose relative to the rig, for seat calibration.</summary>
        public void GetHeadLocal(out Vector3 pos, out Quaternion rot)
        {
            UpdatePoses();
            pos = Head.transform.localPosition;
            rot = Head.transform.localRotation;
        }

        bool Pressed(InputDevice dev, InputFeatureUsage<bool> usage, string key)
        {
            dev.TryGetFeatureValue(usage, out bool now);
            prevButtons.TryGetValue(key, out bool was);
            prevButtons[key] = now;
            return now && !was;
        }

        public bool ReadInput(float dt, InputState s)
        {
            if (!Active) return false;
            UpdatePoses();
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            s.StickGrabbed = s.ThrottleGrabbed = s.StickNear = s.ThrottleNear = false;

            if (right.isValid)
            {
                right.TryGetFeatureValue(CommonUsages.grip, out float squeeze);
                Vector3 pos = CockpitSpace.InverseTransformPoint(RightHand.position);
                s.StickNear = Vector3.Distance(pos, CockpitLayout.StickGrip) < NearDistance;
                if (squeeze > GrabThreshold)
                {
                    if (!stickGrab) { stickGrab = true; stickOrigin = pos; Pulse(XRNode.RightHand, 0.25f, 0.03f); }
                    Vector3 d = pos - stickOrigin;
                    s.Roll = Mathf.Clamp(d.x / StickRange, -1, 1);
                    s.Pitch = Mathf.Clamp(-d.z / StickRange, -1, 1); // pulling toward you (-Z) = nose up
                    s.StickGrabbed = true;
                }
                else
                {
                    stickGrab = false;
                    right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis);
                    s.Roll = InputState.Deadzone(axis.x);
                    s.Pitch = InputState.Deadzone(-axis.y); // pull back = nose up
                }
                right.TryGetFeatureValue(CommonUsages.trigger, out float trig);
                s.Boost = trig > 0.5f;
                if (Pressed(right, CommonUsages.primaryButton, "A")) s.Restart = true;
                if (Pressed(right, CommonUsages.secondaryButton, "B")) s.CycleComfort = true;
                rightFingers.localRotation = Quaternion.Euler(squeeze * 70f, 0, 0);
            }

            if (left.isValid)
            {
                left.TryGetFeatureValue(CommonUsages.grip, out float squeeze);
                Vector3 pos = CockpitSpace.InverseTransformPoint(LeftHand.position);
                s.ThrottleNear = Vector3.Distance(pos, CockpitLayout.ThrottleGrip) < NearDistance;
                if (squeeze > GrabThreshold)
                {
                    if (!throttleGrab) { throttleGrab = true; throttleOrigin = pos; throttleStart = s.Throttle; Pulse(XRNode.LeftHand, 0.25f, 0.03f); }
                    float push = pos.z - throttleOrigin.z; // forward = more power
                    s.Throttle = Mathf.Clamp01(throttleStart + push / ThrottleRange);
                    s.ThrottleGrabbed = true;
                }
                else
                {
                    throttleGrab = false;
                    left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis);
                    s.Throttle = Mathf.Clamp01(s.Throttle + InputState.Deadzone(axis.y) * 0.55f * dt);
                }
                left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
                s.Yaw = InputState.Deadzone(stick.x);
                left.TryGetFeatureValue(CommonUsages.trigger, out float trig);
                s.Brake = trig > 0.5f;
                if (Pressed(left, CommonUsages.secondaryButton, "Y")) s.ToggleMute = true;
                if (Pressed(left, CommonUsages.primaryButton, "X")) s.CycleComfort = true;
                if (Pressed(left, CommonUsages.primary2DAxisClick, "LStick")) s.Recenter = true;
                leftFingers.localRotation = Quaternion.Euler(squeeze * 70f, 0, 0);
            }
            return right.isValid || left.isValid;
        }

        public void Pulse(XRNode node, float amplitude, float seconds)
        {
            var dev = InputDevices.GetDeviceAtXRNode(node);
            if (dev.isValid && dev.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse)
                dev.SendHapticImpulse(0, amplitude, seconds);
        }
    }
}
