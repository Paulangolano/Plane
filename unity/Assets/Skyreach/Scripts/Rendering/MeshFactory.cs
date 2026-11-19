using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Skyreach
{
    /// <summary>
    /// Procedural low-poly meshes (box, cylinder, cone, torus, sphere...) with baked vertex colours,
    /// so the whole game needs no imported models.
    /// </summary>
    public static class MeshFactory
    {
        /// <summary>Accumulates flat-shaded triangles with per-vertex colours.</summary>
        public sealed class Builder
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
            {
                int i = Vertices.Count;
                Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
                Colors.Add(col); Colors.Add(col); Colors.Add(col);
                Uvs.Add(Vector2.zero); Uvs.Add(Vector2.zero); Uvs.Add(Vector2.zero);
                Triangles.Add(i); Triangles.Add(i + 1); Triangles.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
            {
                Tri(a, b, c, col);
                Tri(a, c, d, col);
            }

            /// <summary>Append another mesh transformed by a matrix, tinted with a colour.</summary>
            public void Add(Mesh mesh, Matrix4x4 m, Color col)
            {
                var v = mesh.vertices;
                var t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                    Tri(m.MultiplyPoint3x4(v[t[i]]), m.MultiplyPoint3x4(v[t[i + 1]]), m.MultiplyPoint3x4(v[t[i + 2]]), col);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                if (Vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(Vertices);
                mesh.SetColors(Colors);
                mesh.SetUVs(0, Uvs);
                mesh.SetTriangles(Triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        public static Mesh Box(Vector3 size)
        {
            var b = new Builder();
            Vector3 h = size * 0.5f;
            Vector3[] c =
            {
                new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z),
                new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
            };
            // Front faces: Vector3.Cross(b - a, c - a) points out of the mesh.
            b.Quad(c[0], c[3], c[2], c[1], Color.white); // -Z
            b.Quad(c[5], c[6], c[7], c[4], Color.white); // +Z
            b.Quad(c[4], c[7], c[3], c[0], Color.white); // -X
            b.Quad(c[1], c[2], c[6], c[5], Color.white); // +X
            b.Quad(c[3], c[7], c[6], c[2], Color.white); // +Y
            b.Quad(c[4], c[0], c[1], c[5], Color.white); // -Y
            return b.Build("Box");
        }

        /// <summary>Cylinder along +Y, centred on the origin. radiusTop = 0 gives a cone.</summary>
        public static Mesh Cylinder(float radiusTop, float radiusBottom, float height, int segments, bool caps = true)
        {
            var b = new Builder();
            float hy = height * 0.5f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2;
                float a1 = (i + 1) / (float)segments * Mathf.PI * 2;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                Vector3 d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 b0 = d0 * radiusBottom + Vector3.down * hy;
                Vector3 b1 = d1 * radiusBottom + Vector3.down * hy;
                Vector3 t0 = d0 * radiusTop + Vector3.up * hy;
                Vector3 t1 = d1 * radiusTop + Vector3.up * hy;
                if (radiusTop > 0) b.Quad(b0, t0, t1, b1, Color.white);
                else b.Tri(b0, t0, b1, Color.white);
                if (caps)
                {
                    if (radiusTop > 0) b.Tri(Vector3.up * hy, t1, t0, Color.white);
                    b.Tri(Vector3.down * hy, b0, b1, Color.white);
                }
            }
            return b.Build("Cylinder");
        }

        public static Mesh Cone(float radius, float height, int segments) => Cylinder(0, radius, height, segments);

        /// <summary>Torus in the XY plane, facing +Z (the ring's normal).</summary>
        public static Mesh Torus(float radius, float tube, int radial, int tubular, float arc = Mathf.PI * 2)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int j = 0; j <= radial; j++)
            {
                for (int i = 0; i <= tubular; i++)
                {
                    float u = i / (float)tubular * arc;
                    float v = j / (float)radial * Mathf.PI * 2;
                    verts.Add(new Vector3(
                        (radius + tube * Mathf.Cos(v)) * Mathf.Cos(u),
                        (radius + tube * Mathf.Cos(v)) * Mathf.Sin(u),
                        tube * Mathf.Sin(v)));
                }
            }
            for (int j = 1; j <= radial; j++)
            {
                for (int i = 1; i <= tubular; i++)
                {
                    int a = (tubular + 1) * j + i - 1;
                    int b = (tubular + 1) * (j - 1) + i - 1;
                    int c = (tubular + 1) * (j - 1) + i;
                    int d = (tubular + 1) * j + i;
                    tris.Add(a); tris.Add(b); tris.Add(d);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }
            var mesh = new Mesh { name = "Torus" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh Icosphere(int subdivisions = 1)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            var f = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            for (int s = 0; s < subdivisions; s++)
            {
                var next = new List<int>();
                var cache = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int idx)) return idx;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized);
                    cache[key] = v.Count - 1;
                    return v.Count - 1;
                }
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = Mid(f[i], f[i + 1]), b = Mid(f[i + 1], f[i + 2]), c = Mid(f[i + 2], f[i]);
                    next.AddRange(new[] { f[i], a, c, f[i + 1], b, a, f[i + 2], c, b, a, b, c });
                }
                f = next;
            }
            // Flat shading: unweld.
            var bld = new Builder();
            // Winding is chosen so Vector3.Cross(b - a, c - a) points outward, which Unity treats as the front face.
            for (int i = 0; i < f.Count; i += 3) bld.Tri(v[f[i]], v[f[i + 1]], v[f[i + 2]], Color.white);
            return bld.Build("Icosphere");
        }

        public static Mesh Quad(float width, float height)
        {
            var mesh = new Mesh { name = "Quad" };
            float w = width / 2, h = height / 2;
            mesh.vertices = new[] { new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(w, h, 0), new Vector3(-w, h, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            // Facing -Z, i.e. towards a camera looking down +Z.
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static GameObject Spawn(string name, Mesh mesh, Material mat, Transform parent, Vector3 localPos, Quaternion? localRot = null, Vector3? scale = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.transform.localScale = scale ?? Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }
    }
}
