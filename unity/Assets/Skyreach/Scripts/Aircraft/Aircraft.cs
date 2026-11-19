using System;
using UnityEngine;

namespace Skyreach
{
    /// <summary>What an aircraft can see of the game each frame.</summary>
    public sealed class GameContext
    {
        public GameManager Game;
        public InputState Input;
        public RaceCourse Course;
        public WorldData World;
        public MessagePanel Message;
        public GameAudio Audio;
        public SettingsStore Settings;
        public float Time;
    }

    /// <summary>
    /// Common interface for the aircraft you can pick (the arcade jet and the SR22-class).
    /// Mirrors the aircraft modules in webxr/src/aircraft/*.
    /// </summary>
    public abstract class Aircraft
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public Transform Root { get; protected set; }
        public ControlLayout Layout { get; protected set; } = new ControlLayout();
        public KeyMap Keys { get; protected set; } = new KeyMap();
        public abstract float GearHeight { get; }
        public abstract string AudioProfile { get; }
        public abstract string[] StartMessage { get; }
        public abstract string[] ReadyMessage { get; }

        // State the game reads
        public abstract Vector3 Position { get; }
        public abstract Quaternion Rotation { get; }
        public abstract bool OnGround { get; }
        public abstract bool Crashed { get; }
        public abstract float Speed { get; }
        public abstract float Throttle { get; }
        public virtual float Rpm => 0;
        public virtual bool Boosting => false;

        // Events the game listens to
        public event Action Takeoff;
        public event Action<string, string, float, Color> Landed; // title, subtitle, seconds, colour
        public event Action<string> Crash;
        protected void RaiseTakeoff() => Takeoff?.Invoke();
        protected void RaiseLanded(string t, string s, float secs, Color c) => Landed?.Invoke(t, s, secs, c);
        protected void RaiseCrash(string reason) => Crash?.Invoke(reason);

        public GameContext Ctx { get; set; }
        protected bool CourseFinished => Ctx?.Game != null && Ctx.Game.State == GameState.Finished;

        public abstract void Reset(Vector3 spawn);
        public abstract void Step(float dt, InputState input, Func<float, float, GroundSample> ground);
        public virtual bool HandleAction(string action) => false;
        public abstract void UpdateVisuals(float dt, InputState input);
        public virtual void SetCockpitView(bool inCockpit) { }
        public virtual void Dispose() { }
    }
}
