using System.Collections.Generic;
using Gladiator.Audio;
using Gladiator.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Gladiator.UI
{
    /// <summary>
    /// What you own, shown when you open a chest (E): denarii, weapons, shields, armour, houses and their furniture.
    /// E / Esc closes it.
    /// </summary>
    public class InventoryPanel : MonoBehaviour
    {
        public static InventoryPanel Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;
        /// <summary>The text shown (tests).</summary>
        public string Shown { get; private set; } = "";

        GameObject root;
        Text title, body;
        int openedFrame;

        public static InventoryPanel Ensure()
        {
            if (Instance != null) return Instance;
            return new GameObject("InventoryPanel").AddComponent<InventoryPanel>();
        }

        void Awake()
        {
            Instance = this;
            var canvasGo = new GameObject("InventoryCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 405;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 896);
            scaler.matchWidthOrHeight = 1f;
            root = canvasGo;
            PS2UI.Stretch(PS2UI.Rect(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.5f)));
            var panel = PS2UI.Anchored(PS2UI.Rect(canvasGo.transform, "Panel", PS2UI.PanelBg), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 560));
            title = PS2UI.Text(panel, "", 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0, -36), new Vector2(720, 40), PS2UI.Gold);
            body = PS2UI.Text(panel, "", 17, TextAnchor.UpperLeft, new Vector2(0.5f, 1f), new Vector2(0, -290), new Vector2(690, 430), PS2UI.Parchment);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            body.supportRichText = true;
            PS2UI.Text(panel, "E / ESC cerrar", 13, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(560, 18), new Color(0.75f, 0.7f, 0.6f));
            root.SetActive(false);
        }

        public void Open(string heading, IList<KeyValuePair<string, string>> sections)
        {
            title.text = heading;
            var sb = new System.Text.StringBuilder();
            foreach (var s in sections)
                sb.Append("<color=#f2c14e>").Append(s.Key).Append("</color>\n").Append(string.IsNullOrEmpty(s.Value) ? "-" : s.Value).Append("\n\n");
            Shown = sb.ToString();
            body.text = Shown;
            openedFrame = Time.frameCount;
            root.SetActive(true);
            AudioManager.UI("ui_select");
        }

        public void Close() { if (IsOpen) { root.SetActive(false); AudioManager.UI("ui_back"); } }

        void Update()
        {
            if (!IsOpen || Time.frameCount == openedFrame) return;
            if (GameInput.BackPressed || GameInput.InteractPressed || GameInput.ConfirmPressed) Close();
        }
    }
}
