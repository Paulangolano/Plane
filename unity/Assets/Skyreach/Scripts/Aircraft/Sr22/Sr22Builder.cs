using System.Collections.Generic;
using UnityEngine;

namespace Skyreach
{
    /// <summary>Cockpit positions in the SR22's local frame (origin = pilot's eye, left seat; +Z = nose).</summary>
    public static class Sr22Layout
    {
        public const float CX = 0.3f; // cabin centre line
        public const float PanelTiltDeg = 9.74f; // top of the panel leans away from the pilot
        public static readonly Vector3 PanelCenter = new Vector3(CX, -0.42f, 0.725f);
        public const float PanelHalfDepth = 0.025f;
        public static readonly Vector3 YokeGrip = new Vector3(-0.33f, -0.48f, 0.4f);
        public static readonly Vector3 PowerGrip = new Vector3(CX - 0.06f, -0.52f, 0.31f);
        public static readonly Vector3 CapsHandle = new Vector3(CX, 0.25f, 0.1f);
        public static readonly Vector3 SafeReturnButton = new Vector3(CX + 0.12f, 0.31f, 0.22f);

        /// <summary>A point on the instrument panel's face at about (x, y), `out` metres proud of it.</summary>
        public static Vector3 PanelPoint(float x, float y, float outward = 0)
        {
            float t = PanelTiltDeg * Mathf.Deg2Rad;
            float dy = (y - PanelCenter.y) / Mathf.Cos(t);
            float n = PanelHalfDepth + outward;
            return new Vector3(x, PanelCenter.y + dy * Mathf.Cos(t) + Mathf.Sin(t) * n, PanelCenter.z + dy * Mathf.Sin(t) - Mathf.Cos(t) * n);
        }

        public static Quaternion PanelRotation => Quaternion.Euler(PanelTiltDeg, 0, 0);
    }

    /// <summary>References to the moving parts of the generated SR22.</summary>
    public sealed class Sr22Visual : MonoBehaviour
    {
        public Transform Exterior, Cockpit, CabinShell, PfdAnchor, MfdAnchor;
        public Transform Yoke, Power, FlapHandle, Caps, Prop, Chute, Elevator, Rudder;
        public Transform[] Ailerons;
        public GameObject Disc;
        float propAngle;

        public void SetCockpitView(bool inCockpit) => CabinShell.gameObject.SetActive(!inCockpit);

        public void Animate(float dt, Sr22Physics m, float capsPull)
        {
            Yoke.localPosition = new Vector3(-0.36f, -0.48f, 0.42f - m.PitchInput * 0.045f);
            Yoke.localRotation = Quaternion.Euler(0, 0, -m.RollInput * 34f); // roll right = clockwise
            Power.localPosition = new Vector3(Sr22Layout.CX - 0.06f, -0.62f, 0.25f + m.Throttle * 0.13f);
            FlapHandle.localPosition = new Vector3(0, 0.025f - m.FlapsTarget * 0.05f, -0.015f);
            Caps.localPosition = Sr22Layout.CapsHandle - new Vector3(0, capsPull, 0);
            propAngle += m.Rpm / 60f * 360f * dt;
            Prop.localRotation = Quaternion.Euler(0, 0, propAngle);
            bool fast = m.Rpm > 500;
            Prop.gameObject.SetActive(!fast);
            Disc.SetActive(fast);
            Ailerons[0].localRotation = Quaternion.Euler(m.RollInput * 20f, 0, 0);  // right aileron up when rolling right
            Ailerons[1].localRotation = Quaternion.Euler(-m.RollInput * 20f, 0, 0);
            Elevator.localRotation = Quaternion.Euler(m.PitchInput * 23f, 0, 0);
            Rudder.localRotation = Quaternion.Euler(0, -m.YawInput * 23f, 0); // trailing edge right for right rudder
            bool chute = m.Chute == "inflating" || m.Chute == "deployed";
            Chute.gameObject.SetActive(chute);
            if (chute)
            {
                float s = Mathf.Min(1, m.ChuteT / Sr22Data.ChuteInflateTime);
                Chute.localScale = new Vector3(0.2f + 0.8f * s, 0.3f + 0.7f * s, 0.2f + 0.8f * s);
                Chute.rotation = Quaternion.identity; // canopy always above the aircraft
            }
        }
    }

    /// <summary>Builds the SR22-class exterior and left-seat cockpit from primitives (no imported models).</summary>
    public static class Sr22Builder
    {
        const float CX = Sr22Layout.CX;
        const float FY = -0.6f; // fuselage centre height

        public static Sr22Visual Build(SkyreachMaterials m, Transform parent)
        {
            var root = new GameObject("SR22");
            root.transform.SetParent(parent, false);
            var v = root.AddComponent<Sr22Visual>();

            var paint = m.MakeLit("Sr22Paint", SkyreachMaterials.Hex(0xf2f4f6));
            var stripe = m.MakeLit("Sr22Stripe", SkyreachMaterials.Hex(0x2e5d8a));
            var accent = m.MakeLit("Sr22Accent", SkyreachMaterials.Hex(0x9aa7b4));
            var interior = m.MakeLit("Sr22Interior", SkyreachMaterials.Hex(0x3a3f45));
            var headliner = m.MakeLit("Sr22Headliner", SkyreachMaterials.Hex(0xb9b3a8), SkyreachMaterials.Hex(0x4a4740));
            var leather = m.MakeLit("Sr22Leather", SkyreachMaterials.Hex(0x6b5440));
            var panel = m.MakeLit("Sr22Panel", SkyreachMaterials.Hex(0x1c2026));
            var glare = m.MakeLit("Sr22Glare", SkyreachMaterials.Hex(0x15181c));
            var metal = m.MakeLit("Sr22Metal", SkyreachMaterials.Hex(0x7c858f));
            var tyre = m.MakeLit("Sr22Tyre", SkyreachMaterials.Hex(0x1a1a1a));
            var red = m.MakeUnlit("Sr22Red", SkyreachMaterials.Hex(0xd8262b));

            // Static parts go under one group so they can be combined into a few draw calls.
            var stat = new GameObject("Static").transform;
            stat.SetParent(root.transform, false);
            var alongZ = Quaternion.Euler(90, 0, 0);   // primitive axis +Y → +Z (nose)
            var alongMinusZ = Quaternion.Euler(-90, 0, 0);
            var tyreAxis = Quaternion.Euler(0, 0, 90);

            // ---------------- exterior
            v.Exterior = stat;
            v.CabinShell = MeshFactory.Spawn("CabinShell", MeshFactory.Cylinder(0.68f, 0.7f, 2.5f, 16), paint, root.transform, new Vector3(CX, FY - 0.05f, 0.15f), alongZ).transform;
            MeshFactory.Spawn("Cowl", MeshFactory.Cylinder(0.38f, 0.68f, 1.3f, 16), paint, stat, new Vector3(CX, FY - 0.08f, 2.05f), alongZ);
            MeshFactory.Spawn("Spinner", MeshFactory.Cone(0.16f, 0.4f, 16), paint, stat, new Vector3(CX, FY - 0.08f, 2.88f), alongZ);
            MeshFactory.Spawn("TailCone", MeshFactory.Cylinder(0.16f, 0.7f, 4.2f, 16), paint, stat, new Vector3(CX, FY + 0.12f, -3.2f), alongMinusZ);
            MeshFactory.Spawn("Stripe", MeshFactory.Cylinder(0.705f, 0.705f, 0.9f, 16), stripe, stat, new Vector3(CX, FY - 0.05f, -0.9f), alongZ);

            float half = Sr22Data.WingSpan / 2;
            float dihedral = Mathf.Tan(4.5f * Mathf.Deg2Rad);
            MeshFactory.Spawn("Wing", WingMesh(half, dihedral), paint, stat, Vector3.zero);
            float tipY = -1.12f + (half - 0.55f) * dihedral;
            MeshFactory.Spawn("NavLeft", MeshFactory.Box(new Vector3(0.08f, 0.08f, 0.18f)), red, stat, new Vector3(CX - half - 0.02f, tipY, 0.2f));
            MeshFactory.Spawn("NavRight", MeshFactory.Box(new Vector3(0.08f, 0.08f, 0.18f)), m.LightGreen, stat, new Vector3(CX + half + 0.02f, tipY, 0.2f));

            v.Ailerons = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? 1 : -1;
                var pivot = new GameObject(i == 0 ? "AileronR" : "AileronL").transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = new Vector3(CX + s * (half - 1.9f), -1.12f + (half - 2.5f) * dihedral, -0.5f);
                MeshFactory.Spawn("Surface", MeshFactory.Box(new Vector3(2.6f, 0.04f, 0.32f)), accent, pivot, new Vector3(0, 0, -0.16f));
                v.Ailerons[i] = pivot;
            }

            MeshFactory.Spawn("HStab", MeshFactory.Box(new Vector3(3.9f, 0.08f, 0.9f)), paint, stat, new Vector3(CX, FY + 0.25f, -4.65f));
            v.Elevator = Pivot("Elevator", root.transform, new Vector3(CX, FY + 0.25f, -5.1f));
            MeshFactory.Spawn("Surface", MeshFactory.Box(new Vector3(3.7f, 0.05f, 0.4f)), accent, v.Elevator, new Vector3(0, 0, -0.2f));
            MeshFactory.Spawn("Fin", MeshFactory.Box(new Vector3(0.09f, 1.35f, 1.1f)), paint, stat, new Vector3(CX, FY + 0.95f, -4.65f), Quaternion.Euler(-14.3f, 0, 0));
            v.Rudder = Pivot("Rudder", root.transform, new Vector3(CX, FY + 0.95f, -5.25f));
            MeshFactory.Spawn("Surface", MeshFactory.Box(new Vector3(0.06f, 1.2f, 0.35f)), stripe, v.Rudder, new Vector3(0, 0, -0.17f));

            float gearY = -Sr22Data.GearHeight;
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? 1 : -1;
                MeshFactory.Spawn("Strut", MeshFactory.Box(new Vector3(0.06f, 0.55f, 0.08f)), metal, stat, new Vector3(CX + s * 1.35f, gearY + 0.45f, -0.15f), Quaternion.Euler(0, 0, s * -20f));
                MeshFactory.Spawn("Pant", MeshFactory.Cylinder(0.12f, 0.17f, 0.75f, 10), paint, stat, new Vector3(CX + s * 1.48f, gearY + 0.2f, -0.15f), alongZ);
                MeshFactory.Spawn("Tyre", MeshFactory.Cylinder(0.2f, 0.2f, 0.12f, 14), tyre, stat, new Vector3(CX + s * 1.48f, gearY + 0.2f, -0.15f), tyreAxis);
            }
            MeshFactory.Spawn("NoseStrut", MeshFactory.Box(new Vector3(0.05f, 0.6f, 0.05f)), metal, stat, new Vector3(CX, gearY + 0.45f, 1.95f));
            MeshFactory.Spawn("NoseTyre", MeshFactory.Cylinder(0.17f, 0.17f, 0.1f, 14), tyre, stat, new Vector3(CX, gearY + 0.17f, 1.95f), tyreAxis);

            v.Prop = Pivot("Prop", root.transform, new Vector3(CX, FY - 0.08f, 2.75f));
            for (int i = 0; i < 3; i++)
            {
                var holder = Pivot("Blade" + i, v.Prop, Vector3.zero);
                holder.localRotation = Quaternion.Euler(0, 0, i * 120);
                MeshFactory.Spawn("Blade", MeshFactory.Box(new Vector3(0.1f, 0.95f, 0.03f)), glare, holder, new Vector3(0, 0.5f, 0), Quaternion.Euler(0, 20, 0));
            }
            var discMat = m.MakeUnlit("PropDisc", new Color(0.12f, 0.15f, 0.17f, 0.08f), transparent: true);
            v.Disc = MeshFactory.Spawn("PropDisc", MeshFactory.Cylinder(0.98f, 0.98f, 0.01f, 32), discMat, root.transform, new Vector3(CX, FY - 0.08f, 2.76f), alongZ);
            v.Disc.SetActive(false);

            // Parachute canopy + rigging
            v.Chute = Pivot("Parachute", root.transform, Vector3.zero);
            var canopyMat = m.MakeLit("Canopy", SkyreachMaterials.Hex(0xff6a1a));
            MeshFactory.Spawn("Canopy", MeshFactory.Icosphere(1), canopyMat, v.Chute, new Vector3(CX, 18, 0), null, new Vector3(5.5f, 2f, 5.5f));
            var lineMesh = MeshFactory.Box(new Vector3(0.03f, 1, 0.03f));
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2;
                Vector3 top = new Vector3(CX + Mathf.Cos(a) * 5, 17, Mathf.Sin(a) * 5);
                Vector3 bottom = new Vector3(CX, 0.2f, 0.3f);
                Vector3 mid = (top + bottom) / 2;
                MeshFactory.Spawn("Line", lineMesh, metal, v.Chute, mid, Quaternion.FromToRotation(Vector3.up, top - bottom), new Vector3(1, (top - bottom).magnitude, 1));
            }
            v.Chute.gameObject.SetActive(false);

            // ---------------- cockpit
            v.Cockpit = stat;
            MeshFactory.Spawn("Floor", MeshFactory.Box(new Vector3(1.3f, 0.04f, 2.2f)), interior, stat, new Vector3(CX, -1.0f, 0.1f));
            for (int i = 0; i < 2; i++)
            {
                float x = CX + (i == 0 ? 0.66f : -0.66f);
                MeshFactory.Spawn("Wall", MeshFactory.Box(new Vector3(0.04f, 0.55f, 2.0f)), interior, stat, new Vector3(x, -0.7f, 0.05f));
                MeshFactory.Spawn("Sill", MeshFactory.Box(new Vector3(0.04f, 0.05f, 1.9f)), paint, stat, new Vector3(x, -0.4f, 0.05f));
                MeshFactory.Spawn("UpperFrame", MeshFactory.Box(new Vector3(0.03f, 0.03f, 1.2f)), interior, stat, new Vector3(x, 0.32f, -0.15f));
                MeshFactory.Spawn("APillar", MeshFactory.Box(new Vector3(0.03f, 0.8f, 0.03f)), interior, stat, new Vector3(x, -0.02f, 0.8f), Quaternion.Euler(-35.5f, 0, 0));
                MeshFactory.Spawn("BPillar", MeshFactory.Box(new Vector3(0.05f, 0.7f, 0.06f)), paint, stat, new Vector3(x, -0.08f, -0.72f));
            }
            MeshFactory.Spawn("Roof", MeshFactory.Box(new Vector3(1.36f, 0.03f, 1.2f)), headliner, stat, new Vector3(CX, 0.34f, -0.15f));
            MeshFactory.Spawn("WindscreenTop", MeshFactory.Box(new Vector3(1.3f, 0.03f, 0.05f)), interior, stat, new Vector3(CX, 0.33f, 0.46f));
            MeshFactory.Spawn("Glareshield", MeshFactory.Box(new Vector3(1.32f, 0.04f, 0.32f)), glare, stat, new Vector3(CX, -0.17f, 0.82f), Quaternion.Euler(-2.9f, 0, 0));
            MeshFactory.Spawn("Panel", MeshFactory.Box(new Vector3(1.3f, 0.5f, Sr22Layout.PanelHalfDepth * 2)), panel, stat, Sr22Layout.PanelCenter, Sr22Layout.PanelRotation);

            v.PfdAnchor = Pivot("PfdAnchor", root.transform, Sr22Layout.PanelPoint(0.02f, -0.36f, 0.006f));
            v.PfdAnchor.localRotation = Sr22Layout.PanelRotation;
            v.MfdAnchor = Pivot("MfdAnchor", root.transform, Sr22Layout.PanelPoint(0.43f, -0.36f, 0.006f));
            v.MfdAnchor.localRotation = Sr22Layout.PanelRotation;
            foreach (float x in new[] { 0.02f, 0.43f })
                MeshFactory.Spawn("Bezel", MeshFactory.Box(new Vector3(0.385f, 0.265f, 0.006f)), glare, stat, Sr22Layout.PanelPoint(x, -0.36f, 0.003f), Sr22Layout.PanelRotation);

            MeshFactory.Spawn("Console", MeshFactory.Box(new Vector3(0.2f, 0.22f, 0.75f)), panel, stat, new Vector3(CX, -0.75f, 0.25f));
            MeshFactory.Spawn("ConsoleTop", MeshFactory.Box(new Vector3(0.2f, 0.02f, 0.75f)), glare, stat, new Vector3(CX, -0.63f, 0.25f));
            MeshFactory.Spawn("PowerSlot", MeshFactory.Box(new Vector3(0.02f, 0.005f, 0.16f)), glare, stat, new Vector3(CX - 0.06f, -0.618f, 0.31f));
            for (int i = 0; i < 2; i++)
            {
                float x = CX + (i == 0 ? -0.3f : 0.3f);
                MeshFactory.Spawn("Seat", MeshFactory.Box(new Vector3(0.48f, 0.12f, 0.5f)), leather, stat, new Vector3(x, -0.92f, -0.05f));
                MeshFactory.Spawn("SeatBack", MeshFactory.Box(new Vector3(0.48f, 0.75f, 0.12f)), leather, stat, new Vector3(x, -0.55f, -0.36f));
            }
            MeshFactory.Spawn("YokeBase", MeshFactory.Box(new Vector3(0.06f, 0.06f, 0.25f)), glare, stat, new Vector3(-0.4f, -0.48f, 0.55f));
            MeshFactory.Spawn("CapsCover", MeshFactory.Box(new Vector3(0.16f, 0.012f, 0.1f)), red, stat, new Vector3(CX, 0.322f, 0.1f));

            // Side yoke
            v.Yoke = Pivot("Yoke", root.transform, new Vector3(-0.36f, -0.48f, 0.42f));
            MeshFactory.Spawn("Shaft", MeshFactory.Cylinder(0.015f, 0.015f, 0.16f, 8), metal, v.Yoke, new Vector3(0, 0, 0.05f), alongZ);
            var grip = Pivot("YokeGrip", v.Yoke, new Vector3(0.03f, 0, -0.02f));
            MeshFactory.Spawn("Bar", MeshFactory.Box(new Vector3(0.11f, 0.035f, 0.035f)), glare, grip, Vector3.zero);
            MeshFactory.Spawn("Handle", MeshFactory.Box(new Vector3(0.035f, 0.11f, 0.04f)), glare, grip, new Vector3(0.055f, -0.04f, 0));
            MeshFactory.Spawn("TrimHat", MeshFactory.Box(new Vector3(0.015f, 0.01f, 0.015f)), metal, grip, new Vector3(0.055f, 0.018f, 0));
            MeshFactory.Spawn("ApDisconnect", MeshFactory.Box(new Vector3(0.012f, 0.012f, 0.012f)), red, grip, new Vector3(0.02f, 0.022f, 0));

            // Power lever
            v.Power = Pivot("Power", root.transform, new Vector3(CX - 0.06f, -0.62f, 0.31f));
            MeshFactory.Spawn("Arm", MeshFactory.Box(new Vector3(0.018f, 0.09f, 0.018f)), metal, v.Power, new Vector3(0, 0.045f, 0));
            MeshFactory.Spawn("Knob", MeshFactory.Box(new Vector3(0.055f, 0.035f, 0.05f)), glare, v.Power, new Vector3(0, 0.1f, 0));

            // Flap selector
            var flapLever = Pivot("FlapLever", root.transform, Sr22Layout.PanelPoint(0.7f, -0.56f, 0.01f));
            flapLever.localRotation = Sr22Layout.PanelRotation;
            v.FlapHandle = MeshFactory.Spawn("FlapHandle", MeshFactory.Box(new Vector3(0.04f, 0.02f, 0.03f)), m.LightWhite, flapLever, new Vector3(0, 0.025f, -0.015f)).transform;

            // Parachute handle (overhead) and emergency autoland button
            v.Caps = Pivot("CapsHandle", root.transform, Sr22Layout.CapsHandle);
            MeshFactory.Spawn("Handle", MeshFactory.Box(new Vector3(0.11f, 0.02f, 0.025f)), red, v.Caps, Vector3.zero);
            MeshFactory.Spawn("Stem", MeshFactory.Box(new Vector3(0.012f, 0.07f, 0.012f)), metal, v.Caps, new Vector3(0, 0.04f, 0));
            MeshFactory.Spawn("SafeReturn", MeshFactory.Cylinder(0.022f, 0.022f, 0.018f, 20), red, stat, Sr22Layout.SafeReturnButton);
            Label(stat, "PULL ▼ PARACHUTE", new Vector3(CX, 0.316f, 0.03f), Quaternion.Euler(-90, 0, 0), 0.0045f, new Color(1, 0.85f, 0.85f));
            Label(stat, "SAFE RETURN · HOLD", new Vector3(Sr22Layout.SafeReturnButton.x, 0.316f, 0.17f), Quaternion.Euler(-90, 0, 0), 0.004f, Color.white);
            Label(root.transform, "POWER", new Vector3(CX - 0.06f, -0.616f, 0.2f), Quaternion.Euler(90, 0, 0), 0.004f, Color.white);

            StaticBatchingUtility.Combine(stat.gameObject);
            return v;
        }

        static Transform Pivot(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        public static TextMesh Label(Transform parent, string text, Vector3 pos, Quaternion rot, float size, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            var t = go.AddComponent<TextMesh>();
            t.font = UIFactory.DefaultFont;
            go.GetComponent<MeshRenderer>().sharedMaterial = UIFactory.DefaultFont.material;
            t.text = text;
            t.fontSize = 48;
            t.characterSize = size;
            t.anchor = TextAnchor.MiddleCenter;
            t.alignment = TextAlignment.Center;
            t.color = color;
            return t;
        }

        /// <summary>Low wing with dihedral, both halves, 12 cm thick. Faces are oriented outward automatically.</summary>
        static Mesh WingMesh(float half, float dihedral)
        {
            var b = new MeshFactory.Builder();
            const float r = 0.55f, rootChord = 1.6f, tipChord = 0.9f, y0 = -1.12f, t = 0.12f;
            float y1 = y0 + (half - r) * dihedral;
            foreach (float s in new[] { 1f, -1f })
            {
                Vector3 a = new Vector3(CX + s * r, y0, 0.55f);
                Vector3 bb = new Vector3(CX + s * half, y1, 0.35f);
                Vector3 c = new Vector3(CX + s * half, y1, 0.35f - tipChord);
                Vector3 d = new Vector3(CX + s * r, y0, 0.55f - rootChord);
                Vector3 up = new Vector3(0, t / 2, 0);
                Vector3[] top = { a + up, bb + up, c + up, d + up };
                Vector3[] bot = { a - up, bb - up, c - up, d - up };
                Vector3 centre = (a + bb + c + d) / 4;
                OrientedQuad(b, top[0], top[1], top[2], top[3], Vector3.up);
                OrientedQuad(b, bot[0], bot[1], bot[2], bot[3], Vector3.down);
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    Vector3 mid = (top[i] + top[j]) / 2;
                    Vector3 outward = mid - new Vector3(centre.x, mid.y, centre.z);
                    OrientedQuad(b, top[i], top[j], bot[j], bot[i], outward);
                }
            }
            return b.Build("Sr22Wing");
        }

        /// <summary>Adds a quad, flipping its winding so its front face points along `outward`.</summary>
        static void OrientedQuad(MeshFactory.Builder b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) >= 0) b.Quad(p0, p1, p2, p3, Color.white);
            else b.Quad(p0, p3, p2, p1, Color.white);
        }
    }
}
