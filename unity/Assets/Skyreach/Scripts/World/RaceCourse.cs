using System.Collections.Generic;
using UnityEngine;

namespace Skyreach
{
    public enum RingEventType { None, Ring, Finish }

    public struct RingEvent
    {
        public RingEventType Type;
        public int Index;
        public float Time;
    }

    /// <summary>Ring-crossing detection and race timer (port of RaceCourse in rings.js).</summary>
    public sealed class RaceCourse
    {
        public readonly List<RingSpec> Rings;
        public int Index;
        public float Elapsed;
        public bool Running;
        public bool Finished;

        public RaceCourse(List<RingSpec> rings)
        {
            Rings = rings;
            Reset();
        }

        public RingSpec Current => Rings[Mathf.Min(Index, Rings.Count - 1)];

        public void Reset()
        {
            Index = 0;
            Elapsed = 0;
            Running = false;
            Finished = false;
        }

        public void Start()
        {
            if (!Running && !Finished) Running = true;
        }

        public void Tick(float dt)
        {
            if (Running) Elapsed += dt;
        }

        /// <summary>Did the segment prev→cur pass through the active ring (in the right direction)?</summary>
        public RingEvent Check(Vector3 prev, Vector3 cur)
        {
            if (Finished) return default;
            var ring = Current;
            float d0 = Vector3.Dot(prev - ring.Position, ring.Normal);
            float d1 = Vector3.Dot(cur - ring.Position, ring.Normal);
            if (!(d0 < 0 && d1 >= 0)) return default;
            float t = d0 / (d0 - d1);
            Vector3 hit = Vector3.Lerp(prev, cur, t);
            if (Vector3.Distance(hit, ring.Position) > CourseConfig.RingRadius) return default;
            int passed = Index;
            Index++;
            if (Index >= Rings.Count)
            {
                Finished = true;
                Running = false;
                return new RingEvent { Type = RingEventType.Finish, Index = passed, Time = Elapsed };
            }
            return new RingEvent { Type = RingEventType.Ring, Index = passed };
        }
    }
}
