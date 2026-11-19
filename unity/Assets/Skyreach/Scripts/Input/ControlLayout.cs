using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Skyreach
{
    /// <summary>A cockpit button pressed by touching it with a fingertip (optionally press-and-hold).</summary>
    public sealed class PokeTarget
    {
        public string Id, Action;
        public Vector3 Position; // aircraft-local
        public float Radius = 0.028f;
        public float Hold;       // seconds; 0 = fires on touch
    }

    /// <summary>A handle you grab with the grip button and pull (e.g. the parachute handle).</summary>
    public sealed class GrabTarget
    {
        public string Id, Action;
        public Vector3 Position;
        public float Radius = 0.12f;
        public float PullDistance = 0.1f;
    }

    /// <summary>How the Quest controllers map onto one aircraft's cockpit.</summary>
    public sealed class ControlLayout
    {
        public XRNode StickHand = XRNode.RightHand, ThrottleHand = XRNode.LeftHand;
        public Vector3 StickGrip, ThrottleGrip;
        public float StickRange = 0.09f, ThrottleRange = 0.2f;
        public bool TrimHat;
        /// <summary>Trigger actions per hand: "boost", "brake", "brakeLeft", "brakeRight".</summary>
        public string LeftTrigger = "brake", RightTrigger = "boost";
        /// <summary>Tap actions keyed "right.primary", "right.secondary", "left.primary", "left.secondary", "left.stick".</summary>
        public Dictionary<string, string> Buttons = new Dictionary<string, string>();
        /// <summary>Press-and-hold actions, same keys.</summary>
        public Dictionary<string, (string action, float seconds)> HoldButtons = new Dictionary<string, (string, float)>();
        public List<PokeTarget> PokeTargets = new List<PokeTarget>();
        public List<GrabTarget> GrabTargets = new List<GrabTarget>();
    }

    /// <summary>Keyboard actions for one aircraft (Input System key names, e.g. "leftBracket").</summary>
    public sealed class KeyMap
    {
        public Dictionary<string, string> Keys = new Dictionary<string, string>();
        public Dictionary<string, (string action, float seconds)> HoldKeys = new Dictionary<string, (string, float)>();
    }
}
