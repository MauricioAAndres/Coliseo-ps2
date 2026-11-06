using System;
using System.Collections.Generic;
using Gladiator.Audio;
using Gladiator.Core;
using Gladiator.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace Gladiator.UI
{
    /// <summary>
    /// The arena gate (E in the ludus): pick the fight. Duel, 2 vs 1, free-for-all, waves or the champion, each with
    /// its reward. W/S (D-pad) to choose, E / Enter / A to go, Esc / B to stay in the ludus. Mouse: hover and click.
    /// </summary>
    public class ArenaGateMenu : MonoBehaviour
    {
        public static ArenaGateMenu Instance { get; private set; }
        public bool IsOpen => root != null && root.activeSelf;
        public int Selected { get; private set; }

        GameObject root;
        Text descText;
        readonly List<Image> rowBgs = new List<Image>();
        readonly List<Text> rowTexts = new List<Text>();
        readonly List<RectTransform> rowRects = new List<RectTransform>();
        Action<EncounterType> onChoose;
        int day = 1, openedFrame;

        public static ArenaGateMenu Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ArenaGateMenu");
            return go.AddComponent<ArenaGateMenu>();
        }

        void Awake()
        {
            Instance = this;
            var canvasGo = new GameObject("GateCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 896);
            scaler.matchWidthOrHeight = 1f;
            root = canvasGo;

            var dim = PS2UI.Rect(canvasGo.transform, "Dim", new Color(0f, 0f, 0f, 0.55f));
            PS2UI.Stretch(dim);
            var panel = PS2UI.Anchored(PS2UI.Rect(canvasGo.transform, "Panel", PS2UI.PanelBg), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 470));
            PS2UI.Text(panel, "PUERTA DE LA ARENA", 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0, -36), new Vector2(600, 44), PS2UI.Gold);
            PS2UI.Text(panel, "Elige el combate de hoy", 16, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0, -72), new Vector2(600, 22), PS2UI.Parchment);
            var modes = Encounters.MenuOrder;
            for (int i = 0; i < modes.Length; i++)
            {
                var row = PS2UI.Anchored(PS2UI.Rect(panel, "Row" + i, new Color(0.16f, 0.11f, 0.07f, 0.85f)), new Vector2(0.5f, 1f),
                    new Vector2(0, -102 - i * 52), new Vector2(560, 46));
                rowRects.Add(row);
                rowBgs.Add(row.GetComponent<Image>());
                rowTexts.Add(PS2UI.Text(row, Encounters.Title(modes[i]), 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(540, 30), PS2UI.Parchment));
            }
            descText = PS2UI.Text(panel, "", 15, TextAnchor.UpperCenter, new Vector2(0.5f, 0f), new Vector2(0, 76), new Vector2(580, 44), PS2UI.Parchment);
            descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            PS2UI.Text(panel, "W/S elegir   E / ENTER luchar   ESC volver        (mando: cruceta, A, B)", 13, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f), new Vector2(0, 22), new Vector2(600, 18), new Color(0.75f, 0.7f, 0.6f));
            root.SetActive(false);
        }

        public void Open(int currentDay, Action<EncounterType> choose)
        {
            day = currentDay;
            onChoose = choose;
            Selected = 0;
            openedFrame = Time.frameCount;
            root.SetActive(true);
            Refresh();
            AudioManager.UI("ui_select");
        }

        public void Close()
        {
            if (!IsOpen) return;
            root.SetActive(false);
        }

        public void Select(int i)
        {
            Selected = Mathf.Clamp(i, 0, Encounters.MenuOrder.Length - 1);
            Refresh();
        }

        /// <summary>Goes to the arena with the selected mode.</summary>
        public void Choose()
        {
            var mode = Encounters.MenuOrder[Selected];
            Close();
            AudioManager.UI("ui_select");
            onChoose?.Invoke(mode);
        }

        void Refresh()
        {
            for (int i = 0; i < rowBgs.Count; i++)
            {
                bool sel = i == Selected;
                rowBgs[i].color = sel ? new Color(0.55f, 0.36f, 0.12f, 0.95f) : new Color(0.16f, 0.11f, 0.07f, 0.85f);
                rowTexts[i].color = sel ? Color.white : PS2UI.Parchment;
                rowTexts[i].text = (sel ? "> " : "") + Encounters.Title(Encounters.MenuOrder[i]) + (sel ? " <" : "");
            }
            descText.text = Encounters.Blurb(Encounters.MenuOrder[Selected], day);
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
                for (int i = 0; i < rowRects.Count; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(rowRects[i], mp, null)) continue;
                    if (Selected != i && (m.delta.ReadValue().sqrMagnitude > 0.1f || m.leftButton.wasPressedThisFrame)) { Select(i); AudioManager.UI("ui_move"); }
                    if (m.leftButton.wasPressedThisFrame) { Choose(); return; }
                }
            }
            if (GameInput.ConfirmPressed) { Choose(); return; }
            if (GameInput.BackPressed) { AudioManager.UI("ui_back"); Close(); }
        }
    }
}
