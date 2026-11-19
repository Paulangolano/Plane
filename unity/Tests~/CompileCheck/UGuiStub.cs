// Minimal stand-in for the com.unity.ugui package (only what Skyreach uses), for compile checking.
namespace UnityEngine.UI
{
    public abstract class Graphic : MonoBehaviour
    {
        public virtual Color color { get; set; }
        public bool raycastTarget { get; set; }
        public new bool enabled { get; set; }
        public RectTransform rectTransform => null;
    }
    public class MaskableGraphic : Graphic { }
    public class Image : MaskableGraphic
    {
        public enum Type { Simple, Sliced, Tiled, Filled }
        public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
        public Sprite sprite { get; set; }
        public Type type { get; set; }
        public FillMethod fillMethod { get; set; }
        public float fillAmount { get; set; }
    }
    public class Text : MaskableGraphic
    {
        public Font font { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public string text { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
    }
    public class RawImage : MaskableGraphic { public Texture texture { get; set; } }
    public class RectMask2D : MonoBehaviour { }
    public class Mask : MonoBehaviour { public bool showMaskGraphic { get; set; } }
    public class CanvasScaler : MonoBehaviour { public float dynamicPixelsPerUnit { get; set; } }
}
