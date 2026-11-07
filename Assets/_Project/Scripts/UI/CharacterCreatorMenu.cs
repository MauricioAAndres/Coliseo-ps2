using System;
using System.Collections.Generic;
using Gladiator.Character;
using Gladiator.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Gladiator.UI
{
    /// <summary>
    /// The creator's menu: three tabs (CUERPO / CABEZA / ESTILO) of sliders and option pickers, flat PS2 style.
    /// W/S choose a row, A/D adjust (hold to slide), Tab / LB-RB change tab, R random, Enter finish.
    /// Edits the <see cref="CharacterAppearance"/> in place and raises <see cref="Changed"/>.
    /// </summary>
    public class CharacterCreatorMenu : MonoBehaviour
    {
        public static readonly string[] TabNames = { "CUERPO", "CABEZA", "ESTILO" };

        public event Action Changed;
        public event Action<int> TabChanged;
        public event Action Randomize;
        public event Action Finished;

        public int Tab { get; private set; }
        public int Row { get; private set; }
        public bool Confirming { get; private set; }
        /// <summary>Typing the gladiator's name (keyboard): the rest of the game input is muted meanwhile.</summary>
        public bool EditingName { get; private set; }
        const int MaxNameLength = 16;
        float caretTime;
        public CharacterAppearance Look { get; set; }

        class RowDef
        {
            public string Label;
            public Func<float> Get;            // sliders
            public Action<float> Set;
            public Func<float, string> Format;
            public int Count;                  // options
            public Func<int> GetIndex;
            public Action<int> SetIndex;
            public Func<int, string> Name;
            public Func<int, Color?> Swatch;
            public bool IsSlider => Get != null;
        }

        class RowUI
        {
            public Image Bg, BarBg, BarFill, Swatch;
            public Text Label, Value;
        }

        readonly List<RowDef>[] tabs = new List<RowDef>[3];
        readonly List<RowUI> rowUI = new List<RowUI>();
        readonly Text[] tabTexts = new Text[3];
        readonly Image[] tabBgs = new Image[3];
        Text hintText, infoText, confirmText;
        RectTransform confirmPanel;
        float holdTime;
        const int MaxRows = 10;

        public static CharacterCreatorMenu Create(RectTransform parent, CharacterAppearance look)
        {
            var bg = PS2UI.Rect(parent, "CreatorMenu", PS2UI.PanelBg);
            PS2UI.Anchored(bg, new Vector2(0, 1), new Vector2(10, -10), new Vector2(292, 384));
            var top = PS2UI.Rect(bg, "Top", new Color(0.6f, 0.45f, 0.2f));
            top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1);
            top.sizeDelta = new Vector2(0, 3); top.anchoredPosition = Vector2.zero;

            var m = bg.gameObject.AddComponent<CharacterCreatorMenu>();
            m.Look = look;
            m.DefineRows();
            PS2UI.Text(bg, "CREA TU GLADIADOR", 18, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(12, -9), new Vector2(270, 22), PS2UI.Gold);

            for (int i = 0; i < 3; i++)
            {
                var t = PS2UI.Rect(bg, "Tab" + i, new Color(0, 0, 0, 0));
                PS2UI.Anchored(t, new Vector2(0, 1), new Vector2(10 + i * 92, -36), new Vector2(88, 20));
                m.tabBgs[i] = t.GetComponent<Image>();
                m.tabTexts[i] = PS2UI.Text(t, TabNames[i], 13, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(88, 20), Color.white);
            }

            for (int i = 0; i < MaxRows; i++)
            {
                var r = PS2UI.Rect(bg, "Row" + i, new Color(0, 0, 0, 0));
                PS2UI.Anchored(r, new Vector2(0, 1), new Vector2(8, -64 - i * 27), new Vector2(276, 25));
                var ui = new RowUI { Bg = r.GetComponent<Image>() };
                ui.Label = PS2UI.Text(r, "", 12, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(110, 18), Color.white);
                var barBg = PS2UI.Rect(r, "BarBg", new Color(0.02f, 0.015f, 0.01f, 0.95f));
                PS2UI.Anchored(barBg, new Vector2(0, 0.5f), new Vector2(118, 0), new Vector2(104, 8));
                var fill = PS2UI.Rect(barBg, "Fill", PS2UI.Gold);
                fill.anchorMin = new Vector2(0, 0); fill.anchorMax = new Vector2(0, 1); fill.pivot = new Vector2(0, 0.5f);
                fill.anchoredPosition = Vector2.zero; fill.sizeDelta = new Vector2(52, 0);
                ui.BarBg = barBg.GetComponent<Image>();
                ui.BarFill = fill.GetComponent<Image>();
                var sw = PS2UI.Rect(r, "Swatch", Color.white);
                PS2UI.Anchored(sw, new Vector2(0, 0.5f), new Vector2(118, 0), new Vector2(14, 14));
                ui.Swatch = sw.GetComponent<Image>();
                ui.Value = PS2UI.Text(r, "", 11, TextAnchor.MiddleRight, new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(150, 18), PS2UI.Parchment);
                m.rowUI.Add(ui);
            }

            m.infoText = PS2UI.Text(bg, "", 10, TextAnchor.UpperLeft, new Vector2(0, 0), new Vector2(12, 30), new Vector2(270, 14), PS2UI.Parchment);
            m.hintText = PS2UI.Text(parent, "W/S elegir   A/D ajustar   TAB pestaña   R aleatorio   V animacion   Z/C girar   ENTER terminar  (en NOMBRE: ENTER para escribir)",
                10, TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(620, 14), PS2UI.Parchment);

            m.confirmPanel = PS2UI.Rect(parent, "Confirm", new Color(0.05f, 0.035f, 0.025f, 0.92f));
            PS2UI.Anchored(m.confirmPanel, new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(380, 92));
            m.confirmText = PS2UI.Text(m.confirmPanel, "", 15, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(360, 50), PS2UI.Gold);
            PS2UI.Text(m.confirmPanel, "ENTER / E  entrar al ludus        ESC / Q  seguir editando", 11, TextAnchor.LowerCenter,
                new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(360, 14), PS2UI.Parchment);
            m.confirmPanel.gameObject.SetActive(false);
            m.Refresh();
            return m;
        }

        // ------------------------------------------------------------------ rows
        RowDef Slider(string label, Func<float> get, Action<float> set, Func<float, string> fmt = null) =>
            new RowDef { Label = label, Get = get, Set = set, Format = fmt ?? (v => Mathf.RoundToInt(v * 100f) + "%") };

        RowDef Option(string label, int count, Func<int> get, Action<int> set, Func<int, string> name, Func<int, Color?> swatch = null) =>
            new RowDef { Label = label, Count = count, GetIndex = get, SetIndex = set, Name = name, Swatch = swatch };

        void DefineRows()
        {
            tabs[0] = new List<RowDef>
            {
                Slider("COMPLEXION", () => Look.Femininity, v => Look.Femininity = v,
                    v => v < 0.2f ? "MASCULINA" : v > 0.8f ? "FEMENINA" : "MIXTA " + Mathf.RoundToInt(v * 100f) + "%"),
                Slider("ALTURA", () => Look.Height, v => Look.Height = v, v => Look.HeightMeters.ToString("0.00").Replace('.', ',') + " m"),
                Slider("MUSCULATURA", () => Look.Muscle, v => Look.Muscle = v),
                Slider("PESO", () => Look.Fat, v => Look.Fat = v),
                Slider("HOMBROS", () => Look.Shoulders, v => Look.Shoulders = v),
                Slider("PIERNAS", () => Look.LegLength, v => Look.LegLength = v),
                Slider("BRAZOS", () => Look.ArmLength, v => Look.ArmLength = v),
                Slider("ESPALDA", () => Look.Posture, v => Look.Posture = v,
                    v => v < 0.2f ? "ENCORVADA" : v > 0.8f ? "MUY RECTA" : "RECTA " + Mathf.RoundToInt(v * 100f) + "%"),
                Slider("POSTURA HOMBROS", () => Look.ShoulderBack, v => Look.ShoulderBack = v,
                    v => v < 0.25f ? "ADELANTE" : v > 0.8f ? "MUY ATRAS" : v < 0.45f ? "NORMAL" : "ATRAS " + Mathf.RoundToInt(v * 100f) + "%"),
            };
            tabs[1] = new List<RowDef>
            {
                Slider("CABEZA", () => Look.HeadSize, v => Look.HeadSize = v),
                Slider("MANDIBULA", () => Look.Jaw, v => Look.Jaw = v),
                Slider("NARIZ", () => Look.Nose, v => Look.Nose = v),
                Slider("CUELLO", () => Look.Neck, v => Look.Neck = v),
                Option("PEINADO", CharacterAppearance.HairStyles.Length, () => Look.HairStyle, i => Look.HairStyle = i, i => CharacterAppearance.HairStyles[i]),
                Option("BARBA", CharacterAppearance.Beards.Length, () => Look.Beard, i => Look.Beard = i, i => CharacterAppearance.Beards[i]),
                Option("COLOR PELO", CharacterAppearance.HairColors.Length, () => Look.HairColor, i => Look.HairColor = i,
                    i => CharacterAppearance.HairColorNames[i], i => CharacterAppearance.HairColors[i]),
            };
            tabs[2] = new List<RowDef>
            {
                Option("NOMBRE", CharacterAppearance.Names.Length, () => Mathf.Max(0, Array.IndexOf(CharacterAppearance.Names, Look.Name)),
                    i => Look.Name = CharacterAppearance.Names[i],
                    i => (Look.Name ?? "").ToUpperInvariant() + (EditingName && (caretTime % 1f) < 0.55f ? "_" : EditingName ? " " : "")),
                Option("PIEL", CharacterAppearance.SkinTones.Length, () => Look.SkinTone, i => Look.SkinTone = i,
                    i => "TONO " + (i + 1), i => CharacterAppearance.SkinTones[i]),
                Option("CAMISA", CharacterAppearance.Shirts.Length, () => Look.Shirt, i => Look.Shirt = i, i => CharacterAppearance.Shirts[i]),
                Option("COLOR CAMISA", CharacterAppearance.ClothColors.Length, () => Look.ClothColor, i => Look.ClothColor = i,
                    i => CharacterAppearance.ClothColorNames[i], i => CharacterAppearance.ClothColors[i]),
                Option("PANTALON", CharacterAppearance.PantsNames.Length, () => Look.Pants, i => Look.Pants = i, i => CharacterAppearance.PantsNames[i]),
                Option("COLOR PANTALON", CharacterAppearance.ClothColors.Length, () => Look.PantsColor, i => Look.PantsColor = i,
                    i => CharacterAppearance.ClothColorNames[i], i => CharacterAppearance.ClothColors[i]),
                Option("CALZADO", CharacterAppearance.ShoesNames.Length, () => Look.Shoes, i => Look.Shoes = i, i => CharacterAppearance.ShoesNames[i]),
                Option("METAL", CharacterAppearance.Metals.Length, () => Look.Metal, i => Look.Metal = i,
                    i => CharacterAppearance.MetalNames[i], i => CharacterAppearance.Metals[i]),
                Option("PENACHO", CharacterAppearance.ClothColors.Length, () => Look.HelmetCrest, i => Look.HelmetCrest = i,
                    i => CharacterAppearance.ClothColorNames[i], i => CharacterAppearance.ClothColors[i]),
            };
        }

        // ------------------------------------------------------------------ API (also used by the autotest)
        public void SetTab(int t)
        {
            Tab = (t + tabs.Length) % tabs.Length;
            Row = Mathf.Min(Row, tabs[Tab].Count - 1);
            TabChanged?.Invoke(Tab);
            Refresh();
        }

        public void SelectRow(int r)
        {
            Row = Mathf.Clamp(r, 0, tabs[Tab].Count - 1);
            Refresh();
        }

        public void Step(int dir)
        {
            var d = tabs[Tab][Row];
            if (d.IsSlider) d.Set(Mathf.Clamp01(Mathf.Round((d.Get() + dir * 0.05f) * 20f) / 20f));
            else d.SetIndex(((d.GetIndex() + dir) % d.Count + d.Count) % d.Count);
            Changed?.Invoke();
            Refresh();
        }

        bool OnNameRow => Tab == 2 && Row == 0;

        /// <summary>Starts typing the name (E / Enter on the NOMBRE row). Enter or Esc finishes.</summary>
        public void BeginNameEdit()
        {
            if (EditingName) return;
            EditingName = true;
            caretTime = 0f;
            GameInput.Paused = true; // no W/A/S/D/R/V while typing
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k != null) k.onTextInput += OnChar;
            Refresh();
        }

        public void EndNameEdit()
        {
            if (!EditingName) return;
            EditingName = false;
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k != null) k.onTextInput -= OnChar;
            GameInput.Paused = false;
            GameInput.SuppressFrame = Time.frameCount;
            Look.Name = (Look.Name ?? "").Trim();
            if (Look.Name.Length == 0) Look.Name = CharacterAppearance.Names[0];
            Changed?.Invoke();
            Refresh();
        }

        /// <summary>Types text into the name (also used by the autotest).</summary>
        public void TypeText(string s)
        {
            foreach (char c in s) OnChar(c);
        }

        void OnChar(char c)
        {
            if (!EditingName || char.IsControl(c)) return;
            if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '\'' || c == '.')) return;
            if ((Look.Name ?? "").Length >= MaxNameLength) return;
            Look.Name = (Look.Name ?? "") + c;
            Refresh();
        }

        /// <summary>Clears the name before typing a new one.</summary>
        public void ClearName() { Look.Name = ""; Refresh(); }

        void OnDisable()
        {
            if (EditingName) EndNameEdit();
        }

        public void ShowConfirm(bool on)
        {
            Confirming = on;
            confirmPanel.gameObject.SetActive(on);
            confirmText.text = "¿Entrar al ludus como\n" + Look.Name.ToUpperInvariant() + "?";
        }

        public void SetInfo(string s) { infoText.text = s; }

        public void Refresh()
        {
            for (int i = 0; i < 3; i++)
            {
                bool on = i == Tab;
                tabBgs[i].color = on ? new Color(0.45f, 0.3f, 0.12f, 0.95f) : new Color(0.12f, 0.09f, 0.06f, 0.8f);
                tabTexts[i].color = on ? PS2UI.Gold : PS2UI.Parchment;
            }
            var rows = tabs[Tab];
            for (int i = 0; i < MaxRows; i++)
            {
                var ui = rowUI[i];
                bool used = i < rows.Count;
                ui.Bg.gameObject.SetActive(used);
                if (!used) continue;
                var d = rows[i];
                bool sel = i == Row;
                ui.Bg.color = sel ? new Color(0.45f, 0.3f, 0.12f, 0.85f) : new Color(0, 0, 0, 0.25f);
                ui.Label.text = (sel ? "> " : "  ") + d.Label;
                ui.Label.color = sel ? Color.white : PS2UI.Parchment;
                if (d.IsSlider)
                {
                    float v = d.Get();
                    ui.BarBg.gameObject.SetActive(true);
                    ui.Swatch.gameObject.SetActive(false);
                    ui.BarFill.rectTransform.sizeDelta = new Vector2(Mathf.Round(104f * v), 0);
                    ui.Value.text = d.Format(v);
                    ui.Value.rectTransform.sizeDelta = new Vector2(52, 18);
                }
                else
                {
                    int idx = d.GetIndex();
                    ui.BarBg.gameObject.SetActive(false);
                    Color? c = d.Swatch?.Invoke(idx);
                    ui.Swatch.gameObject.SetActive(c.HasValue);
                    if (c.HasValue) ui.Swatch.color = c.Value;
                    ui.Value.text = (sel ? "<  " : "") + d.Name(idx) + (sel ? "  >" : "");
                    ui.Value.rectTransform.sizeDelta = new Vector2(c.HasValue ? 132 : 160, 18);
                }
            }
        }

        // ------------------------------------------------------------------ input
        void Update()
        {
            if (Look == null) return;
            if (EditingName)
            {
                caretTime += Time.unscaledDeltaTime;
                var k = UnityEngine.InputSystem.Keyboard.current;
                if (k != null)
                {
                    if (k.backspaceKey.wasPressedThisFrame && !string.IsNullOrEmpty(Look.Name)) Look.Name = Look.Name.Substring(0, Look.Name.Length - 1);
                    if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame) { EndNameEdit(); return; }
                }
                Refresh();
                return;
            }
            GameInput.UpdateDeviceMode();
            if (Confirming)
            {
                if (GameInput.ConfirmPressed) { ShowConfirm(false); Finished?.Invoke(); }
                else if (GameInput.BackPressed) ShowConfirm(false);
                return;
            }

            if (GameInput.TabNextPressed) SetTab(Tab + 1);
            if (GameInput.TabPrevPressed) SetTab(Tab - 1);
            if (GameInput.MenuUpPressed) SelectRow(Row - 1);
            if (GameInput.MenuDownPressed) SelectRow(Row + 1);
            if (GameInput.RandomizePressed) { Randomize?.Invoke(); Refresh(); }
            // On the NOMBRE row, E / Enter starts typing (keyboard) instead of finishing.
            if (OnNameRow && GameInput.ConfirmPressed && !GameInput.UsingGamepad) { ClearName(); BeginNameEdit(); return; }
            if (GameInput.ConfirmPressed) { ShowConfirm(true); return; }

            var d = tabs[Tab][Row];
            if (GameInput.StepLeftPressed) { Step(-1); holdTime = 0f; }
            else if (GameInput.StepRightPressed) { Step(1); holdTime = 0f; }
            else if (d.IsSlider)
            {
                float ax = GameInput.SliderAxis;
                if (Mathf.Abs(ax) > 0.3f)
                {
                    holdTime += Time.unscaledDeltaTime;
                    if (holdTime > 0.22f)
                    {
                        d.Set(Mathf.Clamp01(d.Get() + Mathf.Sign(ax) * 0.55f * Time.unscaledDeltaTime));
                        Changed?.Invoke();
                        Refresh();
                    }
                }
                else holdTime = 0f;
            }
        }
    }
}
