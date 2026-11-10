using UnityEngine;
using UnityEngine.UI;

namespace Gladiator.UI
{
    /// <summary>
    /// Taunt wheel: hold T (gamepad D-pad up) and pick one of four gestures with the move keys / left stick,
    /// the mouse or the number keys 1-4; releasing performs it. Its own overlay canvas, so it works in every scene.
    /// </summary>
    public class TauntWheel : MonoBehaviour
    {
        /// <summary>Fighter taunt kind for each slot: up, right, down, left.</summary>
        public static readonly int[] Kinds = { 2, 3, 4, 5 };
        public static readonly string[] Labels = { "LEVANTAR LOS BRAZOS", "SALUDAR", "REVERENCIA", "NEGAR" };
        static readonly Vector2[] Offsets = { new Vector2(0, 92), new Vector2(170, 0), new Vector2(0, -92), new Vector2(-170, 0) };

        static readonly Color Normal = new Color(0.08f, 0.05f, 0.03f, 0.82f);
        static readonly Color Lit = new Color(0.62f, 0.38f, 0.08f, 0.95f);

        public bool IsOpen { get; private set; }
        public int Selected { get; private set; } = -1;

        GameObject root;
        readonly Image[] boxes = new Image[4];
        Font font;

        public static TauntWheel Create()
        {
            var go = new GameObject("TauntWheel");
            var w = go.AddComponent<TauntWheel>();
            w.Build();
            return w;
        }

        void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 1f;

            root = new GameObject("Wheel", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            var rrt = (RectTransform)root.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(10, 10);

            var center = Box(rrt, Vector2.zero, new Vector2(150, 44), new Color(0f, 0f, 0f, 0.6f));
            Label(center.rectTransform, "BURLA\n<size=10>elige y suelta</size>", 14);
            for (int i = 0; i < 4; i++)
            {
                boxes[i] = Box(rrt, Offsets[i], new Vector2(200, 46), Normal);
                Label(boxes[i].rectTransform, Labels[i] + "\n<size=10>" + (i + 1) + "</size>", 15);
            }
            root.SetActive(false);
        }

        Image Box(RectTransform parent, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject("Box", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            var ol = go.AddComponent<Outline>();
            ol.effectColor = new Color(0.9f, 0.7f, 0.3f, 0.8f);
            ol.effectDistance = new Vector2(2, -2);
            return img;
        }

        void Label(RectTransform parent, string s, int size)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(1f, 0.92f, 0.7f);
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = s;
        }

        public void Open()
        {
            IsOpen = true;
            SetSelected(-1);
            root.SetActive(true);
        }

        public void Close()
        {
            IsOpen = false;
            root.SetActive(false);
        }

        public void SetSelected(int i)
        {
            Selected = i;
            for (int k = 0; k < 4; k++) boxes[k].color = k == i ? Lit : Normal;
        }

        /// <summary>Slot for a direction (x right, y up), or -1 when it is too small.</summary>
        public static int SlotFor(Vector2 dir, float minLength)
        {
            if (dir.magnitude < minLength) return -1;
            if (Mathf.Abs(dir.y) >= Mathf.Abs(dir.x)) return dir.y > 0 ? 0 : 2;
            return dir.x > 0 ? 1 : 3;
        }
    }
}
