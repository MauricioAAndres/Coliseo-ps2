using Gladiator.Combat;
using Gladiator.Core;
using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Arena
{
    /// <summary>
    /// Follow camera. Classic: high fixed angle (Colosseum-style) that frames the player and nearest foe.
    /// Shoulder (option): third person behind the right shoulder, turned with the mouse / right stick, with a
    /// souls-style lock-on (middle mouse or Tab / R3) that keeps the rival in view; flick the mouse or stick sideways
    /// to switch target. Minigames (FocusOverride) always use the classic framing.
    /// </summary>
    public class ArenaCamera : MonoBehaviour
    {
        public static ArenaCamera Instance { get; private set; }

        public Fighter Player;
        public float Distance = 11f;
        public float Pitch = 34f;
        public float Yaw = 0f;
        public float SmoothTime = 0.18f;
        [Range(0f, 0.5f)] public float EnemyBias = 0.3f;

        [Header("Shoulder camera")]
        public float ShoulderDistance = 3.4f;
        public float ShoulderOffset = 0.55f;
        public float ShoulderHeight = 1.55f;
        public float LockRange = 18f;

        [Tooltip("When set, the camera frames this point instead of the player (training minigames).")]
        public Vector3? FocusOverride;

        /// <summary>Shoulder camera: the rival locked on (null = free camera).</summary>
        public Fighter LockTarget { get; private set; }
        /// <summary>Shoulder mode is in use right now (option on, a player, no minigame framing).</summary>
        public bool ShoulderActive => GameSettings.ShoulderCamera && Player != null && !FocusOverride.HasValue;
        /// <summary>Shoulder camera heading (degrees): the player moves relative to it.</summary>
        public float OrbitYaw => orbitYaw;

        Vector3 focus, velocity;
        float shake, curDistance = -1f, curPitch = -1f;
        float orbitYaw, orbitPitch = 12f, curBoom = -1f, switchAccum, switchCooldown;
        Vector3 pivot;
        bool orbitInit, cursorLocked;
        Transform marker;

        void Awake() { Instance = this; }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (cursorLocked) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (marker != null) Destroy(marker.gameObject);
        }

        public static void Shake(float amount)
        {
            if (Instance != null) Instance.shake = Mathf.Max(Instance.shake, amount);
        }

        public void SnapToTarget()
        {
            if (Player == null) return;
            focus = DesiredFocus();
            curDistance = Distance; curPitch = Pitch;
            orbitInit = false;
            Place();
        }

        Vector3 DesiredFocus()
        {
            if (FocusOverride.HasValue) return FocusOverride.Value;
            Vector3 p = Player.transform.position;
            Fighter closest = null;
            float best = 100f;
            foreach (var f in Fighter.All)
            {
                if (f == Player || !f.IsAlive) continue;
                float d = Vector3.Distance(f.transform.position, p);
                if (d < best) { best = d; closest = f; }
            }
            if (closest != null)
            {
                Vector3 off = Vector3.ClampMagnitude((closest.transform.position - p) * EnemyBias, 3.5f);
                p += off;
            }
            p.y = 0f;
            return p;
        }

        void LateUpdate()
        {
            if (Player == null) return;
            focus = Vector3.SmoothDamp(focus, DesiredFocus(), ref velocity, SmoothTime);
            UpdateCursor();
            if (ShoulderActive) PlaceShoulder();
            else
            {
                if (LockTarget != null) SetLock(null);
                orbitInit = false;
                Place();
            }
        }

        void Place()
        {
            if (curDistance < 0f) { curDistance = Distance; curPitch = Pitch; }
            float k = 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);
            curDistance = Mathf.Lerp(curDistance, Distance, k);
            curPitch = Mathf.Lerp(curPitch, Pitch, k);
            Quaternion rot = Quaternion.Euler(curPitch, Yaw, 0f);
            Vector3 pos = focus + Vector3.up * 1.2f - rot * Vector3.forward * curDistance;
            transform.SetPositionAndRotation(ApplyShake(pos), rot);
        }

        Vector3 ApplyShake(Vector3 pos)
        {
            if (shake > 0.001f)
            {
                pos += Random.insideUnitSphere * shake * (ShoulderActive ? 0.15f : 0.35f);
                shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 2.5f);
            }
            return pos;
        }

        // ------------------------------------------------------------------ shoulder camera
        void PlaceShoulder()
        {
            float dt = Time.unscaledDeltaTime;
            Vector3 head = Player.transform.position + Vector3.up * ShoulderHeight;
            if (!orbitInit)
            {
                orbitInit = true;
                orbitYaw = Player.transform.eulerAngles.y;
                orbitPitch = 12f;
                pivot = head;
                curBoom = ShoulderDistance;
            }
            pivot = Vector3.Lerp(pivot, head, 1f - Mathf.Exp(-14f * dt));

            if (GameInput.LockOnPressed) SetLock(LockTarget != null ? null : PickTarget(null, 0));
            if (LockTarget != null && (!LockTarget.IsAlive || (LockTarget.transform.position - Player.transform.position).magnitude > LockRange + 4f))
                SetLock(PickTarget(null, 0));

            Vector2 look = GameInput.Paused ? Vector2.zero : GameInput.LookDelta * (0.12f * GameSettings.CameraSensitivity);
            Vector2 stick = GameInput.AimStick;
            if (stick.sqrMagnitude < 0.04f) stick = Vector2.zero;
            look += stick * (150f * GameSettings.CameraSensitivity * dt);
            if (GameSettings.InvertY) look.y = -look.y;

            if (LockTarget != null)
            {
                // Flick sideways to switch target.
                switchCooldown -= dt;
                switchAccum = Mathf.MoveTowards(switchAccum + look.x, 0f, 40f * dt);
                if (switchCooldown <= 0f && Mathf.Abs(switchAccum) > 9f)
                {
                    var next = PickTarget(LockTarget, (int)Mathf.Sign(switchAccum));
                    if (next != null) SetLock(next);
                    switchAccum = 0f;
                    switchCooldown = 0.35f;
                }
                Vector3 to = LockTarget.transform.position + Vector3.up * 1.1f - pivot;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                if (flat.sqrMagnitude > 0.01f)
                {
                    float wantYaw = Quaternion.LookRotation(flat).eulerAngles.y;
                    float wantPitch = Mathf.Clamp(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg + 14f, -10f, 40f);
                    float k = 1f - Mathf.Exp(-9f * dt);
                    orbitYaw = Mathf.LerpAngle(orbitYaw, wantYaw, k);
                    orbitPitch = Mathf.Lerp(orbitPitch, wantPitch, k);
                }
            }
            else
            {
                switchAccum = 0f;
                orbitYaw += look.x;
                orbitPitch = Mathf.Clamp(orbitPitch - look.y, -35f, 65f);
            }

            Quaternion rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            Vector3 shoulder = pivot + rot * Vector3.right * ShoulderOffset;
            Vector3 wantPos = shoulder - rot * Vector3.forward * ShoulderDistance;
            // Keep the camera out of walls: pull it in front of whatever is between the shoulder and it.
            float boom = ShoulderDistance;
            Vector3 dir = wantPos - pivot;
            float len = dir.magnitude;
            if (len > 0.01f)
            {
                var hits = Physics.SphereCastAll(pivot, 0.22f, dir / len, len, ~0, QueryTriggerInteraction.Ignore);
                float nearest = len;
                foreach (var h in hits)
                {
                    if (h.collider.GetComponentInParent<Fighter>() != null) continue;
                    if (h.distance > 0f && h.distance < nearest) nearest = h.distance;
                }
                boom = ShoulderDistance * Mathf.Clamp01(nearest / len);
            }
            curBoom = boom < curBoom ? boom : Mathf.Lerp(curBoom, boom, 1f - Mathf.Exp(-4f * dt));
            float scale = len > 0.01f ? curBoom / ShoulderDistance : 1f;
            Vector3 pos = pivot + (wantPos - pivot) * Mathf.Max(0.15f, scale);
            transform.SetPositionAndRotation(ApplyShake(pos), rot);
            UpdateMarker();
        }

        /// <summary>Best rival to lock: near the centre of the view and close. With a current target and a side
        /// (-1 / 1), the nearest one on that side of it instead.</summary>
        Fighter PickTarget(Fighter current, int side)
        {
            Fighter best = null;
            float bestScore = float.MaxValue;
            Vector3 camFwd = transform.forward; camFwd.y = 0f; camFwd.Normalize();
            Vector3 camRight = transform.right; camRight.y = 0f; camRight.Normalize();
            float curX = 0f;
            if (current != null) curX = Vector3.Dot(current.transform.position - transform.position, camRight);
            foreach (var f in Fighter.All)
            {
                if (f == Player || f == current || !f.IsAlive || f.Harmless || f.TeamId == Player.TeamId) continue;
                Vector3 to = f.transform.position - Player.transform.position;
                float d = to.magnitude;
                if (d > LockRange) continue;
                Vector3 fromCam = f.transform.position - transform.position; fromCam.y = 0f;
                float ang = Vector3.Angle(camFwd, fromCam);
                if (current == null)
                {
                    if (ang > 70f && d > 4f) continue;
                    float score = ang * 0.08f + d * 0.25f;
                    if (score < bestScore) { bestScore = score; best = f; }
                }
                else
                {
                    float x = Vector3.Dot(fromCam, camRight) - curX;
                    if (Mathf.Sign(x) != side || Mathf.Abs(x) < 0.2f || ang > 80f) continue;
                    float score = Mathf.Abs(x) + d * 0.1f;
                    if (score < bestScore) { bestScore = score; best = f; }
                }
            }
            return best;
        }

        /// <summary>Same as the lock-on button (tests).</summary>
        public void ToggleLock() { SetLock(LockTarget != null ? null : PickTarget(null, 0)); }

        void SetLock(Fighter f)
        {
            LockTarget = f;
            if (marker != null) marker.gameObject.SetActive(f != null);
        }

        /// <summary>Small red diamond floating over the locked rival's head.</summary>
        void UpdateMarker()
        {
            if (LockTarget == null) { if (marker != null) marker.gameObject.SetActive(false); return; }
            if (marker == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "LockOnMarker";
                Destroy(go.GetComponent<Collider>());
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = PS2Materials.Flat(new Color(1f, 0.25f, 0.15f), 0f);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                marker = go.transform;
                marker.localScale = Vector3.one * 0.13f;
            }
            marker.gameObject.SetActive(true);
            marker.position = LockTarget.transform.position + Vector3.up * 2.2f; // above the head
            marker.rotation = Quaternion.LookRotation(marker.position - transform.position) * Quaternion.Euler(0f, 0f, 45f);
        }

        /// <summary>The shoulder camera takes the mouse: cursor hidden and locked while playing, free in menus.</summary>
        void UpdateCursor()
        {
            bool want = ShoulderActive && !GameInput.Paused && Application.isFocused && !MenuOpen();
            if (want == cursorLocked) return;
            cursorLocked = want;
            Cursor.lockState = want ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !want;
        }

        float menuCheck;
        bool menuOpen;
        bool MenuOpen()
        {
            if (Gladiator.UI.PauseMenu.Instance != null && Gladiator.UI.PauseMenu.Instance.IsOpen) return true;
            if (Gladiator.UI.ArenaGateMenu.Instance != null && Gladiator.UI.ArenaGateMenu.Instance.IsOpen) return true;
            if (Gladiator.UI.ChoiceMenu.Instance != null && Gladiator.UI.ChoiceMenu.Instance.IsOpen) return true;
            if (Time.unscaledTime >= menuCheck)
            {
                menuCheck = Time.unscaledTime + 0.25f;
                var shop = FindAnyObjectByType<Gladiator.UI.ShopMenu>();
                menuOpen = shop != null && shop.IsOpen;
            }
            return menuOpen;
        }
    }
}
