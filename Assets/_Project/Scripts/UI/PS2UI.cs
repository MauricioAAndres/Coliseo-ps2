using UnityEngine;
using UnityEngine.UI;

namespace Gladiator.UI
{
    /// <summary>
    /// Tiny factory for flat PS2-style uGUI: solid rects, shadowed legacy text, and a canvas that renders
    /// into the low-res game camera (so everything comes out pixelated).
    /// </summary>
    public static class PS2UI
    {
        static Font font;
        public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static readonly Color Gold = new Color(1f, 0.85f, 0.3f);
        public static readonly Color Parchment = new Color(0.85f, 0.8f, 0.7f);
        public static readonly Color PanelBg = new Color(0.06f, 0.04f, 0.03f, 0.78f);
        public static readonly Color Green = new Color(0.3f, 0.85f, 0.25f);
        public static readonly Color Yellow = new Color(0.95f, 0.8f, 0.2f);
        public static readonly Color Red = new Color(0.85f, 0.15f, 0.1f);

        public static RectTransform CreateCanvas(GameObject host, Camera cam)
        {
            var canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            canvas.pixelPerfect = true;
            var scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            return (RectTransform)host.transform;
        }

        public static RectTransform Rect(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        public static RectTransform Anchored(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = r.pivot = anchor;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        public static void Stretch(RectTransform r, float inset = 0f)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
        }

        public static Text Text(Transform parent, string s, int size, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 sizeDelta, Color c)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Anchored((RectTransform)go.transform, anchor, pos, sizeDelta);
            var t = go.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.alignment = align;
            t.color = c;
            t.text = s;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = Color.black;
            sh.effectDistance = new Vector2(1, -1);
            return t;
        }
    }
}
