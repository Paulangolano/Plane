using UnityEngine;
using UnityEngine.Rendering;

namespace Skyreach
{
    /// <summary>
    /// Builds the visible world: island terrain, runway, tower, trees, ocean, sky and clouds.
    /// Everything is generated at startup from WorldData, so the scene file stays tiny.
    /// </summary>
    public static class WorldBuilder
    {
        static readonly Color Sand = SkyreachMaterials.Hex(0xe8d49a);
        static readonly Color WetSand = SkyreachMaterials.Hex(0xc7b07a);
        static readonly Color Grass = SkyreachMaterials.Hex(0x5f9e3a);
        static readonly Color Forest = SkyreachMaterials.Hex(0x3c7a2c);
        static readonly Color Rock = SkyreachMaterials.Hex(0x8a8378);
        static readonly Color Snow = SkyreachMaterials.Hex(0xf4f6f8);
        static readonly Color Seabed = SkyreachMaterials.Hex(0xd9c690);

        public static readonly Color SkyZenith = SkyreachMaterials.Hex(0x2f7fd8);
        public static readonly Color SkyHorizon = SkyreachMaterials.Hex(0xbfe3f7);
        public static readonly Vector3 SunDirection = new Vector3(0.45f, 0.55f, 0.7f).normalized; // z mirrored from WebXR

        // ------------------------------------------------------------------ sky & lighting
        public static void SetupSky(SkyreachMaterials mats)
        {
            var sky = new Material(mats.Sky) { name = "Sky" };
            sky.SetColor("_Zenith", SkyZenith);
            sky.SetColor("_Horizon", SkyHorizon);
            sky.SetColor("_SunColor", SkyreachMaterials.Hex(0xfff2d0));
            RenderSettings.skybox = sky;
            RenderSettings.fog = false; // the Skyreach shaders do their own (cheaper) fog

            Shader.SetGlobalVector("_SkyreachSunDir", SunDirection);
            Shader.SetGlobalColor("_SkyreachSunColor", SkyreachMaterials.Hex(0xfff1dc) * 1.0f);
            Shader.SetGlobalColor("_SkyreachSkyAmbient", SkyreachMaterials.Hex(0xcfe8ff) * 0.5f);
            Shader.SetGlobalColor("_SkyreachGroundAmbient", SkyreachMaterials.Hex(0x5a6b4a) * 0.5f);
            Shader.SetGlobalColor("_SkyreachFogColor", SkyHorizon);
            Shader.SetGlobalVector("_SkyreachFog", new Vector4(WorldConfig.FogNear, WorldConfig.FogFar, 0, 0));
        }

        // ------------------------------------------------------------------ terrain
        static Color TerrainColour(float h, float slope, float n)
        {
            Color c;
            if (h < -1) c = Seabed;
            else if (h < 2.5f) c = Color.Lerp(WetSand, Sand, Mathf.Max(0, (h + 1) / 3.5f));
            else if (h < 7) c = Color.Lerp(Sand, Grass, (h - 2.5f) / 4.5f);
            else if (h < 160) c = Color.Lerp(Grass, Forest, Mathf.Min(1, 0.5f + n * 0.8f));
            else if (h < 260) c = Color.Lerp(Forest, Rock, (h - 160) / 100f);
            else c = Color.Lerp(Rock, Snow, Mathf.Min(1, (h - 260) / 60f));
            if (slope > 0.55f && h > 8) c = Color.Lerp(c, Rock, Mathf.Min(1, (slope - 0.55f) * 2.5f));
            return c;
        }

        public static Transform BuildTerrain(WorldData world, SkyreachMaterials mats, Transform parent)
        {
            var root = new GameObject("Terrain").transform;
            root.SetParent(parent, false);

            foreach (var isl in world.Islands)
            {
                var b = new MeshFactory.Builder();
                Vector3 c = WorldData.IslandCentre(isl);
                float extentX = (float)isl.Radius * 1.3f;
                float extentZ = (float)(isl.Radius * 1.3 / isl.Stretch);
                float spacing = (isl.Home ? 18f : 22f) * WorldConfig.TerrainResolution * 0.5f;
                int nx = Mathf.CeilToInt(extentX * 2 / spacing);
                int nz = Mathf.CeilToInt(extentZ * 2 / spacing);
                float dx = extentX * 2 / nx, dz = extentZ * 2 / nz;
                var heights = new float[nx + 1, nz + 1];
                for (int i = 0; i <= nx; i++)
                    for (int j = 0; j <= nz; j++)
                        heights[i, j] = world.HeightAt(c.x - extentX + i * dx, c.z - extentZ + j * dz);

                for (int i = 0; i < nx; i++)
                {
                    for (int j = 0; j < nz; j++)
                    {
                        float x0 = c.x - extentX + i * dx, x1 = x0 + dx;
                        float z0 = c.z - extentZ + j * dz, z1 = z0 + dz;
                        var p00 = new Vector3(x0, heights[i, j], z0);
                        var p10 = new Vector3(x1, heights[i + 1, j], z0);
                        var p01 = new Vector3(x0, heights[i, j + 1], z1);
                        var p11 = new Vector3(x1, heights[i + 1, j + 1], z1);
                        // Skip cells that are entirely deep underwater (never visible through the opaque sea).
                        if (p00.y < -20 && p10.y < -20 && p01.y < -20 && p11.y < -20) continue;
                        AddTerrainTri(b, world, p00, p01, p11);
                        AddTerrainTri(b, world, p00, p11, p10);
                    }
                }
                var go = MeshFactory.Spawn(isl.Home ? "HomeIsland" : "Island", b.Build("Island"), mats.Terrain, root, Vector3.zero);
                go.isStatic = true;
            }

            BuildRunway(world, mats, root);
            BuildTower(world, mats, root);
            BuildTrees(world, mats, root);
            return root;
        }

        static void AddTerrainTri(MeshFactory.Builder b, WorldData world, Vector3 a, Vector3 bb, Vector3 c)
        {
            Vector3 n = Vector3.Cross(bb - a, c - a).normalized;
            Vector3 centre = (a + bb + c) / 3f;
            float noise = (float)world.Noise.Noise(centre.x * 0.02, -centre.z * 0.02) * 0.5f + 0.5f;
            Color col = TerrainColour(centre.y, 1 - n.y, noise);
            float jitter = 1 + (float)world.Noise.Noise(centre.x * 0.37, -centre.z * 0.37) * 0.06f;
            col *= jitter;
            col.a = 1;
            b.Tri(a, bb, c, col);
        }

        static void BuildRunway(WorldData world, SkyreachMaterials mats, Transform root)
        {
            float hw = WorldData.RunwayHalfWidth, hl = WorldData.RunwayHalfLength, h = RunwayConfig.Height;
            var tex = new Texture2D(128, 1024, TextureFormat.RGBA32, true) { name = "RunwayMarkings", anisoLevel = 4, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[128 * 1024];
            var asphalt = new Color32(0x3b, 0x3d, 0x40, 255);
            var paint = new Color32(0xf2, 0xf2, 0xf2, 255);
            for (int y = 0; y < 1024; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    bool mark = (x >= 4 && x < 7) || (x >= 121 && x < 124); // edge lines
                    if (x >= 62 && x < 66 && y >= 40 && y < 984 && (y - 40) % 40 < 22) mark = true; // centre dashes
                    if ((y >= 6 && y < 32) || (y >= 992 && y < 1018)) // threshold piano keys
                        for (int k = 0; k < 8; k++) if (x >= 12 + k * 13 && x < 19 + k * 13) mark = true;
                    px[y * 128 + x] = mark ? paint : asphalt;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);

            var mesh = new Mesh { name = "Runway" };
            mesh.vertices = new[] { new Vector3(-hw, 0, -hl), new Vector3(-hw, 0, hl), new Vector3(hw, 0, hl), new Vector3(hw, 0, -hl) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            var mat = mats.MakeLit("Runway", Color.white, tex: tex);
            MeshFactory.Spawn("Runway", mesh, mat, root, new Vector3(0, h + 0.08f, 0)).isStatic = true;

            // Edge and threshold lights, merged into three meshes.
            var cube = MeshFactory.Box(new Vector3(1.2f, 0.6f, 1.2f));
            var white = new MeshFactory.Builder();
            var green = new MeshFactory.Builder();
            var red = new MeshFactory.Builder();
            for (float z = -hl; z <= hl; z += 60)
            {
                white.Add(cube, Matrix4x4.Translate(new Vector3(-hw - 2, h + 0.3f, z)), Color.white);
                white.Add(cube, Matrix4x4.Translate(new Vector3(hw + 2, h + 0.3f, z)), Color.white);
            }
            for (float x = -hw; x <= hw; x += 5)
            {
                // Takeoff is towards +Z: green lights at the approach end (-Z), red at the far end.
                green.Add(cube, Matrix4x4.Translate(new Vector3(x, h + 0.3f, -hl - 3)), Color.white);
                red.Add(cube, Matrix4x4.Translate(new Vector3(x, h + 0.3f, hl + 3)), Color.white);
            }
            MeshFactory.Spawn("EdgeLights", white.Build("EdgeLights"), mats.LightWhite, root, Vector3.zero);
            MeshFactory.Spawn("ApproachLights", green.Build("ApproachLights"), mats.LightGreen, root, Vector3.zero);
            MeshFactory.Spawn("EndLights", red.Build("EndLights"), mats.LightRed, root, Vector3.zero);
        }

        static void BuildTower(WorldData world, SkyreachMaterials mats, Transform root)
        {
            float x = -WorldData.RunwayHalfWidth - 70;
            var tower = new GameObject("ControlTower").transform;
            tower.SetParent(root, false);
            tower.localPosition = new Vector3(x, world.HeightAt(x, 0) - 0.5f, 0);
            var wall = mats.MakeLit("TowerWall", SkyreachMaterials.Hex(0xe9e4da));
            MeshFactory.Spawn("Base", MeshFactory.Cylinder(4, 5, 26, 8), wall, tower, new Vector3(0, 13, 0));
            MeshFactory.Spawn("Cab", MeshFactory.Cylinder(7, 5.5f, 6, 8), mats.Glass, tower, new Vector3(0, 29, 0));
            MeshFactory.Spawn("Roof", MeshFactory.Cylinder(7.6f, 7.6f, 1, 8), wall, tower, new Vector3(0, 32.5f, 0));
            MeshFactory.Spawn("Hangar", MeshFactory.Box(new Vector3(40, 14, 30)), mats.Hangar, tower, new Vector3(-30, 7, -60));
        }

        static void BuildTrees(WorldData world, SkyreachMaterials mats, Transform root)
        {
            var trunk = MeshFactory.Cylinder(0.6f, 0.8f, 4, 5);
            var crown = MeshFactory.Cone(4, 11, 6);
            Color bark = SkyreachMaterials.Hex(0x6b4a2b);
            Color leaves = SkyreachMaterials.Hex(0x2f6b2a);
            var b = new MeshFactory.Builder();
            int seed = 4242;
            float Rand()
            {
                seed = (int)((seed * 16807L) % 2147483647L);
                return seed / 2147483647f;
            }
            int count = 0;
            foreach (var isl in world.Islands)
            {
                int attempts = (int)(isl.Radius * isl.Radius * 0.0012);
                Vector3 c = WorldData.IslandCentre(isl);
                for (int i = 0; i < attempts && count < 1800; i++)
                {
                    float a = Rand() * Mathf.PI * 2;
                    float r = Mathf.Sqrt(Rand()) * (float)isl.Radius;
                    float x = c.x + Mathf.Cos(a) * r;
                    float z = c.z + Mathf.Sin(a) * r / (float)isl.Stretch;
                    float h = world.HeightAt(x, z);
                    if (h < 9 || h > 170) continue;
                    if (world.RunwayDistance(x, z) < 90) continue;
                    float slope = Mathf.Abs(world.HeightAt(x + 4, z) - h) + Mathf.Abs(world.HeightAt(x, z + 4) - h);
                    if (slope > 5) continue;
                    if (world.Noise.Noise(x * 0.006, -z * 0.006) < -0.05) continue;
                    float s = 0.7f + Rand() * 0.8f;
                    var m = Matrix4x4.TRS(new Vector3(x, h - 0.5f, z), Quaternion.Euler(0, Rand() * 180, 0), new Vector3(s, s * (0.8f + Rand() * 0.5f), s));
                    b.Add(trunk, m * Matrix4x4.Translate(new Vector3(0, 2, 0)), bark);
                    b.Add(crown, m * Matrix4x4.Translate(new Vector3(0, 9, 0)), leaves);
                    count++;
                }
            }
            MeshFactory.Spawn("Trees", b.Build("Trees"), mats.Terrain, root, Vector3.zero).isStatic = true;
        }

        // ------------------------------------------------------------------ ocean
        public static Transform BuildOcean(WorldData world, SkyreachMaterials mats, Transform parent)
        {
            float size = WorldConfig.Size * 2.2f;
            int seg = 150;
            var verts = new Vector3[(seg + 1) * (seg + 1)];
            var cols = new Color[verts.Length];
            var tris = new int[seg * seg * 6];
            for (int j = 0; j <= seg; j++)
            {
                for (int i = 0; i <= seg; i++)
                {
                    float x = -size / 2 + size * i / seg;
                    float z = -size / 2 + size * j / seg;
                    int idx = j * (seg + 1) + i;
                    verts[idx] = new Vector3(x, 0, z);
                    float shallow = Mathf.Clamp01(1 + world.HeightAt(x, z) / 30f);
                    cols[idx] = new Color(1, 1, 1, shallow);
                }
            }
            int t = 0;
            for (int j = 0; j < seg; j++)
            {
                for (int i = 0; i < seg; i++)
                {
                    int a = j * (seg + 1) + i, b = a + 1, c = a + seg + 1, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = d;
                    tris[t++] = a; tris[t++] = d; tris[t++] = b;
                }
            }
            var mesh = new Mesh { name = "Ocean", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 10, size));

            var mat = new Material(mats.Ocean) { name = "Ocean" };
            mat.SetColor("_Deep", SkyreachMaterials.Hex(0x0b4f7c));
            mat.SetColor("_Shallow", SkyreachMaterials.Hex(0x35c9c0));
            mat.SetColor("_SkyReflect", SkyreachMaterials.Hex(0xa9d6f5));
            var ocean = MeshFactory.Spawn("Ocean", mesh, mat, parent, new Vector3(0, WorldConfig.SeaLevel, 0));

            // Huge flat skirt so the horizon never shows an edge.
            var skirtMesh = new Mesh { name = "OceanSkirt" };
            float S = 60000;
            skirtMesh.vertices = new[] { new Vector3(-S, 0, -S), new Vector3(-S, 0, S), new Vector3(S, 0, S), new Vector3(S, 0, -S) };
            skirtMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            skirtMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            skirtMesh.RecalculateNormals();
            var skirtMat = mats.MakeLit("OceanSkirt", SkyreachMaterials.Hex(0x0b4f7c));
            MeshFactory.Spawn("OceanSkirt", skirtMesh, skirtMat, ocean.transform, new Vector3(0, -0.6f, 0));
            return ocean.transform;
        }

        // ------------------------------------------------------------------ clouds
        public static Transform BuildClouds(SkyreachMaterials mats, Transform parent)
        {
            var rand = new Mulberry32(WorldConfig.Seed + 31);
            float R() => (float)rand.Next();
            var puff = MeshFactory.Icosphere(1);
            var b = new MeshFactory.Builder();
            float range = WorldConfig.Size * 0.9f;
            for (int c = 0; c < WorldConfig.CloudCount; c++)
            {
                float cx = (R() - 0.5f) * range * 2;
                float cz = (R() - 0.5f) * range * 2;
                if (Mathf.Abs(cx) < 200 && Mathf.Abs(cz) < 900) continue;
                float cy = 420 + R() * 520;
                float size = 40 + R() * 70;
                for (int k = 0; k < 7; k++)
                {
                    float a = k / 7f * Mathf.PI * 2 + R();
                    float r = size * (0.2f + R() * 0.9f);
                    var p = new Vector3(cx + Mathf.Cos(a) * r * 1.4f, cy + R() * size * 0.3f, cz + Mathf.Sin(a) * r);
                    float ps = size * (0.45f + R() * 0.5f);
                    b.Add(puff, Matrix4x4.TRS(p, Quaternion.Euler(0, R() * 180, 0), new Vector3(ps * 1.3f, ps * 0.7f, ps)), Color.white);
                }
            }
            return MeshFactory.Spawn("Clouds", b.Build("Clouds"), mats.Cloud, parent, Vector3.zero).transform;
        }
    }
}
