using UnityEngine;
using UnityEngine.UI;

namespace Skyreach
{
    public struct HudData
    {
        public float Speed, Altitude, VerticalSpeed, Throttle, BoostFuel, Bank, PitchAngle, Heading, Elapsed, RingDistance;
        public bool Boosting, Stalling, OnGround, Brake, HasRing;
        public int RingIndex;
        public float RingBearing;
    }

    /// <summary>Glass-cockpit display on the dashboard (same layout as the WebXR version).</summary>
    public sealed class CockpitHUD : MonoBehaviour
    {
        const float W = 1024, H = 440;
        static readonly Color Bg = SkyreachMaterials.Hex(0x0b1118);
        static readonly Color PanelBg = SkyreachMaterials.Hex(0x131c26);
        static readonly Color Label = SkyreachMaterials.Hex(0x7f97ad);
        static readonly Color Value = SkyreachMaterials.Hex(0xe8f4ff);
        static readonly Color Muted = SkyreachMaterials.Hex(0x9fb3c6);
        static readonly Color Amber = SkyreachMaterials.Hex(0xffb21a);
        static readonly Color Red = SkyreachMaterials.Hex(0xff5050);
        static readonly Color Green = SkyreachMaterials.Hex(0x7fe08a);
        static readonly Color Blue = SkyreachMaterials.Hex(0x46c3ff);

        Text speed, altitude, vspeed, rings, time, status, heading, ringDist;
        Image throttleBar, boostBar;
        RectTransform horizon, arrow;
        float accum = 1;

        public static CockpitHUD Create(Transform anchor, Camera cam)
        {
            var canvas = UIFactory.WorldCanvas("HUD", anchor, new Vector2(W, H), 0.92f, cam);
            canvas.transform.localPosition = new Vector3(0, 0, -0.002f);
            var hud = canvas.gameObject.AddComponent<CockpitHUD>();
            hud.Build(canvas.transform);
            return hud;
        }

        void Build(Transform root)
        {
            UIFactory.Image("Background", root, Vector2.zero, new Vector2(W, H), Bg);

            // Left panel: speed, throttle, boost
            UIFactory.Image("LeftPanel", root, new Vector2(12, 12), new Vector2(300, H - 24), PanelBg, UIFactory.Rounded);
            UIFactory.Text("SpeedLabel", root, new Vector2(32, 28), new Vector2(260, 30), "AIRSPEED", 24, Label);
            speed = UIFactory.Text("Speed", root, new Vector2(32, 56), new Vector2(200, 80), "0", 68, Value);
            UIFactory.Text("SpeedUnit", root, new Vector2(226, 96), new Vector2(80, 30), "km/h", 26, Muted, bold: false);
            UIFactory.Text("ThrottleLabel", root, new Vector2(32, 166), new Vector2(260, 30), "THROTTLE", 24, Label);
            throttleBar = Bar(root, new Vector2(32, 204), Blue);
            UIFactory.Text("BoostLabel", root, new Vector2(32, 244), new Vector2(260, 30), "BOOST", 24, Label);
            boostBar = Bar(root, new Vector2(32, 282), Amber);
            status = UIFactory.Text("Status", root, new Vector2(32, 340), new Vector2(260, 50), "", 38, Green);

            // Centre: artificial horizon in a circular mask
            var mask = UIFactory.Image("HorizonMask", root, new Vector2(512, 200), new Vector2(320, 320), Color.white, UIFactory.Circle, new Vector2(0.5f, 0.5f));
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            horizon = UIFactory.Rect("Horizon", mask.transform, new Vector2(160, 160), Vector2.zero, new Vector2(0.5f, 0.5f));
            UIFactory.Image("Sky", horizon, new Vector2(-640, -640), new Vector2(1280, 640), SkyreachMaterials.Hex(0x3c87d6));
            UIFactory.Image("Ground", horizon, new Vector2(-640, 0), new Vector2(1280, 640), SkyreachMaterials.Hex(0x8a5a2b));
            UIFactory.Image("HorizonLine", horizon, new Vector2(-640, -1.5f), new Vector2(1280, 3), Color.white);
            for (int deg = -30; deg <= 30; deg += 10)
            {
                if (deg == 0) continue;
                float y = -deg * Mathf.Deg2Rad * 260;
                UIFactory.Image("Ladder" + deg, horizon, new Vector2(-40, y - 1), new Vector2(80, 2), Color.white);
                UIFactory.Text("LadderText" + deg, horizon, new Vector2(48, y - 11), new Vector2(40, 24), Mathf.Abs(deg).ToString(), 18, Color.white, bold: false);
            }
            // Fixed aircraft symbol
            UIFactory.Image("WingL", root, new Vector2(432, 197), new Vector2(60, 6), Amber);
            UIFactory.Image("WingR", root, new Vector2(532, 197), new Vector2(60, 6), Amber);
            UIFactory.Image("Dot", root, new Vector2(506, 194), new Vector2(12, 12), Amber, UIFactory.Circle);
            heading = UIFactory.Text("Heading", root, new Vector2(412, 380), new Vector2(200, 40), "000°", 30, Value, TextAnchor.UpperCenter);

            // Right panel: altitude, rings, time, next-ring arrow
            UIFactory.Image("RightPanel", root, new Vector2(W - 312, 12), new Vector2(300, H - 24), PanelBg, UIFactory.Rounded);
            UIFactory.Text("AltLabel", root, new Vector2(W - 292, 28), new Vector2(260, 30), "ALTITUDE", 24, Label);
            altitude = UIFactory.Text("Altitude", root, new Vector2(W - 292, 56), new Vector2(200, 80), "0", 68, Value);
            UIFactory.Text("AltUnit", root, new Vector2(W - 72, 96), new Vector2(40, 30), "m", 26, Muted, bold: false);
            vspeed = UIFactory.Text("VSpeed", root, new Vector2(W - 292, 140), new Vector2(260, 30), "", 24, Muted, bold: false);
            UIFactory.Text("RingsLabel", root, new Vector2(W - 292, 190), new Vector2(160, 30), "RINGS", 24, Label);
            rings = UIFactory.Text("Rings", root, new Vector2(W - 292, 218), new Vector2(170, 60), "0/12", 50, Amber);
            UIFactory.Text("TimeLabel", root, new Vector2(W - 292, 292), new Vector2(160, 30), "TIME", 24, Label);
            time = UIFactory.Text("Time", root, new Vector2(W - 292, 320), new Vector2(200, 54), "0:00.0", 44, Value);
            arrow = UIFactory.Image("RingArrow", root, new Vector2(W - 70, 244), new Vector2(46, 56), Amber, UIFactory.Triangle, new Vector2(0.5f, 0.5f)).rectTransform;
            ringDist = UIFactory.Text("RingDist", root, new Vector2(W - 130, 282), new Vector2(120, 30), "", 24, Muted, TextAnchor.UpperCenter, false);
        }

        static Image Bar(Transform root, Vector2 pos, Color c)
        {
            UIFactory.Image("BarBg", root, pos, new Vector2(260, 26), SkyreachMaterials.Hex(0x0b1118));
            var fill = UIFactory.Image("BarFill", root, pos, new Vector2(260, 26), c);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            var white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            fill.sprite = Sprite.Create(white, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            return fill;
        }

        public void UpdateHud(float dt, HudData d)
        {
            accum += dt;
            if (accum < 1f / 15f) return;
            accum = 0;

            speed.text = Mathf.RoundToInt(d.Speed * 3.6f).ToString();
            speed.color = d.Stalling ? Red : Value;
            throttleBar.fillAmount = d.Throttle;
            boostBar.fillAmount = d.BoostFuel / FlightConfig.BoostDuration;
            boostBar.color = d.Boosting ? SkyreachMaterials.Hex(0x7fd4ff) : Amber;
            if (d.Stalling && !d.OnGround) { status.text = "STALL"; status.color = Red; }
            else if (d.Brake && d.OnGround) { status.text = "BRAKES"; status.color = Amber; }
            else if (d.OnGround) { status.text = d.Speed < 1 ? "PARKED" : "TAXI"; status.color = Green; }
            else status.text = "";

            horizon.localRotation = Quaternion.Euler(0, 0, d.Bank * Mathf.Rad2Deg);
            // Pitch offset is applied along the banked axis, like a real attitude indicator.
            Vector2 offset = horizon.localRotation * new Vector3(0, -d.PitchAngle * 260, 0);
            horizon.anchoredPosition = new Vector2(160, -160) + offset;
            float hdg = (d.Heading * Mathf.Rad2Deg + 360) % 360;
            heading.text = Mathf.RoundToInt(hdg).ToString("000") + "°";

            altitude.text = Mathf.Max(0, Mathf.RoundToInt(d.Altitude)).ToString();
            altitude.color = d.Altitude < 40 && !d.OnGround ? Amber : Value;
            vspeed.text = (d.VerticalSpeed >= 0 ? "climb " : "sink ") + Mathf.Abs(d.VerticalSpeed).ToString("0") + " m/s";
            rings.text = Mathf.Min(d.RingIndex, CourseConfig.RingCount) + "/" + CourseConfig.RingCount;
            time.text = FormatTime(d.Elapsed);
            arrow.gameObject.SetActive(d.HasRing);
            ringDist.gameObject.SetActive(d.HasRing);
            if (d.HasRing)
            {
                arrow.localRotation = Quaternion.Euler(0, 0, -d.RingBearing * Mathf.Rad2Deg);
                ringDist.text = (d.RingDistance / 1000f).ToString("0.0") + " km";
            }
        }

        public static string FormatTime(float t)
        {
            int m = Mathf.FloorToInt(t / 60);
            float s = t - m * 60;
            return m + ":" + s.ToString("00.0");
        }
    }
}
