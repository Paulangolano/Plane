using UnityEngine;
using UnityEngine.UI;

namespace Skyreach
{
    /// <summary>Big readable messages on a panel fixed to the cockpit, ~2.6 m ahead.</summary>
    public sealed class MessagePanel : MonoBehaviour
    {
        Text title, subtitle;
        CanvasGroup group;
        float timer;
        public string Current { get; private set; }

        public static MessagePanel Create(Transform jet, Camera cam)
        {
            var canvas = UIFactory.WorldCanvas("Messages", jet, new Vector2(1024, 448), 1.6f, cam);
            canvas.transform.localPosition = new Vector3(0, 0.2f, 2.6f);
            canvas.sortingOrder = 10;
            var panel = canvas.gameObject.AddComponent<MessagePanel>();
            panel.group = canvas.gameObject.AddComponent<CanvasGroup>();
            UIFactory.Image("Background", canvas.transform, new Vector2(24, 24), new Vector2(976, 400), new Color(0.03f, 0.06f, 0.11f, 0.72f), UIFactory.Rounded);
            panel.title = UIFactory.Text("Title", canvas.transform, new Vector2(40, 60), new Vector2(944, 120), "", 92, Color.white, TextAnchor.MiddleCenter);
            panel.subtitle = UIFactory.Text("Subtitle", canvas.transform, new Vector2(60, 196), new Vector2(904, 200), "", 40, SkyreachMaterials.Hex(0xcfe3f5), TextAnchor.UpperCenter, false);
            panel.Hide();
            return panel;
        }

        /// <param name="seconds">How long to show it; float.PositiveInfinity keeps it until replaced.</param>
        public void Show(string titleText, string subtitleText = "", float seconds = 2.5f, Color? colour = null)
        {
            title.text = titleText;
            title.color = colour ?? Color.white;
            subtitle.text = subtitleText;
            ((RectTransform)title.transform).anchoredPosition = new Vector2(40, string.IsNullOrEmpty(subtitleText) ? -164 : -60);
            group.alpha = 1;
            gameObject.SetActive(true);
            timer = seconds;
            Current = titleText;
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            Current = null;
        }

        void Update()
        {
            if (float.IsPositiveInfinity(timer)) return;
            timer -= Time.deltaTime;
            if (timer < 0.5f) group.alpha = Mathf.Max(0, timer / 0.5f);
            if (timer <= 0) Hide();
        }
    }
}
