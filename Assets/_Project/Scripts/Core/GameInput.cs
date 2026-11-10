using UnityEngine;
using UnityEngine.InputSystem;

namespace Gladiator.Core
{
    /// <summary>
    /// Thin polling layer over the Input System (keyboard + mouse + any gamepad).
    /// Xbox layout: X = light, Y = heavy, A = pick up, RB/LB/LT = block, Start = restart.
    /// </summary>
    public static class GameInput
    {
        public static bool UsingGamepad { get; private set; }

        /// <summary>Set by the pause menu: the game sees no input while it is open (and on the frame it closes).</summary>
        public static bool Paused;
        public static int SuppressFrame = -1;
        static bool Muted => Paused || Time.frameCount == SuppressFrame;

        static Keyboard K => Muted ? null : Keyboard.current;
        static Mouse M => Muted ? null : Mouse.current;
        static Gamepad G => Muted ? null : Gamepad.current;

        /// <summary>Call once per frame (PlayerController does) to detect the last used device.</summary>
        public static void UpdateDeviceMode()
        {
            var g = G;
            if (g != null && (g.leftStick.ReadValue().sqrMagnitude > 0.1f || g.rightStick.ReadValue().sqrMagnitude > 0.1f ||
                              g.buttonWest.wasPressedThisFrame || g.buttonNorth.wasPressedThisFrame ||
                              g.buttonSouth.wasPressedThisFrame || g.rightShoulder.isPressed))
                UsingGamepad = true;

            var m = M;
            if (m != null && (m.delta.ReadValue().sqrMagnitude > 4f || m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame))
                UsingGamepad = false;
            var k = K;
            if (k != null && k.anyKey.wasPressedThisFrame) UsingGamepad = false;
        }

        public static Vector2 Move
        {
            get
            {
                Vector2 v = Vector2.zero;
                if (!Muted)
                {
                    if (InputBindings.Held(GameAction.MoveUp)) v.y += 1;
                    if (InputBindings.Held(GameAction.MoveDown)) v.y -= 1;
                    if (InputBindings.Held(GameAction.MoveRight)) v.x += 1;
                    if (InputBindings.Held(GameAction.MoveLeft)) v.x -= 1;
                }
                var g = G;
                if (g != null)
                {
                    Vector2 s = g.leftStick.ReadValue();
                    if (s.sqrMagnitude > 0.04f) v += s;
                }
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        // Rebindable actions (ESC > CONTROLES): see InputBindings.
        static bool Bound(GameAction a) => !Muted && InputBindings.Pressed(a);
        static bool BoundHeld(GameAction a) => !Muted && InputBindings.Held(a);

        public static Vector2 AimStick => G != null ? G.rightStick.ReadValue() : Vector2.zero;
        public static Vector2 MouseScreenPosition => M != null ? M.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        public static bool LightAttackPressed => Bound(GameAction.Light);

        /// <summary>Held heavy button: with the bow, keeps drawing the charged shot.</summary>
        public static bool HeavyAttackHeld => BoundHeld(GameAction.Heavy);

        public static bool HeavyAttackPressed => Bound(GameAction.Heavy);

        public static bool BlockHeld => BoundHeld(GameAction.Block);

        /// <summary>Drop / throw the weapon: G (tap drops, hold throws) / gamepad D-pad down.</summary>
        public static bool DropHeld => BoundHeld(GameAction.Drop);

        /// <summary>Taunt: T / gamepad D-pad up.</summary>
        public static bool TauntPressed => Bound(GameAction.Taunt);
        /// <summary>Activities: hold X (gamepad D-pad left) for the wheel, tap to get up.</summary>
        public static bool ActivityHeld => BoundHeld(GameAction.Activity);

        /// <summary>Toggle walk / run: Caps Lock / gamepad L3.</summary>
        public static bool WalkTogglePressed => Bound(GameAction.Walk);

        /// <summary>Taunt button held: after a moment it opens the taunt wheel.</summary>
        public static bool TauntHeld => BoundHeld(GameAction.Taunt);

        /// <summary>Number row key 1-9 pressed this frame (taunt wheel shortcuts).</summary>
        public static bool DigitPressed(int d)
        {
            var k = K;
            if (k == null || d < 1 || d > 9) return false;
            return k[(Key)((int)Key.Digit1 + d - 1)].wasPressedThisFrame;
        }

        /// <summary>Kick: F / gamepad LB by default.</summary>
        public static bool KickPressed => Bound(GameAction.Kick);

        /// <summary>Shoulder camera: lock on to a rival / let go. Middle mouse button or Tab / gamepad R3 by default.</summary>
        public static bool LockOnPressed => Bound(GameAction.LockOn);
        /// <summary>Same button held: with the classic camera, turns it (with the mouse / right stick).</summary>
        public static bool LockOnHeld => BoundHeld(GameAction.LockOn);

        /// <summary>Mouse movement this frame (pixels).</summary>
        public static Vector2 LookDelta => M != null && !WheelOpen ? M.delta.ReadValue() : Vector2.zero;

        /// <summary>Set while the taunt wheel is open: the camera ignores the mouse, the wheel reads <see cref="WheelMouseDelta"/>.</summary>
        public static bool WheelOpen;
        public static Vector2 WheelMouseDelta => M != null ? M.delta.ReadValue() : Vector2.zero;

        /// <summary>Jump / vault / climb: Space / gamepad A.</summary>
        public static bool JumpPressed => Bound(GameAction.Jump);

        /// <summary>Dodge (roll towards the move direction, back-step without one): Left Ctrl / gamepad B.</summary>
        public static bool DodgePressed => Bound(GameAction.Dodge);

        /// <summary>Crouch while held: C / gamepad right trigger.</summary>
        public static bool CrouchHeld => BoundHeld(GameAction.Crouch);

        public static bool InteractPressed => Bound(GameAction.Interact);

        public static bool RestartPressed =>
            (K != null && K.rKey.wasPressedThisFrame) || (G != null && G.startButton.wasPressedThisFrame);

        // ---- Training minigames
        static int stickFrame = -1;
        static float stickPrevX, stickCurX, stickPrevY, stickCurY;

        static void SampleStick()
        {
            if (stickFrame == Time.frameCount) return;
            stickFrame = Time.frameCount;
            stickPrevX = stickCurX;
            stickPrevY = stickCurY;
            Vector2 st = G != null ? G.leftStick.ReadValue() : Vector2.zero;
            stickCurX = st.x;
            stickCurY = st.y;
        }

        /// <summary>Discrete step left (A / Left arrow / D-pad / stick flick).</summary>
        public static bool StepLeftPressed
        {
            get
            {
                SampleStick();
                return (K != null && (K.aKey.wasPressedThisFrame || K.leftArrowKey.wasPressedThisFrame)) ||
                       (G != null && G.dpad.left.wasPressedThisFrame) || (stickCurX < -0.6f && stickPrevX >= -0.6f);
            }
        }

        /// <summary>Discrete step right (D / Right arrow / D-pad / stick flick).</summary>
        public static bool StepRightPressed
        {
            get
            {
                SampleStick();
                return (K != null && (K.dKey.wasPressedThisFrame || K.rightArrowKey.wasPressedThisFrame)) ||
                       (G != null && G.dpad.right.wasPressedThisFrame) || (stickCurX > 0.6f && stickPrevX <= 0.6f);
            }
        }

        /// <summary>Timing button: light attack, E or Space / X or A.</summary>
        public static bool StrikePressed =>
            LightAttackPressed || InteractPressed || (K != null && K.spaceKey.wasPressedThisFrame);

        /// <summary>Button to mash (weights): E, Space or left click / A or X.</summary>
        public static bool MashPressed =>
            InteractPressed || (K != null && K.spaceKey.wasPressedThisFrame) || (M != null && M.leftButton.wasPressedThisFrame) ||
            (G != null && G.buttonWest.wasPressedThisFrame);

        public static bool ConfirmPressed =>
            InteractPressed || (K != null && (K.enterKey.wasPressedThisFrame || K.numpadEnterKey.wasPressedThisFrame)) ||
            (G != null && G.startButton.wasPressedThisFrame);

        // ---- Menus (shop)
        public static bool MenuUpPressed
        {
            get
            {
                SampleStick();
                return (K != null && (K.wKey.wasPressedThisFrame || K.upArrowKey.wasPressedThisFrame)) ||
                       (G != null && G.dpad.up.wasPressedThisFrame) || (stickCurY > 0.6f && stickPrevY <= 0.6f);
            }
        }

        public static bool MenuDownPressed
        {
            get
            {
                SampleStick();
                return (K != null && (K.sKey.wasPressedThisFrame || K.downArrowKey.wasPressedThisFrame)) ||
                       (G != null && G.dpad.down.wasPressedThisFrame) || (stickCurY < -0.6f && stickPrevY >= -0.6f);
            }
        }

        /// <summary>Close a menu: Esc / Q / gamepad B.</summary>
        public static bool BackPressed =>
            (K != null && (K.escapeKey.wasPressedThisFrame || K.qKey.wasPressedThisFrame)) || (G != null && G.buttonEast.wasPressedThisFrame);

        /// <summary>Secondary interaction (free-for-all at the gate): Q / gamepad Y.</summary>
        public static bool AltInteractPressed =>
            (K != null && K.qKey.wasPressedThisFrame) || (G != null && G.buttonNorth.wasPressedThisFrame);

        // ---- Character creator
        static bool ShiftHeld => K != null && (K.leftShiftKey.isPressed || K.rightShiftKey.isPressed);

        /// <summary>Next tab: Tab / RB.</summary>
        public static bool TabNextPressed =>
            (K != null && K.tabKey.wasPressedThisFrame && !ShiftHeld) || (G != null && G.rightShoulder.wasPressedThisFrame);

        /// <summary>Previous tab: Shift+Tab / LB.</summary>
        public static bool TabPrevPressed =>
            (K != null && K.tabKey.wasPressedThisFrame && ShiftHeld) || (G != null && G.leftShoulder.wasPressedThisFrame);

        /// <summary>Randomise: R / gamepad X.</summary>
        public static bool RandomizePressed =>
            (K != null && K.rKey.wasPressedThisFrame) || (G != null && G.buttonWest.wasPressedThisFrame);

        /// <summary>Cycle the creator's animation preview: V / gamepad Y.</summary>
        public static bool PreviewAnimPressed =>
            (K != null && K.vKey.wasPressedThisFrame) || (G != null && G.buttonNorth.wasPressedThisFrame);

        /// <summary>Turn the preview: Z/C keys, right stick, or dragging with the left mouse button (degrees this frame).</summary>
        public static float PreviewTurn(float dt)
        {
            float deg = 0f;
            if (K != null)
            {
                if (K.zKey.isPressed) deg -= 150f * dt;
                if (K.cKey.isPressed) deg += 150f * dt;
            }
            if (G != null) deg += G.rightStick.ReadValue().x * 180f * dt;
            if (M != null && M.leftButton.isPressed) deg -= M.delta.ReadValue().x * 0.4f;
            return deg;
        }

        /// <summary>Held horizontal input for sliders (A/D, arrows, stick, D-pad): -1..1.</summary>
        public static float SliderAxis
        {
            get
            {
                float x = Move.x;
                if (G != null) x += (G.dpad.right.isPressed ? 1f : 0f) - (G.dpad.left.isPressed ? 1f : 0f);
                return Mathf.Clamp(x, -1f, 1f);
            }
        }

        public static bool QuitPressed => K != null && K.escapeKey.wasPressedThisFrame;
        public static bool TogglePostPressed => K != null && K.f1Key.wasPressedThisFrame;
        public static bool ToggleSnapPressed => K != null && K.f2Key.wasPressedThisFrame;
    }
}
