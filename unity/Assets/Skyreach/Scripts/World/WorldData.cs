using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skyreach
{
    public enum SurfaceType { Water, Land, Runway }

    public struct GroundSample
    {
        public float Height;
        public SurfaceType Type;
    }

    public sealed class Island
    {
        public double X, Z, Radius, Peak, Stretch, Ox, Oz;
        public bool Home;
    }

    public struct RingSpec
    {
        public Vector3 Position;
        public Vector3 Normal;
    }

    /// <summary>
    /// Pure world description: island layout, terrain height, runway, ring course.
    /// The maths is a straight port of webxr/src/world/worldData.js, which uses a right-handed
    /// frame with -Z forward. Unity is left-handed with +Z forward, so Z is mirrored at the
    /// boundary (Unity z = -web z). Both versions therefore build exactly the same islands.
    /// </summary>
    public sealed class WorldData
    {
        public readonly Perlin2D Noise;
        readonly Perlin2D ridgeNoise;
        public readonly List<Island> Islands;
        public readonly List<RingSpec> Rings;
        public const float RunwayHalfWidth = RunwayConfig.Width / 2f;
        public const float RunwayHalfLength = RunwayConfig.Length / 2f;

        public WorldData(int seed = WorldConfig.Seed)
        {
            Noise = new Perlin2D(seed);
            ridgeNoise = new Perlin2D(seed + 99);
            Islands = GenerateIslands(seed);
            Rings = GenerateCourse(seed);
        }

        // ------------------------------------------------------------ public (Unity coords)
        public float HeightAt(float x, float z) => (float)HeightWeb(x, -z);

        public bool IsRunway(float x, float z) =>
            Math.Abs(x) <= RunwayHalfWidth && Math.Abs(z) <= RunwayHalfLength;

        public float RunwayDistance(float x, float z) => (float)RunwayDistanceWeb(x, -z);

        public GroundSample Sample(float x, float z)
        {
            if (IsRunway(x, z)) return new GroundSample { Height = RunwayConfig.Height, Type = SurfaceType.Runway };
            float h = HeightAt(x, z);
            return new GroundSample { Height = h, Type = h > 0.3f ? SurfaceType.Land : SurfaceType.Water };
        }

        /// <summary>Island centre in Unity coordinates.</summary>
        public static Vector3 IslandCentre(Island i) => new Vector3((float)i.X, 0, (float)-i.Z);

        // ------------------------------------------------------------ generation (web coords)
        List<Island> GenerateIslands(int seed)
        {
            var rand = new Mulberry32(seed);
            var list = new List<Island>
            {
                new Island { X = 0, Z = 0, Radius = 900, Peak = 190, Stretch = 0.68, Ox = 11.3, Oz = 7.1, Home = true },
            };
            int attempts = 0;
            while (list.Count < WorldConfig.IslandCount && attempts < 500)
            {
                attempts++;
                double angle = rand.Next() * Math.PI * 2;
                double dist = 1300 + rand.Next() * 1500;
                double radius = 230 + rand.Next() * 420;
                var c = new Island
                {
                    X = Math.Cos(angle) * dist,
                    Z = Math.Sin(angle) * dist,
                    Radius = radius,
                    Peak = 70 + rand.Next() * 380,
                    Stretch = 1,
                    Ox = rand.Next() * 100,
                    Oz = rand.Next() * 100,
                };
                bool clear = true;
                foreach (var o in list)
                {
                    if (Hypot(o.X - c.X, o.Z - c.Z) <= (o.Radius + c.Radius) * 1.15 + 120) { clear = false; break; }
                }
                if (clear) list.Add(c);
            }
            return list;
        }

        double RunwayDistanceWeb(double x, double z)
        {
            double dx = Math.Max(Math.Abs(x) - RunwayHalfWidth, 0);
            double dz = Math.Max(Math.Abs(z) - RunwayHalfLength, 0);
            return Hypot(dx, dz);
        }

        double HeightWeb(double x, double z)
        {
            double h = -45;
            foreach (var isl in Islands)
            {
                double dx = x - isl.X;
                double dz = (z - isl.Z) * isl.Stretch;
                double d = Math.Sqrt(dx * dx + dz * dz) / isl.Radius;
                if (d > 1.25) continue;
                double shape = Perlin2D.SmoothStep(1.2, 0.12, d);
                double n = Noise.Fbm((x + isl.Ox) * 0.0032, (z + isl.Oz) * 0.0032, 5);
                double r = 1 - Math.Abs(ridgeNoise.Fbm(x * 0.0055, z * 0.0055, 4));
                double mountain = isl.Peak * Math.Pow(shape, 1.7) * (0.5 + 0.5 * n) * (0.55 + 0.45 * r * r);
                if (isl.Home)
                {
                    double mask = Perlin2D.SmoothStep(140, 480, RunwayDistanceWeb(x, z)) * Perlin2D.SmoothStep(-200, 400, x);
                    mountain *= mask;
                }
                double hi = -14 + shape * 20 + mountain + n * 3;
                if (hi > h) h = hi;
            }
            double rd = RunwayDistanceWeb(x, z);
            if (rd < 120) h = Perlin2D.Lerp(RunwayConfig.Height, h, Perlin2D.SmoothStep(0, 120, rd));
            return h;
        }

        double ClearanceHeightWeb(double x, double z, double radius)
        {
            double max = HeightWeb(x, z);
            for (int i = 0; i < 8; i++)
            {
                double a = i / 8.0 * Math.PI * 2;
                max = Math.Max(max, HeightWeb(x + Math.Cos(a) * radius * 2, z + Math.Sin(a) * radius * 2));
            }
            return Math.Max(max, 0);
        }

        List<RingSpec> GenerateCourse(int seed)
        {
            var rand = new Mulberry32(seed + 7);
            var pts = new List<double[]> { new double[] { 0, 110, -RunwayHalfLength - 650 } };
            int loopCount = CourseConfig.RingCount - 3;
            for (int i = 0; i < loopCount; i++)
            {
                double t = (i + 1.0) / (loopCount + 1);
                double theta = 0.12 * Math.PI + t * 1.25 * Math.PI;
                double radius = 1700 + (rand.Next() - 0.5) * 900;
                double x = Math.Sin(theta) * radius;
                double z = -Math.Cos(theta) * radius;
                double y = 90 + rand.Next() * 360;
                pts.Add(new[] { x, y, z });
            }
            pts.Add(new double[] { -500, 170, RunwayHalfLength + 1500 });
            pts.Add(new double[] { 0, 75, RunwayHalfLength + 750 });

            foreach (var p in pts)
            {
                double floor = ClearanceHeightWeb(p[0], p[2], CourseConfig.RingRadius) + CourseConfig.RingRadius + 35;
                if (p[1] < floor) p[1] = floor;
            }

            var rings = new List<RingSpec>();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                // Each ring faces the way you arrive at it, so you never meet one edge-on.
                // Ring 1 points straight off the runway; the last ring is lined up for landing.
                double dx, dy, dz;
                if (i == 0) { dx = 0; dy = 0; dz = -1; }
                else if (i == pts.Count - 1) { dx = 0; dy = -0.1; dz = -1; }
                else
                {
                    var prev = pts[i - 1];
                    dx = p[0] - prev[0]; dy = p[1] - prev[1]; dz = p[2] - prev[2];
                }
                dy *= 0.3;
                double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (len == 0) len = 1;
                rings.Add(new RingSpec
                {
                    // Mirror Z into Unity's left-handed frame.
                    Position = new Vector3((float)p[0], (float)p[1], (float)-p[2]),
                    Normal = new Vector3((float)(dx / len), (float)(dy / len), (float)(-dz / len)),
                });
            }
            return rings;
        }

        static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);
    }
}
