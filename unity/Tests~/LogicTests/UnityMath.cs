// Real implementations of the few UnityEngine maths types the core logic uses,
// so FlightModel / WorldData / RaceCourse can run outside the Unity editor.
using System;
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => a * d;
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public Vector3 normalized => magnitude > 1e-6f ? this / magnitude : zero;
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * Math.Max(0, Math.Min(1, t));
        public override string ToString() => $"({x:0.00}, {y:0.00}, {z:0.00})";
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public static Quaternion AngleAxis(float deg, Vector3 axis)
        {
            axis = axis.normalized;
            double h = deg * Math.PI / 360.0;
            float s = (float)Math.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(h));
        }
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
            a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        public static Vector3 operator *(Quaternion q, Vector3 v)
        {
            var u = new Vector3(q.x, q.y, q.z);
            Vector3 t = 2f * Vector3.Cross(u, v);
            return v + q.w * t + Vector3.Cross(u, t);
        }
        public static Quaternion Inverse(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);
        public static float Angle(Quaternion a, Quaternion b)
        {
            float d = Math.Min(Math.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w), 1f);
            return (float)(2 * Math.Acos(d) * 180 / Math.PI);
        }
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Rad2Deg = 57.29578f;
        public const float Deg2Rad = PI / 180f;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0, 1);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Abs(float a) => Math.Abs(a);
        public static float Exp(float a) => (float)Math.Exp(a);
        public static float Sqrt(float a) => (float)Math.Sqrt(a);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Asin(float a) => (float)Math.Asin(a);
        public static int Min(int a, int b) => Math.Min(a, b);
    }
}
