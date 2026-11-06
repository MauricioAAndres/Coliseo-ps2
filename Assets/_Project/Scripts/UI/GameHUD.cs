using System.Collections.Generic;
using Gladiator.Combat;
using Gladiator.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Gladiator.UI
{
    /// <summary>
    /// Flat, square, no-gradient HUD. The canvas renders into the low-res game RenderTexture,
    /// so text and bars come out chunky and pixelated like a PS2 bitmap-font HUD.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        public static readonly Color PlayerColor = new Color(0.92f, 0.7f, 0.18f);
        public static readonly Color EnemyColor = new Color(0.8f, 0.14f, 0.1f);

        class Bar
        {
            public Fighter F;
            public RectTransform Fill, Trail;
            public Text Label;
            public float TrailValue = 1f;
        }

        readonly List<Bar> bars = new List<Bar>();
        Font font;
        Fighter player;
        PlayerController controller;
        Text weaponText, promptText, msgText, bannerTitle, bannerSub;
        GameObject banner;
        float msgTimer;

        public void Init(Camera cam, Fighter playerFighter, PlayerController pc, IList<Fighter> enemies)
        {
            player = playerFighter;
            controller = pc;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            canvas.pixelPerfect = true;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            var root = (RectTransform)transform;

            hudRoot = root;
            bars.Add(MakeBar(root, player, new Vector2(0, 1), new Vector2(10, -8), 190, PlayerColor, TextAnchor.UpperLeft));
            ResetEnemies(enemies);
            statusText = MakeText(root, "", 13, TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(520, 18), new Color(1f, 0.85f, 0.4f));

            weaponText = MakeText(root, "", 13, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(10, -42), new Vector2(520, 18), Color.white);
            StatsPanel.Create(root, new Vector2(0, 1), new Vector2(10, -64));
            promptText = MakeText(root, "", 14, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(400, 20), new Color(1f, 0.92f, 0.5f));
            msgText = MakeText(root, "", 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(600, 26), new Color(1f, 0.85f, 0.3f));

            hintText = MakeText(root,
                "WASD mover  RATON apuntar  CLIC IZQ ligero  CLIC DER fuerte  F patada  SHIFT bloquear  ESPACIO saltar/escalar  CTRL esquivar  C agacharse  T burla  G soltar/lanzar  E recoger",
                10, TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(1000, 14), new Color(0.85f, 0.8f, 0.7f));

            banner = new GameObject("Banner", typeof(RectTransform));
            banner.transform.SetParent(root, false);
            var brt = (RectTransform)banner.transform;
            brt.anchorMin = new Vector2(0, 0.5f); brt.anchorMax = new Vector2(1, 0.5f);
            brt.sizeDelta = new Vector2(0, 76); brt.anchoredPosition = Vector2.zero;
            banner.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
            bannerTitle = MakeText(brt, "", 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(600, 40), Color.white);
            bannerSub = MakeText(brt, "", 13, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, -24), new Vector2(600, 18), Color.white);
            banner.SetActive(false);
        }

        Bar MakeBar(RectTransform parent, Fighter f, Vector2 corner, Vector2 offset, float width, Color color, TextAnchor align)
        {
            var container = new GameObject("Bar_" + f.DisplayName, typeof(RectTransform));
            container.transform.SetParent(parent, false);
            var crt = (RectTransform)container.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = corner;
            crt.anchoredPosition = offset;
            crt.sizeDelta = new Vector2(width, 30);

            var label = MakeText(crt, f.DisplayName.ToUpper(), 12, align, new Vector2(corner.x, 1), Vector2.zero, new Vector2(width, 14), Color.white);

            var frame = Rect(crt, "Frame", new Color(0.05f, 0.03f, 0.02f));
            frame.anchorMin = new Vector2(0, 0); frame.anchorMax = new Vector2(1, 0);
            frame.pivot = new Vector2(0.5f, 0); frame.sizeDelta = new Vector2(0, 14); frame.anchoredPosition = Vector2.zero;

            var bg = Rect(frame, "Bg", new Color(0.25f, 0.06f, 0.04f));
            Inset(bg, 2);
            var trail = Rect(bg, "Trail", new Color(0.95f, 0.92f, 0.85f));
            var fill = Rect(bg, "Fill", color);
            foreach (var r in new[] { trail, fill })
            {
                r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
                r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
                if (corner.x > 0.5f) r.pivot = new Vector2(1, 0.5f);
            }
            return new Bar { F = f, Fill = fill, Trail = trail, Label = label };
        }

        static RectTransform Rect(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        static void Inset(RectTransform r, float px)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(px, px); r.offsetMax = new Vector2(-px, -px);
        }

        Text MakeText(RectTransform parent, string s, int size, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 sizeDelta, Color c)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;
            var t = go.AddComponent<Text>();
            t.font = font;
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

        RectTransform hudRoot;
        Text statusText;
        readonly List<GameObject> enemyBarObjects = new List<GameObject>();

        /// <summary>Rebuilds the opponents' health bars (waves bring new ones); tighter spacing when there are many.</summary>
        public void ResetEnemies(IList<Fighter> enemies)
        {
            foreach (var go in enemyBarObjects) if (go != null) Destroy(go);
            enemyBarObjects.Clear();
            bars.RemoveAll(b => b.F != player);
            float step = enemies.Count <= 4 ? 34f : Mathf.Max(22f, 300f / enemies.Count);
            for (int i = 0; i < enemies.Count; i++)
            {
                int before = hudRoot.childCount;
                bars.Add(MakeBar(hudRoot, enemies[i], new Vector2(1, 1), new Vector2(-10, -8 - i * step), 190, EnemyColor, TextAnchor.UpperRight));
                for (int c = before; c < hudRoot.childCount; c++) enemyBarObjects.Add(hudRoot.GetChild(c).gameObject);
            }
        }

        /// <summary>A line at the top centre (wave counter).</summary>
        public void SetStatus(string s) { if (statusText != null) statusText.text = s; }

        public void ShowMessage(string s, float duration = 1.5f)
        {
            if (msgText == null) return;
            msgText.text = s;
            msgTimer = duration;
        }

        public void ShowBanner(string title, string sub, Color color)
        {
            banner.SetActive(true);
            bannerTitle.text = title;
            bannerTitle.color = color;
            bannerSub.text = sub;
        }

        Text hintText;
        bool hintShoulder;
        int hintVersion = -1;

        void Update()
        {
            if (hintText != null && (hintShoulder != GameSettings.ShoulderCamera || hintVersion != InputBindings.Version))
            {
                hintShoulder = GameSettings.ShoulderCamera;
                hintVersion = InputBindings.Version;
                string K(GameAction a) => InputBindings.Short(a);
                string move = K(GameAction.MoveUp) + K(GameAction.MoveLeft) + K(GameAction.MoveDown) + K(GameAction.MoveRight);
                if (move.Length > 4) move = K(GameAction.MoveUp) + "/" + K(GameAction.MoveLeft) + "/" + K(GameAction.MoveDown) + "/" + K(GameAction.MoveRight);
                hintText.text = move + " mover  " + (hintShoulder ? "RATON camara  " + K(GameAction.LockOn) + " fijar" : "RATON apuntar") +
                    "  " + K(GameAction.Light) + " ligero  " + K(GameAction.Heavy) + " fuerte  " + K(GameAction.Kick) + " patada  " +
                    K(GameAction.Block) + " bloquear  " + K(GameAction.Jump) + " saltar  " + K(GameAction.Dodge) + " esquivar  " +
                    K(GameAction.Crouch) + " agacharse  " + K(GameAction.Taunt) + " burla  " + K(GameAction.Drop) + " soltar/lanzar  " +
                    K(GameAction.Interact) + " recoger";
            }
            float dt = Time.unscaledDeltaTime;
            foreach (var b in bars)
            {
                if (b.F == null) continue;
                float v = b.F.Health.Normalized;
                b.TrailValue = b.TrailValue > v ? Mathf.MoveTowards(b.TrailValue, v, dt * 0.45f) : v;
                SetFill(b.Fill, v, b);
                SetFill(b.Trail, b.TrailValue, b);
                b.Label.color = b.F.IsAlive ? Color.white : new Color(0.5f, 0.5f, 0.5f);
            }

            if (player != null)
            {
                var w = player.CurrentWeapon;
                string arms = w != null ? w.Stats.Name.ToUpper() : "PUÑOS";
                if (player.OffWeapon != null) arms += " + " + player.OffWeapon.Stats.Name.ToUpper();
                if (player.EquippedShield != null) arms += " + " + player.EquippedShield.Stats.Name.ToUpper() + (player.EquippedShield.OnBack ? " (ESPALDA)" : "");
                if (player.Charging) arms += "   TENSANDO " + Mathf.RoundToInt(player.ChargeLevel * 100f) + "%";
                if (player.MeleeCharging) arms += player.PowerCharged ? "   ¡GOLPE DEMOLEDOR LISTO! (suelta)" : "   CARGANDO " + Mathf.RoundToInt(player.ChargeLevel * 100f) + "%";
                if (player.Armor != null && player.Armor.Count > 0)
                    arms += "   PESO " + player.Armor.Weight.ToString("0.#") + " kg (-" + Mathf.RoundToInt(35f * player.ArmorLoad) + "% vel)";
                weaponText.text = "ARMA: " + arms +
                                  "    HP " + Mathf.CeilToInt(player.Health.Current) + "/" + Mathf.CeilToInt(player.Health.Max);
            }

            var nearby = controller != null ? controller.NearbyWeapon : null;
            promptText.text = nearby != null && player != null && player.IsAlive
                ? (GameInput.UsingGamepad ? "(A) " : "[E] ") + "RECOGER " + nearby.Stats.Name.ToUpper()
                : "";

            if (msgTimer > 0f)
            {
                msgTimer -= dt;
                var c = msgText.color;
                c.a = Mathf.Clamp01(msgTimer * 2f);
                msgText.color = c;
            }
            else msgText.text = "";
        }

        static void SetFill(RectTransform r, float v, Bar b)
        {
            bool fromRight = r.pivot.x > 0.5f;
            if (fromRight) { r.anchorMin = new Vector2(1f - v, 0f); r.anchorMax = Vector2.one; }
            else { r.anchorMin = Vector2.zero; r.anchorMax = new Vector2(v, 1f); }
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }
    }
}
