using UnityEngine;

namespace Skyreach
{
    /// <summary>Cockpit control positions in the jet's local frame (origin = pilot's eye, +Z = nose).</summary>
    public static class CockpitLayout
    {
        public static readonly Vector3 StickPivot = new Vector3(0, -0.72f, 0.36f);
        public static readonly Vector3 StickGrip = new Vector3(0, -0.5f, 0.36f);
        public static readonly Vector3 ThrottlePivot = new Vector3(-0.3f, -0.58f, 0.2f);
        public static readonly Vector3 ThrottleGrip = new Vector3(-0.3f, -0.42f, 0.2f);
        public const float ThrottleTravelDeg = 31.5f;
        public const float StickMaxDeg = 21.8f;
    }

    /// <summary>References to the moving parts of the generated jet.</summary>
    public sealed class Jet : MonoBehaviour
    {
        public Transform Cockpit, HudAnchor, StickPivot, ThrottlePivot, Flame;
        public GameObject StickHighlight, ThrottleHighlight;
        public Material FlameMaterial;

        static readonly Color FlameNormal = new Color(1f, 0.63f, 0.25f, 0.85f);
        static readonly Color FlameBoost = new Color(0.44f, 0.71f, 1f, 0.85f);

        public void SetControls(float pitch, float roll, float throttle, bool boosting)
        {
            StickPivot.localRotation = Quaternion.Euler(-pitch * CockpitLayout.StickMaxDeg, 0, -roll * CockpitLayout.StickMaxDeg);
            ThrottlePivot.localRotation = Quaternion.Euler((throttle - 0.5f) * CockpitLayout.ThrottleTravelDeg, 0, 0);
            float f = boosting ? 1.6f + Mathf.Sin(Time.time * 40) * 0.15f : 0.25f + throttle * 0.6f;
            Flame.localScale = new Vector3(1, f, 1);
            FlameMaterial.color = boosting ? FlameBoost : FlameNormal;
        }
    }

    public static class JetBuilder
    {
        const float FuseY = -1.15f;

        public static Jet Build(SkyreachMaterials m, Transform parent)
        {
            var root = new GameObject("Jet");
            root.transform.SetParent(parent, false);
            var jet = root.AddComponent<Jet>();
            var t = root.transform;

            // ---------------- exterior (split around the open cockpit) ----------------
            var ext = new GameObject("Exterior").transform;
            ext.SetParent(t, false);
            var alongZ = Quaternion.Euler(90, 0, 0); // cylinder axis Y -> +Z
            MeshFactory.Spawn("FrontBody", MeshFactory.Cylinder(0.92f, 0.9f, 2.2f, 12), m.Paint, ext, new Vector3(0, FuseY, 2.1f), alongZ);
            MeshFactory.Spawn("RearBody", MeshFactory.Cylinder(0.75f, 0.92f, 6.6f, 12), m.Paint, ext, new Vector3(0, FuseY, -4.0f), Quaternion.Euler(-90, 0, 0));
            MeshFactory.Spawn("Nose", MeshFactory.Cone(0.9f, 4.0f, 12), m.Paint, ext, new Vector3(0, FuseY, 5.2f), alongZ);
            MeshFactory.Spawn("NoseTip", MeshFactory.Cone(0.3f, 1.1f, 10), m.Dark, ext, new Vector3(0, FuseY, 7.6f), alongZ);
            MeshFactory.Spawn("NoseStripe", MeshFactory.Cylinder(0.94f, 0.94f, 0.3f, 12), m.Stripe, ext, new Vector3(0, FuseY, 3.15f), alongZ);

            var wing = WingMesh();
            MeshFactory.Spawn("WingR", wing, m.Paint, ext, new Vector3(0.7f, -1.45f, -0.6f));
            MeshFactory.Spawn("WingL", wing, m.Paint, ext, new Vector3(-0.7f, -1.45f, -0.6f), null, new Vector3(-1, 1, 1));
            var tip = MeshFactory.Box(new Vector3(0.25f, 0.22f, 1.2f));
            MeshFactory.Spawn("TipR", tip, m.Stripe, ext, new Vector3(7.15f, -1.53f, -3.35f));
            MeshFactory.Spawn("TipL", tip, m.Stripe, ext, new Vector3(-7.15f, -1.53f, -3.35f));
            MeshFactory.Spawn("TailFin", MeshFactory.Box(new Vector3(0.14f, 2.6f, 2.2f)), m.Paint, ext, new Vector3(0, 0.35f, -6.6f), Quaternion.Euler(-20, 0, 0));
            var stab = MeshFactory.Box(new Vector3(3.4f, 0.1f, 1.4f));
            MeshFactory.Spawn("StabR", stab, m.Paint, ext, new Vector3(1.9f, -1.05f, -6.8f));
            MeshFactory.Spawn("StabL", stab, m.Paint, ext, new Vector3(-1.9f, -1.05f, -6.8f));
            MeshFactory.Spawn("Exhaust", MeshFactory.Cylinder(0.7f, 0.6f, 0.6f, 12), m.Dark, ext, new Vector3(0, FuseY, -7.4f), alongZ);
            jet.FlameMaterial = new Material(m.Flame);
            jet.Flame = MeshFactory.Spawn("Flame", MeshFactory.Cone(0.55f, 3, 10), jet.FlameMaterial, ext, new Vector3(0, FuseY, -9.1f), Quaternion.Euler(-90, 0, 0)).transform;

            // ---------------- cockpit ----------------
            var cp = new GameObject("Cockpit").transform;
            cp.SetParent(t, false);
            jet.Cockpit = cp;
            var side = MeshFactory.Box(new Vector3(0.08f, 0.75f, 2.6f));
            MeshFactory.Spawn("SideL", side, m.Paint, cp, new Vector3(-0.6f, -0.62f, 0));
            MeshFactory.Spawn("SideR", side, m.Paint, cp, new Vector3(0.6f, -0.62f, 0));
            MeshFactory.Spawn("Coaming", MeshFactory.Box(new Vector3(1.0f, 0.04f, 0.14f)), m.Dark, cp, new Vector3(0, -0.215f, 0.95f));
            MeshFactory.Spawn("Floor", MeshFactory.Box(new Vector3(1.15f, 0.05f, 2.4f)), m.Dark, cp, new Vector3(0, -1.0f, 0));
            var dashTilt = Quaternion.Euler(14.3f, 0, 0); // top edge leans away so the screen faces the pilot
            MeshFactory.Spawn("Dash", MeshFactory.Box(new Vector3(1.0f, 0.42f, 0.06f)), m.Panel, cp, new Vector3(0, -0.44f, 0.8f), dashTilt);
            var console = MeshFactory.Box(new Vector3(0.28f, 0.08f, 0.9f));
            MeshFactory.Spawn("ConsoleL", console, m.Panel, cp, new Vector3(-0.4f, -0.66f, 0.1f));
            MeshFactory.Spawn("ConsoleR", console, m.Panel, cp, new Vector3(0.4f, -0.66f, 0.1f));
            MeshFactory.Spawn("Seat", MeshFactory.Box(new Vector3(0.62f, 1.1f, 0.14f)), m.Dark, cp, new Vector3(0, -0.55f, -0.42f));
            MeshFactory.Spawn("Headrest", MeshFactory.Box(new Vector3(0.34f, 0.3f, 0.14f)), m.Dark, cp, new Vector3(0, 0.12f, -0.48f));

            var arch = MeshFactory.Torus(0.64f, 0.016f, 6, 24, Mathf.PI);
            MeshFactory.Spawn("CanopyFront", arch, m.Frame, cp, new Vector3(0, -0.22f, 1.05f));
            MeshFactory.Spawn("CanopyRear", arch, m.Frame, cp, new Vector3(0, -0.22f, -0.55f));
            MeshFactory.Spawn("CanopySpine", MeshFactory.Box(new Vector3(0.035f, 0.035f, 1.5f)), m.Frame, cp, new Vector3(0, 0.42f, 0.2f));
            var rail = MeshFactory.Box(new Vector3(0.04f, 0.04f, 2.4f));
            MeshFactory.Spawn("RailL", rail, m.Frame, cp, new Vector3(-0.6f, -0.22f, -0.1f));
            MeshFactory.Spawn("RailR", rail, m.Frame, cp, new Vector3(0.6f, -0.22f, -0.1f));

            jet.HudAnchor = new GameObject("HudAnchor").transform;
            jet.HudAnchor.SetParent(cp, false);
            jet.HudAnchor.localPosition = new Vector3(0, -0.42f, 0.765f);
            jet.HudAnchor.localRotation = dashTilt;

            // Flight stick
            jet.StickPivot = new GameObject("StickPivot").transform;
            jet.StickPivot.SetParent(cp, false);
            jet.StickPivot.localPosition = CockpitLayout.StickPivot;
            MeshFactory.Spawn("Shaft", MeshFactory.Cylinder(0.014f, 0.018f, 0.22f, 8), m.Dark, jet.StickPivot, new Vector3(0, 0.11f, 0));
            MeshFactory.Spawn("Grip", MeshFactory.Cylinder(0.028f, 0.028f, 0.14f, 8), m.Dark, jet.StickPivot, new Vector3(0, 0.25f, 0));
            MeshFactory.Spawn("Button", MeshFactory.Icosphere(1), m.LightRed, jet.StickPivot, new Vector3(0, 0.33f, 0.012f), null, Vector3.one * 0.012f);
            MeshFactory.Spawn("Boot", MeshFactory.Cylinder(0.05f, 0.07f, 0.05f, 10), m.Dark, jet.StickPivot, Vector3.zero);

            // Throttle
            jet.ThrottlePivot = new GameObject("ThrottlePivot").transform;
            jet.ThrottlePivot.SetParent(cp, false);
            jet.ThrottlePivot.localPosition = CockpitLayout.ThrottlePivot;
            MeshFactory.Spawn("Lever", MeshFactory.Box(new Vector3(0.02f, 0.16f, 0.02f)), m.Dark, jet.ThrottlePivot, new Vector3(0, 0.08f, 0));
            MeshFactory.Spawn("Knob", MeshFactory.Box(new Vector3(0.06f, 0.05f, 0.08f)), m.Stripe, jet.ThrottlePivot, new Vector3(0, 0.17f, 0));
            MeshFactory.Spawn("Track", MeshFactory.Box(new Vector3(0.04f, 0.02f, 0.22f)), m.Panel, cp, CockpitLayout.ThrottlePivot);

            var ring = MeshFactory.Torus(0.06f, 0.006f, 6, 20);
            jet.StickHighlight = MeshFactory.Spawn("StickHighlight", ring, m.Highlight, jet.StickPivot, new Vector3(0, 0.25f, 0), Quaternion.Euler(90, 0, 0));
            jet.ThrottleHighlight = MeshFactory.Spawn("ThrottleHighlight", ring, m.Highlight, jet.ThrottlePivot, new Vector3(0, 0.17f, 0), Quaternion.Euler(90, 0, 0));
            jet.StickHighlight.SetActive(false);
            jet.ThrottleHighlight.SetActive(false);
            return jet;
        }

        /// <summary>Swept wing (right side), 16 cm thick, extending toward +X and back toward -Z.</summary>
        static Mesh WingMesh()
        {
            Vector2[] outline = { new Vector2(0, 0), new Vector2(6.5f, 2.2f), new Vector2(6.5f, 3.3f), new Vector2(0, 4.6f) };
            const float thick = 0.16f;
            var b = new MeshFactory.Builder();
            Vector3 Top(int i) => new Vector3(outline[i].x, 0, -outline[i].y);
            Vector3 Bot(int i) => new Vector3(outline[i].x, -thick, -outline[i].y);
            // top (+Y) and bottom (-Y)
            b.Quad(Top(0), Top(1), Top(2), Top(3), Color.white);
            b.Quad(Bot(0), Bot(3), Bot(2), Bot(1), Color.white);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                b.Quad(Top(i), Bot(i), Bot(j), Top(j), Color.white);
            }
            var mesh = b.Build("Wing");
            return mesh;
        }
    }
}
