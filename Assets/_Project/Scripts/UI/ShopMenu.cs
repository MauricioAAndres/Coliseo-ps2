using System;
using System.Collections.Generic;
using Gladiator.Core;
using Gladiator.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace Gladiator.UI
{
    /// <summary>
    /// The mercator, with two tabs (TAB / LB-RB to switch):
    ///   COMPRAR: the arsenal and the shields with price / owned state (scrolling list), E/A buys and equips.
    ///   EQUIPO:  what you carry into the arena: right hand (any owned weapon) and left hand (nothing, a shield, or a
    ///            second one-handed weapon); A/D or the D-pad cycles. Two-handers and the bow put the shield on the back.
    /// W/S to choose, Esc/Q/B to leave.
    /// </summary>
    public class ShopMenu : MonoBehaviour
    {
        public bool IsOpen => root != null && root.gameObject.activeSelf;
        public int Selected { get; private set; }
        /// <summary>0 = COMPRAR, 1 = EQUIPO.</summary>
        public int Tab { get; private set; }
        public event Action Closed;

        const int Visible = 9;
        const float RowH = 22f;

        RectTransform root;
        Text purseText, descText, statusText, hintText;
        readonly Text[] tabTexts = new Text[5];
        public static readonly string[] TabNames = { "ARMAS", "ARMADURAS", "ESCUDOS", "EQUIPO", "ASPECTO" };
        const int EquipTab = 3, LookTab = 4;
        bool IsBuyTab => Tab < EquipTab;
        readonly List<Text> rowTexts = new List<Text>();
        readonly List<Image> rowBgs = new List<Image>();
        PlayerProfile profile;
        int openedFrame, scroll;
        float statusTimer;

        public static ShopMenu Create(RectTransform parent)
        {
            var bg = PS2UI.Rect(parent, "ShopMenu", new Color(0.05f, 0.035f, 0.025f, 0.92f));
            PS2UI.Anchored(bg, new Vector2(0.5f, 0.5f), new Vector2(0, -10), new Vector2(500, 356));
            var top = PS2UI.Rect(bg, "Top", new Color(0.6f, 0.45f, 0.2f));
            top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1);
            top.sizeDelta = new Vector2(0, 3); top.anchoredPosition = Vector2.zero;

            var m = bg.gameObject.AddComponent<ShopMenu>();
            m.root = bg;
            PS2UI.Text(bg, "MERCATOR", 18, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(14, -10), new Vector2(150, 22), PS2UI.Gold);
            // Tabs on their own line under the title.
            float[] tabX = { 14f, 84f, 186f, 274f, 360f };
            for (int i = 0; i < 5; i++)
                m.tabTexts[i] = PS2UI.Text(bg, TabNames[i], 14, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(tabX[i], -32), new Vector2(110, 20), Color.white);
            m.purseText = PS2UI.Text(bg, "", 14, TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(-14, -12), new Vector2(160, 18), Color.white);

            for (int i = 0; i < Visible; i++)
            {
                var row = PS2UI.Rect(bg, "Row" + i, new Color(0f, 0f, 0f, 0f));
                PS2UI.Anchored(row, new Vector2(0.5f, 1), new Vector2(0, -56 - i * RowH), new Vector2(472, RowH - 2));
                m.rowBgs.Add(row.GetComponent<Image>());
                m.rowTexts.Add(PS2UI.Text(row, "", 13, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(456, 18), Color.white));
            }
            m.descText = PS2UI.Text(bg, "", 11, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(14, -62 - Visible * RowH), new Vector2(472, 44), PS2UI.Parchment);
            m.descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            m.statusText = PS2UI.Text(bg, "", 14, TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(470, 18), PS2UI.Gold);
            m.hintText = PS2UI.Text(bg, "", 10, TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(480, 14), PS2UI.Parchment);
            bg.gameObject.SetActive(false);
            return m;
        }

        public void Open(PlayerProfile p)
        {
            profile = p;
            Selected = 0;
            scroll = 0;
            Tab = 0;
            openedFrame = Time.frameCount;
            statusText.text = "";
            root.gameObject.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen) return;
            root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        /// <summary>EQUIPO rows: both hands, then the eight armour slots. ASPECTO: clothes, colours, hair, beard, shield paint.</summary>
        const int EquipRows = 2 + Character.ArmorSet.SlotCount;
        const int LookRows = 9;
        int Count => Tab == EquipTab ? EquipRows : Tab == LookTab ? LookRows : Category(Tab).Count;

        /// <summary>Items of a shop tab: 0 weapons, 1 armour, 2 shields.</summary>
        static List<ShopItem> Category(int tab)
        {
            var list = new List<ShopItem>();
            foreach (var it in ShopCatalog.Items)
                if ((tab == 0 && it.Weapon != null) || (tab == 1 && it.Armor != null) || (tab == 2 && it.Shield != null)) list.Add(it);
            return list;
        }

        /// <summary>Jumps to an item's tab and selects it (autotest / shortcuts).</summary>
        public bool SelectItem(string id)
        {
            for (int t = 0; t < 3; t++)
            {
                int i = Category(t).FindIndex(it => it.Id == id);
                if (i < 0) continue;
                SetTab(t);
                Select(i);
                return true;
            }
            return false;
        }

        public void SetTab(int t)
        {
            Tab = (t % 5 + 5) % 5;
            Selected = 0;
            scroll = 0;
            Refresh();
        }

        public void Select(int i)
        {
            Selected = Mathf.Clamp(i, 0, Count - 1);
            if (Selected < scroll) scroll = Selected;
            if (Selected >= scroll + Visible) scroll = Selected - Visible + 1;
            Refresh();
        }

        public ShopCatalog.BuyResult BuySelected()
        {
            if (!IsBuyTab) return ShopCatalog.BuyResult.AlreadyOwned;
            var item = Category(Tab)[Selected];
            var r = ShopCatalog.Buy(profile, item);
            switch (r)
            {
                case ShopCatalog.BuyResult.Ok: Status("¡COMPRADO!  " + item.Name + " equipado", PS2UI.Green); break;
                case ShopCatalog.BuyResult.AlreadyOwned: Status("Ya lo tienes (cambia tu equipo en la pestaña EQUIPO)", PS2UI.Yellow); break;
                default: Status("No tienes suficientes denarios", PS2UI.Red); break;
            }
            Debug.Log($"[SHOP] buy {item.Id} -> {r}  denarii={profile.Denarii}");
            Refresh();
            return r;
        }

        /// <summary>ASPECTO tab: 0 shirt, 1 shirt colour, 2 pants, 3 pants colour, 4 shoes, 5 hair, 6 beard, 7 hair colour, 8 shield.</summary>
        public void StepLook(int row, int dir)
        {
            var a = profile.Appearance;
            int W(int v, int n) => (v % n + n) % n;
            switch (row)
            {
                case 0: a.Shirt = W(a.Shirt + dir, Character.CharacterAppearance.Shirts.Length); break;
                case 1: a.ClothColor = W(a.ClothColor + dir, Character.CharacterAppearance.ClothColors.Length); break;
                case 2: a.Pants = W(a.Pants + dir, Character.CharacterAppearance.PantsNames.Length); break;
                case 3: a.PantsColor = W(a.PantsColor + dir, Character.CharacterAppearance.ClothColors.Length); break;
                case 4: a.Shoes = W(a.Shoes + dir, Character.CharacterAppearance.ShoesNames.Length); break;
                case 5: a.HairStyle = W(a.HairStyle + dir, Character.CharacterAppearance.HairStyles.Length); break;
                case 6: a.Beard = W(a.Beard + dir, Character.CharacterAppearance.Beards.Length); break;
                case 7: a.HairColor = W(a.HairColor + dir, Character.CharacterAppearance.HairColors.Length); break;
                default: a.ShieldColor = W(a.ShieldColor + dir, Character.CharacterAppearance.ShieldColorNames.Length); break;
            }
            if (!GameManager.AutoTest) a.Save();
            profile.NotifyChanged();
            Refresh();
        }

        string LookLabel(int i)
        {
            var a = profile.Appearance;
            string N(string[] arr, int v) => arr[Mathf.Clamp(v, 0, arr.Length - 1)];
            switch (i)
            {
                case 0: return "CAMISA:          " + N(Character.CharacterAppearance.Shirts, a.Shirt);
                case 1: return "COLOR CAMISA:    " + N(Character.CharacterAppearance.ClothColorNames, a.ClothColor);
                case 2: return "PANTALON:        " + N(Character.CharacterAppearance.PantsNames, a.Pants);
                case 3: return "COLOR PANTALON:  " + N(Character.CharacterAppearance.ClothColorNames, a.PantsColor);
                case 4: return "CALZADO:         " + N(Character.CharacterAppearance.ShoesNames, a.Shoes);
                case 5: return "PEINADO:         " + N(Character.CharacterAppearance.HairStyles, a.HairStyle);
                case 6: return "BARBA:           " + N(Character.CharacterAppearance.Beards, a.Beard);
                case 7: return "COLOR PELO:      " + N(Character.CharacterAppearance.HairColorNames, a.HairColor);
                default: return "COLOR ESCUDO:    " + N(Character.CharacterAppearance.ShieldColorNames, a.ShieldColor);
            }
        }

        /// <summary>EQUIPO tab: 0 right hand, 1 left hand, 2-9 armour slots (limb pieces cycle through both / left / right); dir = -1 / +1.</summary>
        public void StepEquip(int row, int dir)
        {
            if (row >= 2)
            {
                var slot = (Character.ArmorSlot)(row - 2);
                // Options: nothing, then each owned piece (limb pieces three times: both, left, right).
                var opts = new List<(string id, int side)> { ("", 0) };
                foreach (var id in Loadout.ArmorOptions(profile, slot))
                {
                    if (id == "") continue;
                    if (Character.ArmorLibrary.Sided(slot)) { opts.Add((id, 0)); opts.Add((id, 1)); opts.Add((id, 2)); }
                    else opts.Add((id, 0));
                }
                int cur = opts.FindIndex(o => o.id == profile.Armor[(int)slot] && o.side == profile.ArmorSides[(int)slot]);
                var next = opts[((Mathf.Max(0, cur) + dir) % opts.Count + opts.Count) % opts.Count];
                profile.Armor[(int)slot] = next.id;
                profile.ArmorSides[(int)slot] = next.side;
                profile.NotifyChanged();
                Debug.Log($"[SHOP] armour {slot} = {next.id} side={next.side}  weight {profile.ArmorSet.Weight:0.#} kg");
                Refresh();
                return;
            }
            if (row == 0)
            {
                var opts = Loadout.MainOptions(profile);
                int i = Mathf.Max(0, opts.IndexOf(profile.MainHand));
                Loadout.SetMain(profile, opts[(i + dir + opts.Count) % opts.Count]);
                // Keep the left choice valid for the new main weapon.
                var left = Loadout.LeftOptions(profile);
                if (!left.Contains(Loadout.LeftValue(profile))) Loadout.SetLeft(profile, "");
            }
            else
            {
                var opts = Loadout.LeftOptions(profile);
                int i = Mathf.Max(0, opts.IndexOf(Loadout.LeftValue(profile)));
                Loadout.SetLeft(profile, opts[(i + dir + opts.Count) % opts.Count]);
            }
            profile.NotifyChanged();
            Debug.Log($"[SHOP] loadout main={profile.MainHand} off={profile.OffHand} shield={profile.ShieldId}");
            Refresh();
        }

        string EquipLabel(int i)
        {
            var a = profile.Appearance;
            switch (i)
            {
                case 0: return "MANO DERECHA:   " + Loadout.Label(profile.MainHand);
                case 1: return "MANO IZQUIERDA: " + Loadout.Label(Loadout.LeftValue(profile));
            }
            var slot = (Character.ArmorSlot)(i - 2);
            string id = profile.Armor[(int)slot];
            string side = !string.IsNullOrEmpty(id) && Character.ArmorLibrary.Sided(slot)
                ? "  [" + Character.ArmorLibrary.SideName((Character.ArmorSide)profile.ArmorSides[(int)slot]) + "]" : "";
            return Character.ArmorLibrary.SlotName(slot) + ": " + Loadout.ArmorLabel(id) + side;
        }

        void Status(string s, Color c) { statusText.text = s; statusText.color = c; statusTimer = 2.5f; }

        void Refresh()
        {
            purseText.text = "DENARIOS: " + profile.Denarii;
            for (int t = 0; t < 4; t++)
            {
                tabTexts[t].color = Tab == t ? PS2UI.Gold : new Color(0.6f, 0.55f, 0.5f);
                tabTexts[t].text = Tab == t ? "[" + TabNames[t] + "]" : TabNames[t];
            }
            var cat = IsBuyTab ? Category(Tab) : null;
            for (int r = 0; r < rowTexts.Count; r++)
            {
                int i = r + scroll;
                bool show = i < Count;
                rowTexts[r].gameObject.SetActive(show);
                rowBgs[r].color = show && i == Selected ? new Color(0.45f, 0.3f, 0.12f, 0.9f) : new Color(0f, 0f, 0f, 0f);
                if (!show) continue;
                bool sel = i == Selected;
                if (IsBuyTab)
                {
                    var it = cat[i];
                    bool owned = ShopCatalog.Owns(profile, it);
                    rowTexts[r].text = (sel ? "> " : "   ") + it.Name + "      " + (owned ? "COMPRADO" : it.Price + " den.");
                    rowTexts[r].color = owned ? new Color(0.6f, 0.6f, 0.55f) : (profile.Denarii >= it.Price ? Color.white : new Color(0.85f, 0.55f, 0.45f));
                }
                else
                {
                    string label = Tab == LookTab ? LookLabel(i) : EquipLabel(i);
                    rowTexts[r].text = (sel ? "> " : "   ") + label + (sel ? "   < >" : "");
                    rowTexts[r].color = Color.white;
                }
            }
            if (IsBuyTab)
            {
                descText.text = cat.Count > 0 ? cat[Mathf.Clamp(Selected, 0, cat.Count - 1)].Description : "";
                hintText.text = "W/S elegir   E comprar   TAB siguiente pestaña   ESC salir     (mando: cruceta, A, LB/RB, B)";
            }
            else
            {
                descText.text = Tab == LookTab ? "Ropa, colores, peinado, barba y el color del escudo (gratis)."
                                               : Loadout.Describe(profile) + "\n" + Loadout.WeightLine(profile);
                hintText.text = "W/S elegir   A/D cambiar   TAB siguiente pestaña   ESC salir     (mando: cruceta, LB/RB, B)";
            }
        }

        void Update()
        {
            if (!IsOpen) return;
            if (statusTimer > 0f) { statusTimer -= Time.unscaledDeltaTime; if (statusTimer <= 0f) statusText.text = ""; }
            if (Time.frameCount == openedFrame) return;
            if (GameInput.TabNextPressed) SetTab(Tab + 1);
            else if (GameInput.TabPrevPressed) SetTab(Tab - 1);
            if (GameInput.MenuUpPressed) Select(Selected - 1);
            if (GameInput.MenuDownPressed) Select(Selected + 1);
            if (IsBuyTab)
            {
                if (GameInput.InteractPressed) BuySelected();
            }
            else if (Tab == LookTab)
            {
                if (GameInput.StepLeftPressed) StepLook(Selected, -1);
                if (GameInput.StepRightPressed || GameInput.InteractPressed) StepLook(Selected, 1);
            }
            else
            {
                if (GameInput.StepLeftPressed) StepEquip(Selected, -1);
                if (GameInput.StepRightPressed || GameInput.InteractPressed) StepEquip(Selected, 1);
            }
            if (GameInput.BackPressed) Close();
        }
    }
}
