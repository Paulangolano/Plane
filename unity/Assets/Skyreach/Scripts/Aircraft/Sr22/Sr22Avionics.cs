using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Skyreach
{
    public struct AvionicsData
    {
        public float Pitch, Bank, Beta, Ias, Tas, AltFt, AltSelFt, VsFpm, Heading, HdgBug;
        public float? VsSelFpm;
        public bool ApEngaged, AltArmed, EspActive, StallWarning, EngineRunning, ParkingBrake, OnGround, Assists, FlapOverspeed;
        public string Lateral, Vertical, SafeReturn, Chute, TimeText;
        public float Flaps, FlapsTarget, Trim, Rpm, Manifold, FuelFlow, FuelGal, GLoad, GroundSpeedKt, RingDistance;
        public int PercentPower, RingIndex, RingCount;
        public Vector3 Position;
        public Vector3[] Rings;
        public float Time;
    }

    /// <summary>
    /// SR22-style glass cockpit in world-space uGUI (port of webxr/src/aircraft/sr22/avionics.js):
    /// PFD with attitude, speed/altitude tapes, VSI, heading tape and autopilot modes; MFD with a
    /// track-up moving map and an engine page.
    /// </summary>
    public sealed class Sr22Avionics : MonoBehaviour
    {
        const float W = 1024, H = 683;
        static readonly Color Sky = SkyreachMaterials.Hex(0x2f6fc0), Ground = SkyreachMaterials.Hex(0x7a4f24);
        static readonly Color Tape = new Color(0.12f, 0.15f, 0.19f, 0.8f), Bg = SkyreachMaterials.Hex(0x0a0d10);
        static readonly Color White = Color.white, Dim = SkyreachMaterials.Hex(0x8fa0b0), Cyan = SkyreachMaterials.Hex(0x2fe0ff);
        static readonly Color Green = SkyreachMaterials.Hex(0x3be36a), Amber = SkyreachMaterials.Hex(0xffb21a), Red = SkyreachMaterials.Hex(0xff3b30);
        static readonly Color Magenta = SkyreachMaterials.Hex(0xff4fd8);

        // PFD
        RectTransform horizon, bankPointer, altBug, vsPointer;
        Text iasText, tasText, altText, altSelText, vsText, hdgText, hdgBugText, apText, latText, vertText, armedText, espText, flapsText, warnText;
        Image trimMarker, warnBg;
        readonly List<Text> iasTicks = new List<Text>(), altTicks = new List<Text>(), hdgTicks = new List<Text>();
        RectTransform hdgBugMark;
        // MFD
        RectTransform mapRoot, runway;
        RawImage terrain;
        readonly List<Image> ringMarks = new List<Image>();
        Text rpmText, powerText, mapText, footText, capsText, assistText, gsText;
        readonly Dictionary<string, Text> engine = new Dictionary<string, Text>();
        float pfdAccum = 1, mfdAccum = 1;
        const float MapPxPerM = (H * 0.5f) / (5 * 1852f);
        const float MapExtent = 7000;

        public static Sr22Avionics Create(Sr22Visual v, WorldData world, Camera cam)
        {
            var go = new GameObject("Avionics");
            go.transform.SetParent(v.transform, false);
            var av = go.AddComponent<Sr22Avionics>();
            av.BuildPfd(UIFactory.WorldCanvas("PFD", v.PfdAnchor, new Vector2(W, H), 0.36f, cam).transform);
            av.BuildMfd(UIFactory.WorldCanvas("MFD", v.MfdAnchor, new Vector2(W, H), 0.36f, cam).transform, world);
            return av;
        }

        // ------------------------------------------------------------------ PFD
        void BuildPfd(Transform root)
        {
            UIFactory.Image("Bg", root, Vector2.zero, new Vector2(W, H), Bg);
            var clip = UIFactory.Rect("AttitudeClip", root, Vector2.zero, new Vector2(W, 530));
            clip.gameObject.AddComponent<RectMask2D>();
            horizon = UIFactory.Rect("Horizon", clip, new Vector2(512, 300), Vector2.zero, new Vector2(0.5f, 0.5f));
            UIFactory.Image("Sky", horizon, new Vector2(-1400, -1400), new Vector2(2800, 1400), Sky);
            UIFactory.Image("Ground", horizon, new Vector2(-1400, 0), new Vector2(2800, 1400), Ground);
            UIFactory.Image("Line", horizon, new Vector2(-1400, -1.5f), new Vector2(2800, 3), White);
            for (int d = -30; d <= 30; d += 5)
            {
                if (d == 0) continue;
                float y = -d * 9f;
                float half = d % 10 == 0 ? 90 : 50;
                UIFactory.Image("Ladder", horizon, new Vector2(-half, y - 1), new Vector2(half * 2, 2), White);
                if (d % 10 == 0) UIFactory.Text("L", horizon, new Vector2(half + 8, y - 12), new Vector2(50, 24), Mathf.Abs(d).ToString(), 22, White);
            }
            // Bank scale + pointer
            foreach (int d in new[] { -60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60 })
            {
                var tick = UIFactory.Rect("BankTick", root, new Vector2(512, 300), Vector2.zero, new Vector2(0.5f, 0.5f));
                tick.localRotation = Quaternion.Euler(0, 0, -d);
                UIFactory.Image("T", tick, new Vector2(-1.5f, -255), new Vector2(3, Mathf.Abs(d) % 30 == 0 ? 25 : 15), White);
            }
            bankPointer = UIFactory.Rect("BankPointer", root, new Vector2(512, 300), Vector2.zero, new Vector2(0.5f, 0.5f));
            var tri = UIFactory.Image("Pointer", bankPointer, new Vector2(0, -216), new Vector2(28, 24), White, UIFactory.Triangle, new Vector2(0.5f, 0.5f));
            tri.rectTransform.localRotation = Quaternion.Euler(0, 0, 0);
            // Aircraft symbol
            UIFactory.Image("WingL", root, new Vector2(512 - 100, 297), new Vector2(70, 7), Amber);
            UIFactory.Image("WingR", root, new Vector2(512 + 30, 297), new Vector2(70, 7), Amber);
            UIFactory.Image("Dot", root, new Vector2(506, 294), new Vector2(12, 12), Amber, UIFactory.Circle);

            // Speed tape
            var sClip = UIFactory.Rect("SpeedTape", root, new Vector2(40, 70), new Vector2(130, 450));
            sClip.gameObject.AddComponent<RectMask2D>();
            UIFactory.Image("Bg", sClip, Vector2.zero, new Vector2(130, 450), Tape);
            for (int i = 0; i < 13; i++) iasTicks.Add(UIFactory.Text("Tick", sClip, Vector2.zero, new Vector2(110, 30), "", 26, White, TextAnchor.MiddleRight));
            UIFactory.Image("IasBox", root, new Vector2(36, 274), new Vector2(140, 52), Color.black);
            iasText = UIFactory.Text("Ias", root, new Vector2(36, 274), new Vector2(140, 52), "0", 40, White, TextAnchor.MiddleCenter);
            tasText = UIFactory.Text("Tas", root, new Vector2(40, 530), new Vector2(200, 30), "", 22, Dim, bold: false);

            // Altitude tape
            var aClip = UIFactory.Rect("AltTape", root, new Vector2(W - 250, 70), new Vector2(150, 450));
            aClip.gameObject.AddComponent<RectMask2D>();
            UIFactory.Image("Bg", aClip, Vector2.zero, new Vector2(150, 450), Tape);
            for (int i = 0; i < 13; i++) altTicks.Add(UIFactory.Text("Tick", aClip, Vector2.zero, new Vector2(120, 30), "", 24, White));
            altBug = UIFactory.Image("AltBug", aClip, new Vector2(0, 0), new Vector2(10, 28), Cyan).rectTransform;
            UIFactory.Image("AltBox", root, new Vector2(W - 256, 274), new Vector2(162, 52), Color.black);
            altText = UIFactory.Text("Alt", root, new Vector2(W - 256, 274), new Vector2(162, 52), "0", 36, White, TextAnchor.MiddleCenter);
            altSelText = UIFactory.Text("AltSel", root, new Vector2(W - 250, 22), new Vector2(150, 40), "", 28, Cyan, TextAnchor.MiddleCenter);

            // VSI
            UIFactory.Image("VsiBg", root, new Vector2(W - 90, 90), new Vector2(60, 420), Tape);
            vsPointer = UIFactory.Image("VsPointer", root, new Vector2(W - 88, 286), new Vector2(58, 28), White).rectTransform;
            vsText = UIFactory.Text("Vs", vsPointer, Vector2.zero, new Vector2(58, 28), "", 18, Color.black, TextAnchor.MiddleCenter);

            // Heading tape (bottom)
            var hClip = UIFactory.Rect("HdgTape", root, new Vector2(262, 575), new Vector2(500, 50));
            hClip.gameObject.AddComponent<RectMask2D>();
            UIFactory.Image("Bg", hClip, Vector2.zero, new Vector2(500, 50), Tape);
            for (int i = 0; i < 9; i++) hdgTicks.Add(UIFactory.Text("Tick", hClip, Vector2.zero, new Vector2(60, 50), "", 24, White, TextAnchor.MiddleCenter));
            hdgBugMark = UIFactory.Image("HdgBug", hClip, Vector2.zero, new Vector2(16, 10), Cyan).rectTransform;
            UIFactory.Image("HdgBox", root, new Vector2(457, 535), new Vector2(110, 40), Color.black);
            hdgText = UIFactory.Text("Hdg", root, new Vector2(457, 535), new Vector2(110, 40), "000°", 30, White, TextAnchor.MiddleCenter);
            hdgBugText = UIFactory.Text("HdgBugText", root, new Vector2(262, 630), new Vector2(200, 40), "", 24, Cyan);

            // Annunciator bar
            UIFactory.Image("Ann", root, Vector2.zero, new Vector2(W, 48), new Color(0, 0, 0, 0.75f));
            apText = UIFactory.Text("Ap", root, new Vector2(412, 4), new Vector2(200, 40), "AP OFF", 28, Dim, TextAnchor.MiddleCenter);
            latText = UIFactory.Text("Lat", root, new Vector2(212, 4), new Vector2(200, 40), "ROL", 28, Dim, TextAnchor.MiddleCenter);
            vertText = UIFactory.Text("Vert", root, new Vector2(612, 4), new Vector2(200, 40), "PIT", 28, Dim, TextAnchor.MiddleCenter);
            armedText = UIFactory.Text("Armed", root, new Vector2(800, 4), new Vector2(120, 40), "", 26, White, TextAnchor.MiddleCenter);
            espText = UIFactory.Text("Esp", root, new Vector2(20, 4), new Vector2(100, 40), "", 28, Amber, TextAnchor.MiddleCenter);

            // Flaps / trim / warnings
            flapsText = UIFactory.Text("Flaps", root, new Vector2(30, 575), new Vector2(220, 30), "FLAPS UP", 22, White);
            UIFactory.Text("TrimL", root, new Vector2(30, 612), new Vector2(70, 30), "TRIM", 22, Dim);
            UIFactory.Image("TrimBar", root, new Vector2(100, 618), new Vector2(80, 20), new Color(0.3f, 0.35f, 0.4f));
            trimMarker = UIFactory.Image("TrimMark", root, new Vector2(137, 615), new Vector2(6, 26), Green);
            warnBg = UIFactory.Image("WarnBg", root, new Vector2(262, 80), new Vector2(500, 52), new Color(0, 0, 0, 0.7f));
            warnText = UIFactory.Text("Warn", root, new Vector2(262, 80), new Vector2(500, 52), "", 34, Red, TextAnchor.MiddleCenter);
        }

        // ------------------------------------------------------------------ MFD
        void BuildMfd(Transform root, WorldData world)
        {
            UIFactory.Image("Bg", root, Vector2.zero, new Vector2(W, H), Bg);
            var clip = UIFactory.Rect("MapClip", root, new Vector2(250, 0), new Vector2(W - 250, H));
            clip.gameObject.AddComponent<RectMask2D>();
            mapRoot = UIFactory.Rect("MapRoot", clip, new Vector2((W - 250) / 2, H * 0.38f), Vector2.zero, new Vector2(0.5f, 0.5f));
            terrain = new GameObject("Terrain", typeof(RectTransform)).AddComponent<RawImage>();
            terrain.transform.SetParent(mapRoot, false);
            terrain.texture = TerrainTexture(world);
            terrain.raycastTarget = false;
            var trt = terrain.rectTransform;
            trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = Vector2.one * (MapExtent * 2 * MapPxPerM);
            runway = UIFactory.Image("Runway", mapRoot, Vector2.zero, new Vector2(4, WorldData.RunwayHalfLength * 2 * MapPxPerM), new Color(0.92f, 0.92f, 0.92f), null, new Vector2(0.5f, 0.5f)).rectTransform;
            runway.anchorMin = runway.anchorMax = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < CourseConfig.RingCount; i++)
            {
                var mark = UIFactory.Image("Ring", mapRoot, Vector2.zero, new Vector2(14, 14), White, UIFactory.Circle, new Vector2(0.5f, 0.5f));
                mark.rectTransform.anchorMin = mark.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                ringMarks.Add(mark);
            }
            UIFactory.Image("Own", clip, new Vector2((W - 250) / 2, H * 0.62f), new Vector2(30, 40), White, UIFactory.Triangle, new Vector2(0.5f, 0.5f));
            UIFactory.Image("Header", root, new Vector2(250, 0), new Vector2(W - 250, 46), new Color(0, 0, 0, 0.75f));
            mapText = UIFactory.Text("Header", root, new Vector2(264, 6), new Vector2(W - 280, 36), "", 24, White);
            UIFactory.Image("Footer", root, new Vector2(250, H - 46), new Vector2(W - 250, 46), new Color(0, 0, 0, 0.75f));
            footText = UIFactory.Text("Footer", root, new Vector2(264, H - 40), new Vector2(480, 36), "", 24, White);
            capsText = UIFactory.Text("Caps", root, new Vector2(W - 290, H - 40), new Vector2(270, 36), "CAPS ARMED", 24, Dim, TextAnchor.UpperRight);

            UIFactory.Image("EngineBg", root, Vector2.zero, new Vector2(250, H), SkyreachMaterials.Hex(0x11161b));
            UIFactory.Text("RpmLabel", root, new Vector2(16, 30), new Vector2(220, 30), "RPM", 22, Dim, TextAnchor.UpperCenter);
            rpmText = UIFactory.Text("Rpm", root, new Vector2(16, 60), new Vector2(220, 70), "0", 60, White, TextAnchor.UpperCenter);
            string[] rows = { "% POWER", "MAN IN", "FUEL FLOW", "FUEL GAL", "OIL °F", "OIL PSI", "G LOAD", "OAT °C" };
            for (int i = 0; i < rows.Length; i++)
            {
                UIFactory.Text("L", root, new Vector2(16, 200 + i * 45), new Vector2(140, 34), rows[i], 22, Dim);
                engine[rows[i]] = UIFactory.Text("V", root, new Vector2(120, 196 + i * 45), new Vector2(114, 38), "", 28, White, TextAnchor.UpperRight);
            }
            powerText = engine["% POWER"];
            assistText = UIFactory.Text("Assist", root, new Vector2(16, 590), new Vector2(220, 30), "", 22, Green, TextAnchor.UpperCenter);
            gsText = UIFactory.Text("Gs", root, new Vector2(16, 630), new Vector2(220, 30), "", 22, Dim, TextAnchor.UpperCenter);
        }

        static Texture2D TerrainTexture(WorldData world)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float x = i / (float)size * 2 * MapExtent - MapExtent;
                    float z = j / (float)size * 2 * MapExtent - MapExtent;
                    float h = world.HeightAt(x, z);
                    Color32 c = h < -8 ? new Color32(12, 38, 70, 255) : h < 0.3f ? new Color32(24, 92, 120, 255) : h < 6 ? new Color32(190, 170, 110, 255)
                        : h < 150 ? new Color32((byte)(40 + h * 0.3f), (byte)(110 - h * 0.2f), 45, 255) : new Color32(160, 140, 115, 255);
                    px[j * size + i] = c;
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ update
        public void Tick(float dt, AvionicsData d)
        {
            pfdAccum += dt;
            mfdAccum += dt;
            if (pfdAccum >= 1f / 15f)
            {
                pfdAccum = 0;
                DrawPfd(d);
            }
            if (mfdAccum >= 1f / 8f)
            {
                mfdAccum = 0;
                DrawMfd(d);
            }
        }

        void DrawPfd(AvionicsData d)
        {
            float bankDeg = d.Bank * Mathf.Rad2Deg;
            horizon.localRotation = Quaternion.Euler(0, 0, bankDeg);
            Vector2 off = horizon.localRotation * new Vector3(0, -d.Pitch * Mathf.Rad2Deg * 9f, 0);
            horizon.anchoredPosition = new Vector2(512, -300) + off;
            bankPointer.localRotation = Quaternion.Euler(0, 0, bankDeg);

            // Speed tape: ticks every 10 kt, 4.2 px per knot, centred at y = 225 in the tape.
            int baseKt = Mathf.FloorToInt(d.Ias / 10) * 10;
            for (int i = 0; i < iasTicks.Count; i++)
            {
                int v = baseKt + (i - 6) * 10;
                var t = iasTicks[i];
                t.text = v >= 0 && v % 20 == 0 ? v.ToString() : v >= 0 ? "–" : "";
                ((RectTransform)t.transform).anchoredPosition = new Vector2(8, -(225 - (v - d.Ias) * 4.2f) + 15);
                t.color = v >= Sr22Data.Vne ? Red : v >= Sr22Data.Vno ? Amber : v < Sr22Data.Vso ? Red : White;
            }
            iasText.text = Mathf.RoundToInt(d.Ias).ToString();
            iasText.color = (!d.OnGround && d.Ias < Sr22Data.Vs1) || d.Ias > Sr22Data.Vne ? Red : White;
            tasText.text = $"TAS {Mathf.RoundToInt(d.Tas)}KT";

            // Altitude tape: ticks every 100 ft, 0.42 px per foot.
            int baseFt = Mathf.FloorToInt(d.AltFt / 100) * 100;
            for (int i = 0; i < altTicks.Count; i++)
            {
                int a = baseFt + (i - 6) * 100;
                var t = altTicks[i];
                t.text = a % 200 == 0 ? a.ToString() : "–";
                ((RectTransform)t.transform).anchoredPosition = new Vector2(24, -(225 - (a - d.AltFt) * 0.42f) + 15);
            }
            altBug.anchoredPosition = new Vector2(0, -Mathf.Clamp(225 - (d.AltSelFt - d.AltFt) * 0.42f, 6, 444) + 14);
            altText.text = (Mathf.RoundToInt(d.AltFt / 10) * 10).ToString();
            altSelText.text = Mathf.RoundToInt(d.AltSelFt).ToString();

            float vy = Mathf.Clamp(d.VsFpm, -2000, 2000) * 0.1f;
            vsPointer.anchoredPosition = new Vector2(W - 88, -(286 - vy));
            vsText.text = Mathf.RoundToInt(Mathf.Abs(d.VsFpm) / 100).ToString();

            float hdg = (d.Heading * Mathf.Rad2Deg + 360) % 360;
            int baseH = Mathf.FloorToInt(hdg / 10) * 10;
            for (int i = 0; i < hdgTicks.Count; i++)
            {
                int h = baseH + (i - 4) * 10;
                int shown = ((h % 360) + 360) % 360;
                hdgTicks[i].text = shown % 30 == 0 ? (shown == 0 ? "N" : shown == 90 ? "E" : shown == 180 ? "S" : shown == 270 ? "W" : (shown / 10).ToString()) : "|";
                ((RectTransform)hdgTicks[i].transform).anchoredPosition = new Vector2(250 + (h - hdg) * 6 - 30, 0);
            }
            float bugDeg = Mathf.DeltaAngle(hdg, d.HdgBug * Mathf.Rad2Deg);
            hdgBugMark.anchoredPosition = new Vector2(250 + Mathf.Clamp(bugDeg, -40, 40) * 6 - 8, 0);
            hdgText.text = Mathf.RoundToInt(hdg).ToString("000") + "°";
            hdgBugText.text = "HDG " + Mathf.RoundToInt((d.HdgBug * Mathf.Rad2Deg + 360) % 360).ToString("000") + "°";

            apText.text = d.ApEngaged ? "AP" : "AP OFF";
            apText.color = d.ApEngaged ? Green : Dim;
            latText.text = d.Lateral;
            vertText.text = d.Vertical == "VS" && d.VsSelFpm.HasValue ? $"VS {Mathf.RoundToInt(d.VsSelFpm.Value)}" : d.Vertical;
            latText.color = vertText.color = d.ApEngaged ? Green : Dim;
            armedText.text = d.AltArmed ? "ALTS" : "";
            espText.text = d.EspActive ? "ESP" : "";

            flapsText.text = "FLAPS " + new[] { "UP", "50%", "100%" }[Sr22Data.FlapIndex(d.FlapsTarget)] + (d.FlapOverspeed ? "  OVERSPEED" : "");
            flapsText.color = Mathf.Abs(d.Flaps - d.FlapsTarget) > 0.01f || d.FlapOverspeed ? Amber : White;
            trimMarker.rectTransform.anchoredPosition = new Vector2(137 + d.Trim * 38, -615);

            string warn = null;
            Color wc = Red;
            if (d.StallWarning && Mathf.FloorToInt(d.Time * 3) % 2 == 0) warn = "STALL";
            else if (!string.IsNullOrEmpty(d.SafeReturn)) { warn = d.SafeReturn; wc = Green; }
            else if (d.Chute == "deployed" || d.Chute == "inflating") { warn = "PARACHUTE DEPLOYED"; wc = Amber; }
            else if (!d.EngineRunning) { warn = "ENGINE OFF"; wc = Amber; }
            else if (d.ParkingBrake && d.OnGround) { warn = "PARKING BRAKE"; wc = Amber; }
            warnBg.enabled = warn != null;
            warnText.text = warn ?? "";
            warnText.color = wc;
            warnText.fontSize = warn != null && warn.Length > 18 ? 24 : 34;
        }

        void DrawMfd(AvionicsData d)
        {
            mapRoot.anchoredPosition = new Vector2((W - 250) / 2, -H * 0.62f);
            mapRoot.localRotation = Quaternion.Euler(0, 0, d.Heading * Mathf.Rad2Deg);
            Vector2 Map(float x, float z) => new Vector2((x - d.Position.x) * MapPxPerM, (z - d.Position.z) * MapPxPerM);
            terrain.rectTransform.anchoredPosition = Map(0, 0);
            runway.anchoredPosition = Map(0, 0);
            for (int i = 0; i < ringMarks.Count && i < d.Rings.Length; i++)
            {
                var mark = ringMarks[i];
                mark.enabled = i >= d.RingIndex;
                mark.color = i == d.RingIndex ? Magenta : new Color(1, 1, 1, 0.75f);
                mark.rectTransform.sizeDelta = Vector2.one * (i == d.RingIndex ? 18 : 12);
                mark.rectTransform.anchoredPosition = Map(d.Rings[i].x, d.Rings[i].z);
            }
            mapText.text = d.RingIndex < d.RingCount ? $"MAP · TRK UP · 5 NM      RING {d.RingIndex + 1}  {d.RingDistance / 1852f:0.0} NM" : "MAP · TRK UP · 5 NM      COURSE COMPLETE";
            footText.text = $"RINGS {Mathf.Min(d.RingIndex, d.RingCount)}/{d.RingCount}   TIME {d.TimeText}";
            capsText.text = d.Chute == "stowed" ? "CAPS ARMED" : "CAPS " + d.Chute.ToUpperInvariant();
            capsText.color = d.Chute == "stowed" ? Dim : Amber;

            rpmText.text = (Mathf.RoundToInt(d.Rpm / 10) * 10).ToString();
            powerText.text = d.PercentPower.ToString();
            powerText.color = Green;
            engine["MAN IN"].text = d.Manifold.ToString("0.0");
            engine["FUEL FLOW"].text = d.FuelFlow.ToString("0.0");
            engine["FUEL GAL"].text = d.FuelGal.ToString("0");
            engine["FUEL GAL"].color = d.FuelGal < 15 ? Amber : White;
            engine["OIL °F"].text = d.EngineRunning ? "195" : "120";
            engine["OIL PSI"].text = d.EngineRunning ? "52" : "0";
            engine["OIL PSI"].color = d.EngineRunning ? White : Red;
            engine["G LOAD"].text = d.GLoad.ToString("0.0");
            engine["OAT °C"].text = Mathf.RoundToInt(15 - d.AltFt * 0.002f).ToString();
            assistText.text = d.Assists ? "ASSISTS ON" : "ASSISTS OFF";
            assistText.color = d.Assists ? Green : Dim;
            gsText.text = $"GS {Mathf.RoundToInt(d.GroundSpeedKt)} KT";
        }
    }
}
