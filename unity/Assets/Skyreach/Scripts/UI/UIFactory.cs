using System;
using UnityEngine;
using UnityEngine.UI;

namespace Skyreach
{
    /// <summary>Small helpers for building world-space uGUI in code.</summary>
    public static class UIFactory
    {
        static Font font;
        public static Font DefaultFont => font ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        static Sprite circle, triangle, rounded;
        public static Sprite Circle => circle ? circle : (circle = MakeSprite(128, (x, y) => (x * x + y * y) <= 1f ? 1f : 0f));
        public static Sprite Triangle => triangle ? triangle : (triangle = MakeSprite(64, (x, y) =>
        {
            // Arrow pointing up: tip at the top, notch at the bottom.
            float ty = (y + 1) / 2f; // 0 bottom .. 1 top
            float half = (1 - ty) * 0.6f;
            bool inside = Mathf.Abs(x) <= half && ty <= 1f;
            bool notch = ty < 0.25f && Mathf.Abs(x) < (0.25f - ty) * 2.4f;
            return inside && !notch ? 1f : 0f;
        }));
        public static Sprite Rounded => rounded ? rounded : (rounded = MakeRounded());

        public static Canvas WorldCanvas(string name, Transform parent, Vector2 pixels, float metresWide, Camera cam)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = cam;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = pixels;
            float s = metresWide / pixels.x;
            rt.localScale = new Vector3(s, s, s);
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4;
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            // Positions are measured from the top-left corner of the parent, like a canvas 2D context.
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = pivot ?? new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(pos.x, -pos.y);
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(string name, Transform parent, Vector2 pos, Vector2 size, Color color, Sprite sprite = null, Vector2? pivot = null)
        {
            var img = Rect(name, parent, pos, size, pivot).gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            if (sprite == Rounded) img.type = UnityEngine.UI.Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        public static Text Text(string name, Transform parent, Vector2 pos, Vector2 size, string text, int fontSize, Color color,
            TextAnchor align = TextAnchor.UpperLeft, bool bold = true)
        {
            var t = Rect(name, parent, pos, size).gameObject.AddComponent<Text>();
            t.font = DefaultFont;
            t.fontSize = fontSize;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static Sprite MakeSprite(int size, Func<float, float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float fx = (x + 0.5f) / size * 2 - 1, fy = (y + 0.5f) / size * 2 - 1;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(alpha(fx, fy) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeRounded()
        {
            const int size = 64, r = 20;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int cx = Mathf.Clamp(x, r, size - 1 - r), cy = Mathf.Clamp(y, r, size - 1 - r);
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }
    }
}
