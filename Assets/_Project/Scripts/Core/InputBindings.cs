using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Gladiator.Core
{
    /// <summary>Game actions the player can rebind (ESC > CONTROLES).</summary>
    public enum GameAction { MoveUp, MoveDown, MoveLeft, MoveRight, Light, Heavy, Kick, Block, Jump, Dodge, Crouch, Taunt, Drop, Interact, LockOn }

    /// <summary>
    /// Key / button assignments, two columns per action: keyboard + mouse, and gamepad. Saved in PlayerPrefs.
    /// A binding is "kb:&lt;key&gt;", "mouse:&lt;button&gt;" or "pad:&lt;control path&gt;" (Input System control names,
    /// e.g. kb:space, mouse:leftButton, pad:buttonSouth, pad:dpad/up). Assigning a key that another action already uses
    /// swaps them, so nothing is left without a key by accident.
    /// </summary>
    public static class InputBindings
    {
        const string Prefix = "Gladiator.Bind.";

        struct Def
        {
            public string Label; public string[] Kb, Pad; public bool PadFixed;
            public Def(string label, string[] kb, string[] pad, bool padFixed = false) { Label = label; Kb = kb; Pad = pad; PadFixed = padFixed; }
        }

        static readonly Def[] Defs =
        {
            new Def("AVANZAR", new[] { "kb:w", "kb:upArrow" }, new string[0], true),
            new Def("RETROCEDER", new[] { "kb:s", "kb:downArrow" }, new string[0], true),
            new Def("IZQUIERDA", new[] { "kb:a", "kb:leftArrow" }, new string[0], true),
            new Def("DERECHA", new[] { "kb:d", "kb:rightArrow" }, new string[0], true),
            new Def("ATAQUE LIGERO", new[] { "mouse:leftButton", "kb:j" }, new[] { "pad:buttonWest" }),
            new Def("ATAQUE FUERTE", new[] { "mouse:rightButton", "kb:k" }, new[] { "pad:buttonNorth" }),
            new Def("PATADA", new[] { "kb:f" }, new[] { "pad:leftShoulder" }),
            new Def("BLOQUEAR", new[] { "kb:leftShift" }, new[] { "pad:rightShoulder", "pad:leftTrigger" }),
            new Def("SALTAR / ESCALAR", new[] { "kb:space" }, new[] { "pad:buttonSouth" }),
            new Def("ESQUIVAR", new[] { "kb:leftCtrl" }, new[] { "pad:buttonEast" }),
            new Def("AGACHARSE", new[] { "kb:c" }, new[] { "pad:rightTrigger" }),
            new Def("BURLA", new[] { "kb:t" }, new[] { "pad:dpad/up" }),
            new Def("SOLTAR / LANZAR ARMA", new[] { "kb:g" }, new[] { "pad:dpad/down" }),
            new Def("RECOGER / USAR", new[] { "kb:e" }, new[] { "pad:buttonSouth" }),
            new Def("FIJAR / GIRAR CAMARA", new[] { "mouse:middleButton", "kb:tab" }, new[] { "pad:rightStickPress" }),
        };

        public static int Count => Defs.Length;
        /// <summary>Goes up on every change (the HUD rebuilds its key hints).</summary>
        public static int Version { get; private set; }

        static List<string>[] kb, pad;

        static void Ensure() { if (kb == null) Load(); }

        public static void Load()
        {
            kb = new List<string>[Defs.Length];
            pad = new List<string>[Defs.Length];
            for (int i = 0; i < Defs.Length; i++)
            {
                kb[i] = Read(Prefix + (GameAction)i + ".kb", Defs[i].Kb);
                pad[i] = Read(Prefix + (GameAction)i + ".pad", Defs[i].Pad);
            }
            Version++;
        }

        static List<string> Read(string key, string[] def)
        {
            if (!PlayerPrefs.HasKey(key)) return new List<string>(def);
            string v = PlayerPrefs.GetString(key, "");
            return new List<string>(v.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));
        }

        public static void Save()
        {
            Ensure();
            for (int i = 0; i < Defs.Length; i++)
            {
                PlayerPrefs.SetString(Prefix + (GameAction)i + ".kb", string.Join(";", kb[i]));
                PlayerPrefs.SetString(Prefix + (GameAction)i + ".pad", string.Join(";", pad[i]));
            }
            PlayerPrefs.Save();
        }

        public static void ResetDefaults(bool save = true)
        {
            kb = new List<string>[Defs.Length];
            pad = new List<string>[Defs.Length];
            for (int i = 0; i < Defs.Length; i++) { kb[i] = new List<string>(Defs[i].Kb); pad[i] = new List<string>(Defs[i].Pad); }
            Version++;
            if (save) Save();
        }

        public static string Label(GameAction a) => Defs[(int)a].Label;
        public static bool PadFixed(GameAction a) => Defs[(int)a].PadFixed;
        public static IReadOnlyList<string> Get(GameAction a, bool gamepad) { Ensure(); return gamepad ? pad[(int)a] : kb[(int)a]; }

        /// <summary>
        /// Assigns a key / button (its column comes from the device). Another action using it gets this action's old
        /// key instead. Returns false when the action cannot take it (movement on the gamepad is the left stick).
        /// </summary>
        public static bool Set(GameAction a, string path, bool save = true)
        {
            Ensure();
            bool gp = path.StartsWith("pad:");
            if (gp && PadFixed(a)) return false;
            var col = gp ? pad : kb;
            int ai = (int)a;
            var old = new List<string>(col[ai]);
            for (int i = 0; i < col.Length; i++)
            {
                if (i == ai || !col[i].Contains(path)) continue;
                // The other action gives it up and takes this action's old key (if it does not have it already).
                col[i].Remove(path);
                foreach (var o in old) if (!col[i].Contains(o)) { col[i].Add(o); break; }
            }
            col[ai] = new List<string> { path };
            Version++;
            if (save) Save();
            return true;
        }

        // ------------------------------------------------------------------ reading
        struct Cached { public InputDevice Device; public ButtonControl Control; }
        static readonly Dictionary<string, Cached> cache = new Dictionary<string, Cached>();

        static ButtonControl Resolve(string path)
        {
            InputDevice dev;
            int colon = path.IndexOf(':');
            if (colon < 0) return null;
            string kind = path.Substring(0, colon);
            if (kind == "kb") dev = Keyboard.current;
            else if (kind == "mouse") dev = Mouse.current;
            else if (kind == "pad") dev = Gamepad.current;
            else return null;
            if (dev == null) return null;
            if (cache.TryGetValue(path, out var c) && c.Device == dev) return c.Control;
            var ctl = dev.TryGetChildControl<ButtonControl>(path.Substring(colon + 1));
            cache[path] = new Cached { Device = dev, Control = ctl };
            return ctl;
        }

        public static bool Held(GameAction a)
        {
            Ensure();
            foreach (var p in kb[(int)a]) { var c = Resolve(p); if (c != null && c.isPressed) return true; }
            foreach (var p in pad[(int)a]) { var c = Resolve(p); if (c != null && c.isPressed) return true; }
            return false;
        }

        public static bool Pressed(GameAction a)
        {
            Ensure();
            foreach (var p in kb[(int)a]) { var c = Resolve(p); if (c != null && c.wasPressedThisFrame) return true; }
            foreach (var p in pad[(int)a]) { var c = Resolve(p); if (c != null && c.wasPressedThisFrame) return true; }
            return false;
        }

        /// <summary>
        /// The key / button pressed this frame on any device, as a binding (null if none). Esc and the gamepad's
        /// Select are left out: they cancel. Stick directions are not buttons here (only the stick clicks).
        /// </summary>
        public static string CaptureAny()
        {
            var k = Keyboard.current;
            if (k != null)
                foreach (var key in k.allKeys)
                    if (key != null && key.wasPressedThisFrame && key.keyCode != Key.Escape) return "kb:" + key.name;
            var m = Mouse.current;
            if (m != null)
            {
                if (m.leftButton.wasPressedThisFrame) return "mouse:leftButton";
                if (m.rightButton.wasPressedThisFrame) return "mouse:rightButton";
                if (m.middleButton.wasPressedThisFrame) return "mouse:middleButton";
                if (m.forwardButton.wasPressedThisFrame) return "mouse:forwardButton";
                if (m.backButton.wasPressedThisFrame) return "mouse:backButton";
            }
            var g = Gamepad.current;
            if (g != null)
            {
                foreach (var ctl in g.allControls)
                {
                    if (!(ctl is ButtonControl b) || !b.wasPressedThisFrame || ctl == g.selectButton) continue;
                    string rel = ctl.path.Substring(g.path.Length + 1);
                    if ((rel.StartsWith("leftStick/") || rel.StartsWith("rightStick/")) || rel.Contains("/") && !rel.StartsWith("dpad/")) continue;
                    if (rel == "dpad") continue;
                    return "pad:" + rel;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ names
        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "mouse:leftButton", "CLIC IZQ" }, { "mouse:rightButton", "CLIC DER" }, { "mouse:middleButton", "RUEDA" },
            { "mouse:forwardButton", "RATON 5" }, { "mouse:backButton", "RATON 4" },
            { "kb:space", "ESPACIO" }, { "kb:leftShift", "SHIFT" }, { "kb:rightShift", "SHIFT DER" }, { "kb:leftCtrl", "CTRL" },
            { "kb:rightCtrl", "CTRL DER" }, { "kb:leftAlt", "ALT" }, { "kb:rightAlt", "ALT GR" }, { "kb:tab", "TAB" },
            { "kb:enter", "ENTER" }, { "kb:backspace", "BORRAR" }, { "kb:capsLock", "BLOQ MAYUS" },
            { "kb:upArrow", "FLECHA ARRIBA" }, { "kb:downArrow", "FLECHA ABAJO" }, { "kb:leftArrow", "FLECHA IZQ" }, { "kb:rightArrow", "FLECHA DER" },
            { "pad:buttonSouth", "A" }, { "pad:buttonEast", "B" }, { "pad:buttonWest", "X" }, { "pad:buttonNorth", "Y" },
            { "pad:leftShoulder", "LB" }, { "pad:rightShoulder", "RB" }, { "pad:leftTrigger", "LT" }, { "pad:rightTrigger", "RT" },
            { "pad:leftStickPress", "L3" }, { "pad:rightStickPress", "R3" }, { "pad:start", "START" },
            { "pad:dpad/up", "CRUZ ARRIBA" }, { "pad:dpad/down", "CRUZ ABAJO" }, { "pad:dpad/left", "CRUZ IZQ" }, { "pad:dpad/right", "CRUZ DER" },
        };

        public static string Name(string path)
        {
            if (Names.TryGetValue(path, out var n)) return n;
            int colon = path.IndexOf(':');
            string rel = colon >= 0 ? path.Substring(colon + 1) : path;
            if (path.StartsWith("kb:"))
            {
                var k = Keyboard.current;
                var ctl = k != null ? k.TryGetChildControl<KeyControl>(rel) : null;
                if (ctl != null && !string.IsNullOrEmpty(ctl.displayName)) return ctl.displayName.ToUpperInvariant();
            }
            return rel.ToUpperInvariant();
        }

        /// <summary>"CLIC IZQ / J" for a column; "-" when empty.</summary>
        public static string Describe(GameAction a, bool gamepad)
        {
            if (gamepad && PadFixed(a)) return "STICK IZQ";
            var list = Get(a, gamepad);
            if (list.Count == 0) return "-";
            var parts = new List<string>();
            foreach (var p in list) parts.Add(Name(p));
            return string.Join(" / ", parts);
        }

        /// <summary>First keyboard / mouse key of an action, for the on-screen hints.</summary>
        public static string Short(GameAction a)
        {
            var list = Get(a, false);
            return list.Count > 0 ? Name(list[0]) : "-";
        }
    }
}
