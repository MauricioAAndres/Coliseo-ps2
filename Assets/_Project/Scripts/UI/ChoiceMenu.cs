using System;
using System.Collections.Generic;
using Gladiator.Audio;
using Gladiator.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Gladiator.UI
{
    /// <summary>
    /// A small pick-one menu (training station: train for stats or free practice). W/S, E/Enter/A to choose,
    /// Esc/B to cancel; mouse hover + click. Disabled options are shown greyed out with their reason.
    /// </summary>
    public class ChoiceMenu : MonoBehaviour
    {
        public static ChoiceMenu Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;
        public int Selected { get; private set; }

        public class Option
        {
            public string Label, Blurb;
            public bool Enabled = true;
            public Action OnChoose;
        }

        GameObject root;
        RectTransform panel;
        Text title, descText;
        readonly List<Option> options = new List<Option>();
        readonly List<RectTransform> rows = new List<RectTransform>();
        Action onCancel;
        int openedFrame;

        public static ChoiceMenu Ensure()
        {
            if (Instance != null) return Instance;
            return new GameObject("ChoiceMenu").AddComponent<ChoiceMenu>();
        }

        void Awake()
        {
            Instance = this;
            var canvasGo = new GameObject("ChoiceCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 410;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 896);
            scaler.matchWidthOrHeight = 1f;
            root = canvasGo;
            var dim = PS2UI.Rect(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.45f));
            PS2UI.Stretch(dim);
            panel = PS2UI.Anchored(PS2UI.Rect(canvasGo.transform, "Panel", PS2UI.PanelBg), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600, 300));
            title = PS2UI.Text(panel, "", 28, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(560, 36), PS2UI.Gold);
            descText = PS2UI.Text(panel, "", 15, TextAnchor.UpperCenter, new Vector2(0.5f, 0f), new Vector2(0, 70), new Vector2(560, 40), PS2UI.Parchment);
            descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            PS2UI.Text(panel, "W/S elegir   E / ENTER aceptar   ESC volver", 13, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(0, 20), new Vector2(560, 18), new Color(0.75f, 0.7f, 0.6f));
            root.SetActive(false);
        }

        public void Open(string heading, IList<Option> opts, Action cancel = null)
        {
            foreach (var r in rows) Destroy(r.gameObject);
            rows.Clear();
            options.Clear();
            options.AddRange(opts);
            onCancel = cancel;
            title.text = heading;
            for (int i = 0; i < options.Count; i++)
            {
                var row = PS2UI.Anchored(PS2UI.Rect(panel, "Row" + i, new Color(0.16f, 0.11f, 0.07f, 0.85f)), new Vector2(0.5f, 1f),
                    new Vector2(0, -70 - i * 52), new Vector2(520, 44));
                PS2UI.Text(row, options[i].Label, 20, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500, 28), PS2UI.Parchment);
                rows.Add(row);
            }
            panel.sizeDelta = new Vector2(600, 190 + options.Count * 52);
            Selected = Mathf.Max(0, options.FindIndex(o => o.Enabled));
            openedFrame = Time.frameCount;
            root.SetActive(true);
            Refresh();
            AudioManager.UI("ui_select");
        }

        public void Close() { if (IsOpen) root.SetActive(false); }

        public void Select(int i) { Selected = Mathf.Clamp(i, 0, options.Count - 1); Refresh(); }

        public void Choose()
        {
            if (Selected < 0 || Selected >= options.Count || !options[Selected].Enabled) { AudioManager.UI("ui_back"); return; }
            var o = options[Selected];
            Close();
            AudioManager.UI("ui_select");
            o.OnChoose?.Invoke();
        }

        void Refresh()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                bool sel = i == Selected, on = options[i].Enabled;
                rows[i].GetComponent<Image>().color = sel ? new Color(0.55f, 0.36f, 0.12f, 0.95f) : new Color(0.16f, 0.11f, 0.07f, 0.85f);
                var t = rows[i].GetComponentInChildren<Text>();
                t.color = !on ? new Color(0.5f, 0.47f, 0.42f) : sel ? Color.white : PS2UI.Parchment;
            }
            descText.text = options.Count > 0 ? options[Selected].Blurb : "";
        }

        void Update()
        {
            if (!IsOpen || Time.frameCount == openedFrame) return;
            if (GameInput.MenuUpPressed) { Select(Selected - 1); AudioManager.UI("ui_move"); }
            if (GameInput.MenuDownPressed) { Select(Selected + 1); AudioManager.UI("ui_move"); }
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m != null && !GameInput.Paused)
            {
                Vector2 mp = m.position.ReadValue();
                for (int i = 0; i < rows.Count; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(rows[i], mp, null)) continue;
                    if (Selected != i && (m.delta.ReadValue().sqrMagnitude > 0.1f || m.leftButton.wasPressedThisFrame)) Select(i);
                    if (m.leftButton.wasPressedThisFrame) { Choose(); return; }
                }
            }
            if (GameInput.ConfirmPressed) { Choose(); return; }
            if (GameInput.BackPressed) { AudioManager.UI("ui_back"); Close(); onCancel?.Invoke(); }
        }
    }
}
