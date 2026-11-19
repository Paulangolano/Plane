using UnityEngine;

namespace Skyreach
{
    /// <summary>
    /// Creates every material the game uses from the four Skyreach shaders.
    /// The shaders are referenced by the GameManager in the scene, which guarantees
    /// they are included in the Quest build.
    /// </summary>
    public sealed class SkyreachMaterials
    {
        public readonly Shader Lit, Ocean, Sky, Unlit;

        public Material Terrain, Paint, Stripe, Dark, Panel, Frame, Glass, Hangar;
        public Material RingActive, RingNext, RingDone, RingGlow, Cloud, Flame, Explosion;
        public Material LightWhite, LightRed, LightGreen, Glove, Highlight;

        static readonly int ColorId = Shader.PropertyToID("_Color");

        public SkyreachMaterials(Shader lit, Shader ocean, Shader sky, Shader unlit)
        {
            Lit = lit ? lit : Shader.Find("Skyreach/Lit");
            Ocean = ocean ? ocean : Shader.Find("Skyreach/Ocean");
            Sky = sky ? sky : Shader.Find("Skyreach/Sky");
            Unlit = unlit ? unlit : Shader.Find("Skyreach/Unlit");

            Terrain = MakeLit("Terrain", Color.white, vertexColors: true);
            Paint = MakeLit("Paint", Hex(0xdfe6ee));
            Stripe = MakeLit("Stripe", Hex(0xff7a1a));
            Dark = MakeLit("Dark", Hex(0x23282f));
            Panel = MakeLit("Panel", Hex(0x343b45));
            Frame = MakeLit("CanopyFrame", Hex(0x4a5560));
            Glass = MakeLit("TowerGlass", Hex(0x2b5f7a), Hex(0x0b2230));
            Hangar = MakeLit("Hangar", Hex(0xa9b3bd));
            RingNext = MakeLit("RingNext", Color.white, Hex(0x335577));
            RingDone = MakeLit("RingDone", Hex(0x48e070), Hex(0x0d3a18));
            Cloud = MakeLit("Cloud", Color.white, Hex(0x8796a8));
            Glove = MakeLit("Glove", Hex(0x3a8dde));

            RingActive = MakeUnlit("RingActive", Hex(0xffb21a));
            LightWhite = MakeUnlit("LightWhite", Color.white);
            LightRed = MakeUnlit("LightRed", Hex(0xff3b2f));
            LightGreen = MakeUnlit("LightGreen", Hex(0x3dff6a));
            RingGlow = MakeUnlit("RingGlow", new Color(1f, 0.83f, 0.42f, 0.18f), transparent: true);
            Flame = MakeUnlit("Flame", new Color(1f, 0.63f, 0.25f, 0.85f), transparent: true);
            Explosion = MakeUnlit("Explosion", new Color(1f, 0.7f, 0.25f, 0.9f), transparent: true);
            Highlight = MakeUnlit("Highlight", new Color(0.5f, 0.88f, 1f, 0.85f), transparent: true);
        }

        public Material MakeLit(string name, Color color, Color? emission = null, bool vertexColors = false, Texture tex = null)
        {
            var m = new Material(Lit) { name = name };
            m.SetColor(ColorId, color);
            m.SetColor("_Emission", emission ?? Color.black);
            m.SetFloat("_VertexColorWeight", vertexColors ? 1f : 0f);
            if (tex) m.SetTexture("_MainTex", tex);
            m.enableInstancing = true;
            return m;
        }

        public Material MakeUnlit(string name, Color color, bool transparent = false)
        {
            var m = new Material(Unlit) { name = name };
            m.SetColor(ColorId, color);
            if (transparent)
            {
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = 3000;
            }
            m.enableInstancing = true;
            return m;
        }

        public static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
    }
}
