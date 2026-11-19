using System;

namespace Skyreach
{
    /// <summary>
    /// Deterministic RNG, bit-for-bit identical to mulberry32 in webxr/src/core/noise.js,
    /// so the Unity and WebXR versions generate the same archipelago.
    /// </summary>
    public sealed class Mulberry32
    {
        uint state;

        public Mulberry32(int seed) { state = unchecked((uint)seed); }

        public double Next()
        {
            unchecked
            {
                state += 0x6D2B79F5u;
                uint t = state;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    /// <summary>Classic 2D Perlin noise with fractal Brownian motion.</summary>
    public sealed class Perlin2D
    {
        readonly byte[] perm = new byte[512];

        public Perlin2D(int seed)
        {
            var rand = new Mulberry32(seed);
            var p = new byte[256];
            for (int i = 0; i < 256; i++) p[i] = (byte)i;
            for (int i = 255; i > 0; i--)
            {
                int j = (int)Math.Floor(rand.Next() * (i + 1));
                byte t = p[i];
                p[i] = p[j];
                p[j] = t;
            }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }

        static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

        static double Grad(int hash, double x, double y)
        {
            switch (hash & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }

        public double Noise(double x, double y)
        {
            double xf0 = Math.Floor(x);
            double yf0 = Math.Floor(y);
            int X = (int)((long)xf0 & 255);
            int Y = (int)((long)yf0 & 255);
            double xf = x - xf0;
            double yf = y - yf0;
            double u = Fade(xf);
            double v = Fade(yf);
            int aa = perm[perm[X] + Y];
            int ab = perm[perm[X] + Y + 1];
            int ba = perm[perm[X + 1] + Y];
            int bb = perm[perm[X + 1] + Y + 1];
            double x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
            double x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
            return Lerp(x1, x2, v);
        }

        public double Fbm(double x, double y, int octaves = 5)
        {
            double amp = 1, freq = 1, sum = 0, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += Noise(x * freq, y * freq) * amp;
                norm += amp;
                amp *= 0.5;
                freq *= 2.03;
            }
            return sum / norm;
        }

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Math.Min(1, Math.Max(0, (x - edge0) / (edge1 - edge0)));
            return t * t * (3 - 2 * t);
        }
    }
}
