using System.Collections;
using System.IO;
using Gladiator.Combat;
using Gladiator.Core;
using UnityEngine;

namespace Gladiator.Arena
{
    /// <summary>
    /// Smoke test, only active when the player is launched with "-autotest".
    /// Verifies pickup, AI combat, disarm + re-arm and match resolution; writes [AUTOTEST] lines to
    /// the player log and screenshots next to the executable, then quits.
    /// </summary>
    public class AutoTestRunner : MonoBehaviour
    {
        ArenaBootstrap arena;
        PlayerController pc;
        int hits, blocks, guardBreaks, disarms, weaponChanges, evaded, armorHits;
        float armorStopped;
        readonly System.Collections.Generic.SortedDictionary<string, int> moves = new System.Collections.Generic.SortedDictionary<string, int>();
        string outDir;

        public void Init(ArenaBootstrap a, PlayerController controller)
        {
            arena = a;
            pc = controller;
        }

        void OnEnable()
        {
            Fighter.AnyHitResolved += OnHit;
            Fighter.AnyWeaponChanged += OnWeapon;
            Fighter.AnyAttackEvaded += OnEvaded;
            Fighter.AnyMove += OnMove;
        }

        void OnDisable()
        {
            Fighter.AnyHitResolved -= OnHit;
            Fighter.AnyWeaponChanged -= OnWeapon;
            Fighter.AnyAttackEvaded -= OnEvaded;
            Fighter.AnyMove -= OnMove;
        }

        void OnEvaded(Fighter attacker, Fighter target)
        {
            evaded++;
            Log($"EVADED {target.DisplayName} {(target.Crouching ? "ducked" : "dodged")} {attacker.DisplayName} {attacker.CurrentAttackType}");
        }

        void OnMove(Fighter f, string what)
        {
            string k = (f == arena.Player ? "player:" : "ai:") + what;
            moves[k] = moves.TryGetValue(k, out int c) ? c + 1 : 1;
            if (f == arena.Player) Log($"MOVE {what} at y={f.transform.position.y:0.00} fall={f.LastFallHeight:0.00}");
        }

        static void Teleport(Fighter f, Vector3 pos, Vector3 face)
        {
            f.Controller.enabled = false;
            f.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(face));
            f.Controller.enabled = true;
            f.SetFacing(face);
        }

        IEnumerator WaitIdle(Fighter f, float max)
        {
            for (float t = 0f; t < max && f.State != FighterState.Idle; t += Time.unscaledDeltaTime) yield return null;
        }

        /// <summary>Crouch, roll, back-step, jump, vault onto a block, walk off, climb a platform, drop off it.</summary>
        IEnumerator MovementTest(Fighter p, string tag)
        {
            var enemyAI = arena.Enemies[0].GetComponent<EnemyAI>();
            if (enemyAI != null) enemyAI.enabled = false;
            pc.enabled = false;
            Vector3 home = p.transform.position;

            p.SetCrouch(true);
            yield return Wait(0.7f);
            Log($"CROUCH crouching={p.Crouching} height={p.Controller.height:0.00} playing={Poser(p)}");
            yield return Shot(tag + "m1_crouch");
            p.SetCrouch(false);
            yield return Wait(0.4f);

            bool ok = p.TryDodge(p.transform.right);
            yield return Wait(0.24f);
            Log($"DODGE ok={ok} state={p.State} invulnerable={p.Invulnerable} playing={Poser(p)}");
            yield return Shot(tag + "m2_roll");
            yield return WaitIdle(p, 2f);
            yield return Wait(0.4f);
            Vector3 b0 = p.transform.position;
            ok = p.TryDodge(Vector3.zero);
            yield return WaitIdle(p, 2f);
            Log($"BACKSTEP ok={ok} moved={Vector3.Distance(b0, p.transform.position):0.00}m");
            yield return Wait(0.3f);

            float y0 = p.transform.position.y, apex = y0;
            ok = p.TryJump(Vector3.zero);
            bool shot = false;
            for (float t = 0f; t < 2f && (t < 0.1f || p.State == FighterState.Airborne); t += Time.unscaledDeltaTime)
            {
                apex = Mathf.Max(apex, p.transform.position.y);
                if (!shot && t > 0.25f) { shot = true; yield return Shot(tag + "m3_jump"); }
                yield return null;
            }
            Log($"JUMP ok={ok} apex={apex - y0:0.00}m state={p.State}");
            yield return WaitIdle(p, 1f);

            ok = p.TryAttack(AttackType.Kick);
            for (float t = 0f; t < 1f && p.State != FighterState.Active; t += Time.unscaledDeltaTime) yield return null;
            Log($"KICK ok={ok} state={p.State} type={p.CurrentAttackType} playing={Poser(p)}");
            yield return Shot(tag + "m0_kick");
            yield return WaitIdle(p, 1.5f);

            Transform block = null, plat = null;
            foreach (var o in arena.Obstacles) { if (o.name == "BlockSW") block = o; if (o.name == "PlatformSE") plat = o; }

            // Vault onto the low block, then walk off its far side.
            Vector3 fwd = block.forward;
            Teleport(p, block.position - fwd * (0.7f + 0.75f) + Vector3.up * 0.05f, fwd);
            yield return Wait(0.5f);
            ok = p.TryJump(fwd);
            Log($"VAULT ok={ok} state={p.State} kind={p.CurrentTraverse}");
            yield return Wait(0.45f);
            yield return Shot(tag + "m4_vault");
            yield return WaitIdle(p, 3f);
            Log($"VAULT done y={p.transform.position.y:0.00} (block 0.90)");
            yield return Wait(0.3f);
            for (float t = 0f; t < 1.4f; t += Time.unscaledDeltaTime) { p.Move(fwd * 0.6f); yield return null; }
            yield return WaitIdle(p, 2f);
            Log($"WALKOFF y={p.transform.position.y:0.00} fall={p.LastFallHeight:0.00}");

            // Climb the platform, then drop off it (hard landing).
            fwd = plat.forward;
            Teleport(p, plat.position - fwd * (1.6f + 0.8f) + Vector3.up * 0.05f, fwd);
            yield return Wait(0.5f);
            ok = p.TryJump(fwd);
            Log($"CLIMB ok={ok} state={p.State} kind={p.CurrentTraverse}");
            yield return Wait(0.5f);
            yield return Shot(tag + "m5_hang");
            yield return Wait(0.45f);
            yield return Shot(tag + "m6_pull");
            yield return WaitIdle(p, 4f);
            Log($"CLIMB done y={p.transform.position.y:0.00} (platform 2.30)");
            yield return Shot(tag + "m7_top");
            shot = false;
            for (float t = 0f; t < 3f; t += Time.unscaledDeltaTime)
            {
                if (p.State == FighterState.Idle || p.State == FighterState.Airborne) p.Move(fwd);
                if (!shot && p.State == FighterState.Airborne && p.AirTime > 0.2f) { shot = true; yield return Shot(tag + "m8_fall"); }
                if (p.State == FighterState.Landing) { yield return Shot(tag + "m9_land"); break; }
                yield return null;
            }
            yield return WaitIdle(p, 2f);
            Log($"DROP y={p.transform.position.y:0.00} fall={p.LastFallHeight:0.00}");

            // Kick from 2.3 m, slightly off to the side: the step-in and the turn must land it.
            Fighter e = arena.Enemies[0];
            Teleport(e, home + new Vector3(0.5f, 0f, 2.3f), Vector3.back);
            Teleport(p, home, Vector3.forward);
            yield return Wait(0.4f);
            float hp0 = e.Health.Current;
            Vector3 k0 = p.transform.position;
            p.TryAttack(AttackType.Kick);
            yield return WaitIdle(p, 1.5f);
            Log($"KICK LUNGE stepped={Vector3.Distance(k0, p.transform.position):0.00}m hit={e.Health.Current < hp0} enemyHP {hp0:0}->{e.Health.Current:0}");
            yield return WaitIdle(e, 1.5f);

            // Kick into a raised guard: the enemy must be stunned ~2 s, and a hit must end it early.
            Teleport(e, home + Vector3.forward * 1.3f, Vector3.back);
            Teleport(p, home, Vector3.forward);
            yield return Wait(0.3f);
            e.SetBlock(true);
            yield return Wait(0.3f);
            p.TryAttack(AttackType.Kick);
            for (float t = 0f; t < 1.5f && !e.IsStunned; t += Time.unscaledDeltaTime) yield return null;
            float st0 = Time.time;
            Log($"STUN kicked a guard: stunned={e.IsStunned} enemyState={e.State}");
            yield return Wait(0.8f);
            yield return Shot(tag + "m10_stunned");
            e.SetBlock(false);
            for (float t = 0f; t < 3f && e.IsStunned; t += Time.unscaledDeltaTime) yield return null;
            Log($"STUN wore off after {Time.time - st0:0.00}s (expected ~{e.KickStunDuration:0.0})");
            yield return Wait(0.4f);
            e.SetBlock(true);
            yield return Wait(0.3f);
            p.TryAttack(AttackType.Kick);
            for (float t = 0f; t < 1.5f && !e.IsStunned; t += Time.unscaledDeltaTime) yield return null;
            yield return WaitIdle(p, 1.5f);
            e.SetBlock(false);
            p.TryAttack(AttackType.Light);
            for (float t = 0f; t < 1.5f && e.IsStunned; t += Time.unscaledDeltaTime) yield return null;
            Log($"STUN broken by a hit: stunned={e.IsStunned} enemyState={e.State} after {Time.time - st0:0.00}s since first");
            yield return Wait(0.6f);

            Teleport(e, home + Vector3.forward * 10f, Vector3.back);
            Teleport(p, home, Vector3.forward);
            pc.enabled = true;
            if (enemyAI != null) enemyAI.enabled = true;
            yield return Wait(0.3f);
        }

        static void LogAudio()
        {
            var am = Gladiator.Audio.AudioManager.Instance;
            Log($"AUDIO listener={(am != null)} listeners={FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length} played: " +
                string.Join(", ", System.Linq.Enumerable.Select(Gladiator.Audio.AudioManager.Counts, kv => kv.Key + "=" + kv.Value)));
            if (am != null && !levelsLogged) { levelsLogged = true; Log("AUDIO levels " + am.LevelReport()); }
        }
        static bool levelsLogged;

        /// <summary>
        /// Armour: walking 1.5 s and rolling with no armour, light and heavy sets (distance covered), and the share of
        /// damage stopped per zone on an armoured target. Shots of the armoured gladiator up close.
        /// </summary>
        IEnumerator ArmorTest(Fighter p, Fighter e, string tag)
        {
            var ai = e.GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = false;
            pc.enabled = false;
            var gm = Gladiator.Core.GameManager.Instance;
            var look = gm != null ? gm.Profile.Appearance : p.GetComponentInChildren<Gladiator.Character.HumanoidRig>().Appearance;
            Vector3 home = p.transform.position;
            var light = new System.Collections.Generic.List<string>();
            var heavy = new System.Collections.Generic.List<string>();
            foreach (var a in Gladiator.Character.ArmorLibrary.All) { if (a.Heavy) heavy.Add(a.Id); else if (a.Id != "galea_cuero") light.Add(a.Id); }
            var sets = new (string name, Gladiator.Character.ArmorSet set)[]
            {
                ("sin", Gladiator.Character.ArmorSet.Empty),
                ("ligera", Gladiator.Character.ArmorSet.FromIds(light)),
                ("pesada", Gladiator.Character.ArmorSet.FromIds(heavy)),
            };
            var cam = ArenaCamera.Instance;
            float camD = cam != null ? cam.Distance : 0f, camP = cam != null ? cam.Pitch : 0f, camY = cam != null ? cam.Yaw : 0f;
            foreach (var (name, set) in sets)
            {
                CharacterFactory.RebuildBody(p, look, set);
                Teleport(e, home + Vector3.forward * 20f, Vector3.back);
                Teleport(p, home + Vector3.left * 3f, Vector3.right);
                yield return Wait(0.4f);
                Vector3 a0 = p.transform.position;
                for (float t = 0f; t < 1.5f; t += Time.unscaledDeltaTime) { p.Move(Vector3.right); yield return null; }
                float walk = Vector3.Distance(a0, p.transform.position);
                yield return Wait(0.4f);
                Vector3 r0 = p.transform.position;
                p.TryDodge(Vector3.left);
                yield return WaitIdle(p, 2f);
                float roll = Vector3.Distance(r0, p.transform.position);
                Log($"ARMOR {name}: weight={set.Weight:0.#}kg load={p.ArmorLoad:0.00} walk1.5s={walk:0.00}m roll={roll:0.00}m");
                if (name != "sin" && cam != null)
                {
                    cam.Distance = 3.6f; cam.Pitch = 12f; cam.Yaw = 200f;
                    yield return Wait(0.8f);
                    yield return Shot($"{tag}a_{name}");
                    cam.Distance = camD; cam.Pitch = camP; cam.Yaw = camY;
                }
            }

            // Damage per zone on a target in heavy armour (player keeps the heavy set, the enemy gets it too).
            CharacterFactory.RebuildBody(e, e.GetComponentInChildren<Gladiator.Character.HumanoidRig>().Appearance, Gladiator.Character.ArmorSet.FromIds(heavy));
            float enemyMax = e.Health.Max;
            e.Health.SetMax(100000f, true);
            Teleport(p, home, Vector3.forward);
            Teleport(e, home + Vector3.forward * 1.9f, Vector3.back);
            int before = armorHits;
            for (int k = 0; k < 8; k++)
            {
                p.TryAttack(k % 2 == 0 ? AttackType.Light : AttackType.Heavy);
                for (float t = 0f; t < 2f && p.State != FighterState.Idle; t += Time.unscaledDeltaTime) yield return null;
                yield return WaitIdle(e, 1f);
                Teleport(e, home + Vector3.forward * 1.9f, Vector3.back);
            }
            Log($"ARMOR zones: {armorHits - before} of 8 blows hit armour, avg stopped {(armorHits > 0 ? armorStopped / armorHits : 0f) * 100f:0}%");

            // Back to the profile's armour; the enemy keeps a day-1 set.
            CharacterFactory.RebuildBody(p, look, gm != null ? gm.Profile.ArmorSet : Gladiator.Character.ArmorSet.Empty);
            CharacterFactory.RebuildBody(e, e.GetComponentInChildren<Gladiator.Character.HumanoidRig>().Appearance,
                Gladiator.Character.ArmorSet.FromIds(new[] { "galea_bronce" }));
            e.Health.SetMax(enemyMax, true);
            Teleport(e, home + Vector3.forward * 10f, Vector3.back);
            Teleport(p, home, Vector3.forward);
            pc.enabled = true;
            if (ai != null) ai.enabled = true;
            yield return Wait(0.3f);
        }

        /// <summary>
        /// Disarming: light-light-heavy combo, a heavy held 2 s, a power strike into a raised guard (broken + dazed),
        /// a plain heavy (must NOT disarm); then G: drop at the feet and throw at a rival.
        /// </summary>
        IEnumerator DisarmTest(Fighter p, Fighter e, string tag)
        {
            var ai = e.GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = false;
            pc.enabled = false;
            Vector3 home = p.transform.position;
            float enemyMax = e.Health.Max;
            e.Health.SetMax(100000f, true);
            Arm(p, WeaponLibrary.Gladius, null, null);
            void Rearm() { if (e.CurrentWeapon == null) e.Equip(WeaponFactory.Create(WeaponLibrary.Spatha)); }
            void Front(float d) { Teleport(e, home + Vector3.forward * d, Vector3.back); Teleport(p, home, Vector3.forward); }

            // 1) combo
            Rearm(); Front(1.9f);
            yield return Wait(0.4f);
            foreach (var t in new[] { AttackType.Light, AttackType.Light, AttackType.Heavy })
            {
                Front(1.9f);
                p.TryAttack(t);
                for (float k = 0f; k < 2f && p.State != FighterState.Idle; k += Time.unscaledDeltaTime) yield return null;
            }
            Log($"DISARM combo: enemyArmed={e.CurrentWeapon != null} (expected False)");

            // 2) charged heavy
            Rearm(); Front(1.9f);
            yield return Wait(0.5f);
            p.SetCharge(true);
            p.TryAttack(AttackType.Heavy);
            yield return Wait(2.3f);
            bool ready = p.PowerCharged;
            yield return Shot(tag + "x_carga");
            p.SetCharge(false);
            for (float k = 0f; k < 2f && p.State != FighterState.Idle; k += Time.unscaledDeltaTime) yield return null;
            Log($"DISARM charged: ready={ready} enemyArmed={e.CurrentWeapon != null} (expected False)");

            // 3) power strike into a guard
            Rearm(); Front(1.9f);
            e.SetBlock(true);
            yield return Wait(0.4f);
            p.SetCharge(true);
            p.TryAttack(AttackType.Heavy);
            yield return Wait(2.2f);
            p.SetCharge(false);
            bool stunned = false;
            for (float k = 0f; k < 1.5f; k += Time.unscaledDeltaTime) { if (e.IsStunned) stunned = true; yield return null; }
            e.SetBlock(false);
            Log($"DISARM guard: stunned={stunned} (expected True) enemyArmed={e.CurrentWeapon != null}");
            for (float k = 0f; k < 2.5f && e.State != FighterState.Idle; k += Time.unscaledDeltaTime) yield return null;

            // 4) a plain heavy does not disarm any more
            Rearm(); Front(1.9f);
            yield return Wait(1.6f);
            p.TryAttack(AttackType.Heavy);
            for (float k = 0f; k < 2f && p.State != FighterState.Idle; k += Time.unscaledDeltaTime) yield return null;
            Log($"DISARM plain heavy: enemyArmed={e.CurrentWeapon != null} (expected True)");

            // 5) G: drop, then throw at a rival 6 m away
            bool dropped = p.DropAtFeet();
            yield return Wait(0.6f);
            Log($"DROP ok={dropped} playerArmed={p.CurrentWeapon != null} groundWeapons={Weapon.GroundWeapons.Count}");
            Arm(p, WeaponLibrary.Axe, null, null);
            Front(6f);
            yield return Wait(0.4f);
            float hp0 = e.Health.Current;
            bool threw = p.TryThrow();
            yield return Wait(0.35f);
            yield return Shot(tag + "x_lanzar");
            yield return Wait(1.2f);
            Log($"THROW ok={threw} playerArmed={p.CurrentWeapon != null} enemyHit={e.Health.Current < hp0} dmg={hp0 - e.Health.Current:0.0}");

            var gm = Gladiator.Core.GameManager.Instance;
            var prof = gm != null ? gm.Profile : null;
            Arm(p, prof != null ? (WeaponLibrary.ByName(prof.MainHand) ?? WeaponLibrary.Gladius) : WeaponLibrary.Gladius,
                prof != null ? WeaponLibrary.ByName(prof.OffHand) : null, prof != null ? ShieldLibrary.ById(prof.ShieldId) : null);
            Rearm();
            e.Health.SetMax(enemyMax, true);
            Teleport(e, home + Vector3.forward * 10f, Vector3.back);
            Teleport(p, home, Vector3.forward);
            pc.enabled = true;
            if (ai != null) ai.enabled = true;
            yield return Wait(0.3f);
        }

        /// <summary>Jump + light (slash in the air), jump + kick (diving kick), jump + heavy (slam to the ground).</summary>
        IEnumerator AirTest(Fighter p, Fighter e, string tag)
        {
            var ai = e.GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = false;
            pc.enabled = false;
            Vector3 home = p.transform.position;
            float enemyMax = e.Health.Max;
            e.Health.SetMax(100000f, true);
            var cases = new[] { (AttackType.Light, 1.6f, "slash"), (AttackType.Kick, 3.2f, "dive"), (AttackType.Heavy, 1.7f, "slam") };
            foreach (var (type, dist, name) in cases)
            {
                Teleport(e, home + Vector3.forward * dist, Vector3.back);
                Teleport(p, home, Vector3.forward);
                yield return Wait(0.7f);
                float hp0 = e.Health.Current;
                p.TryJump(Vector3.zero);
                yield return Wait(0.22f);
                float y0 = p.transform.position.y - home.y;
                bool started = p.TryAttack(type);
                AirAttackKind kind = p.AirAttack;
                float t = 0f, groundAt = -1f, maxY = y0;
                bool shot = false;
                var seen = new System.Collections.Generic.List<string>();
                for (; t < 3f; t += Time.deltaTime)
                {
                    string st = p.State.ToString();
                    if (seen.Count == 0 || seen[seen.Count - 1] != st) seen.Add(st);
                    maxY = Mathf.Max(maxY, p.transform.position.y - home.y);
                    if (groundAt < 0f && p.IsGrounded) groundAt = t;
                    if (!shot && t > 0.2f) { shot = true; yield return Shot(tag + "y_air_" + name); continue; }
                    if (p.State == FighterState.Idle && p.IsGrounded && t > 0.3f) break;
                    yield return null;
                }
                Vector3 moved = p.transform.position - home; moved.y = 0f;
                Log($"AIR {name}: started={started} kind={kind} y0={y0:0.00} maxY={maxY:0.00} ground={groundAt:0.00}s moved={moved.magnitude:0.00} " +
                    $"enemyHit={e.Health.Current < hp0} dmg={hp0 - e.Health.Current:0.0} states=[{string.Join(">", seen)}]");
            }
            e.Health.SetMax(enemyMax, true);
            Teleport(e, home + Vector3.forward * 10f, Vector3.back);
            Teleport(p, home, Vector3.forward);
            pc.enabled = true;
            if (ai != null) ai.enabled = true;
            yield return Wait(0.3f);
        }

        /// <summary>Shoulder camera (free, then locked on) and the PS3 look, with shots of each.</summary>
        IEnumerator ViewTest(Fighter p, Fighter e, string tag)
        {
            var ai = e.GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = false;
            Vector3 home = p.transform.position;
            Teleport(p, home, Vector3.forward);
            Teleport(e, home + new Vector3(2.2f, 0f, 5f), Vector3.back);
            var cam = ArenaCamera.Instance;
            Gladiator.Core.GameSettings.CameraMode = 1;
            Gladiator.Core.GameSettings.Apply(false);
            yield return Wait(1.2f);
            float camDist = cam != null ? (cam.transform.position - (p.transform.position + Vector3.up * 1.55f)).magnitude : -1f;
            Log($"VIEW shoulder active={(cam != null && cam.ShoulderActive)} camDist={camDist:0.00} pitch={(cam != null ? cam.transform.eulerAngles.x : 0f):0} cursor={Cursor.lockState}");
            yield return Shot(tag + "v1_hombro");
            if (cam != null) cam.ToggleLock();
            yield return Wait(1.2f);
            Vector3 toE = e.transform.position - p.transform.position; toE.y = 0f;
            Vector3 camF = cam != null ? cam.transform.forward : Vector3.forward; camF.y = 0f;
            Log($"VIEW lock target={(cam != null && cam.LockTarget != null ? cam.LockTarget.DisplayName : "none")} " +
                $"playerFacingErr={Vector3.Angle(p.transform.forward, toE):0} camAimErr={Vector3.Angle(camF, toE):0}");
            yield return Shot(tag + "v2_fijado");

            Gladiator.Core.GameSettings.GraphicsPreset = 1;
            Gladiator.Core.GameSettings.Apply(false);
            yield return Wait(1.2f);
            var rs = Gladiator.PS2Style.PS2RenderSetup.Instance;
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            Log($"VIEW ps3 rt={(rs != null && rs.Target != null ? rs.Target.width + "x" + rs.Target.height + " aa" + rs.Target.antiAliasing : "?")} " +
                $"shadowDist={(urp != null ? urp.shadowDistance : -1f)} msaa={(urp != null ? urp.msaaSampleCount : -1)} sun={(RenderSettings.sun != null ? RenderSettings.sun.shadows.ToString() : "none")}");
            yield return Shot(tag + "v3_ps3_hombro");
            if (cam != null) cam.ToggleLock();
            Gladiator.Core.GameSettings.CameraMode = 0;
            Gladiator.Core.GameSettings.Apply(false);
            yield return Wait(1.2f);
            yield return Shot(tag + "v4_ps3_clasica");
            Gladiator.Core.GameSettings.GraphicsPreset = 0;
            Gladiator.Core.GameSettings.Apply(false);
            yield return Wait(0.8f);
            // Classic camera turned by hand: mouse wheel held + mouse moved, then a quick tap puts it back.
            var mouse = UnityEngine.InputSystem.Mouse.current;
            float turned = 0f, afterTap = 0f, yaw0 = cam != null ? cam.transform.eulerAngles.y : 0f, yawTurned = yaw0;
            if (mouse != null && cam != null)
            {
                var held = new UnityEngine.InputSystem.LowLevel.MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Middle);
                for (int i = 0; i < 30; i++)
                {
                    held.delta = new Vector2(20f, 0f);
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, held);
                    yield return null;
                }
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState());
                yield return Wait(0.8f);
                turned = cam.ClassicTurn;
                yawTurned = cam.transform.eulerAngles.y;
                yield return Shot(tag + "v6_clasica_girada");
                held.delta = Vector2.zero;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, held);
                yield return null;
                yield return null;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState());
                yield return Wait(0.8f);
                afterTap = cam.ClassicTurn;
            }
            Log($"VIEW classic turn={turned:0} camYaw {yaw0:0}->{yawTurned:0} afterTap={afterTap:0} (expected turned, then 0)");
            Log($"VIEW back ps2 rt={(rs != null && rs.Target != null ? rs.Target.width + "x" + rs.Target.height : "?")} shadowDist={(urp != null ? urp.shadowDistance : -1f)} cursor={Cursor.lockState}");
            yield return Shot(tag + "v5_ps2_clasica");
            Teleport(e, home + Vector3.forward * 10f, Vector3.back);
            if (ai != null) ai.enabled = true;
            yield return Wait(0.3f);
        }

        /// <summary>Key rebinding: defaults, a rebound key that works when pressed (simulated), swapping, capture,
        /// and the controls page. Nothing is saved: the player's own keys are reloaded at the end.</summary>
        IEnumerator BindTest(string tag)
        {
            var kbd = UnityEngine.InputSystem.Keyboard.current;
            bool wasEnabled = kbd != null && kbd.enabled;
            if (kbd != null && !kbd.enabled) UnityEngine.InputSystem.InputSystem.EnableDevice(kbd); // window without focus
            Log($"BIND keyboard={(kbd != null ? kbd.displayName : "none")} enabledBefore={wasEnabled} focused={Application.isFocused}");
            InputBindings.ResetDefaults(false);
            Log($"BIND defaults light=[{InputBindings.Describe(GameAction.Light, false)} | {InputBindings.Describe(GameAction.Light, true)}] " +
                $"kick=[{InputBindings.Describe(GameAction.Kick, false)} | {InputBindings.Describe(GameAction.Kick, true)}] " +
                $"move=[{InputBindings.Describe(GameAction.MoveUp, false)} | {InputBindings.Describe(GameAction.MoveUp, true)}]");
            InputBindings.Set(GameAction.Kick, "kb:h", false);
            bool kickH = false, kickF = false;
            if (kbd != null)
            {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kbd, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.H));
                yield return null;
                kickH = InputBindings.Pressed(GameAction.Kick);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kbd, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                yield return null;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kbd, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.F));
                yield return null;
                kickF = InputBindings.Pressed(GameAction.Kick);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kbd, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.N));
                yield return null;
            }
            string captured = InputBindings.CaptureAny();
            if (kbd != null) UnityEngine.InputSystem.InputSystem.QueueStateEvent(kbd, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            yield return null;
            Log($"BIND rebind kick=H pressedH={kickH} (expected True) pressedF={kickF} (expected False) capture={captured} (expected kb:n)");
            InputBindings.Set(GameAction.Jump, "kb:leftCtrl", false);
            Log($"BIND swap jump=[{InputBindings.Describe(GameAction.Jump, false)}] dodge=[{InputBindings.Describe(GameAction.Dodge, false)}] (expected CTRL / ESPACIO)");
            bool padMove = InputBindings.Set(GameAction.MoveUp, "pad:buttonSouth", false);
            Log($"BIND pad move refused={!padMove} (expected True)");
            var pm = Gladiator.UI.PauseMenu.Instance;
            if (pm != null)
            {
                pm.Open();
                pm.ShowPage(Gladiator.UI.PauseMenu.Page.Controls);
                yield return Wait(0.3f);
                yield return Shot(tag + "w_controles");
                pm.Close();
                yield return Wait(0.2f);
            }
            InputBindings.Load();
            Log($"BIND restored kick=[{InputBindings.Describe(GameAction.Kick, false)}]");
        }

        static void Arm(Fighter f, WeaponStats main, WeaponStats off, ShieldStats shield)
        {
            f.UnequipAll();
            f.Equip(WeaponFactory.Create(main));
            if (off != null) f.EquipOff(WeaponFactory.Create(off));
            if (shield != null) Shield.Attach(f, shield, new Color(0.7f, 0.12f, 0.08f));
        }

        /// <summary>
        /// Every weapon family against a standing target: grip flags, which clip plays, damage dealt, and a shot at
        /// the moment of the heavy strike (or the full draw for the bow).
        /// </summary>
        IEnumerator ArsenalTest(Fighter p, Fighter e, string tag)
        {
            var ai = e.GetComponent<EnemyAI>();
            if (ai != null) ai.enabled = false;
            pc.enabled = false;
            Vector3 home = p.transform.position;
            float enemyMax = e.Health.Max;
            e.Health.SetMax(100000f, true); // the target must survive every weapon
            var cam = ArenaCamera.Instance;
            float camD = cam != null ? cam.Distance : 0f, camP = cam != null ? cam.Pitch : 0f, camY = cam != null ? cam.Yaw : 0f;
            if (cam != null) { cam.Distance = 4.2f; cam.Pitch = 14f; cam.Yaw = 55f; } // close side view for the grips
            var loadouts = new (string id, WeaponStats main, WeaponStats off, ShieldStats sh, float dist)[]
            {
                ("hacha2_escudo", WeaponLibrary.GreatAxe, null, ShieldLibrary.Scutum, 2.3f),
                ("mandoble", WeaponLibrary.Greatsword, null, null, 2.5f),
                ("mazo", WeaponLibrary.Maul, null, ShieldLibrary.Iron, 2.2f),
                ("dos_hachas", WeaponLibrary.Axe, WeaponLibrary.Axe, null, 1.9f),
                ("dos_espadas", WeaponLibrary.Gladius, WeaponLibrary.Spatha, null, 1.9f),
                ("lanza_escudo", WeaponLibrary.Spear, null, ShieldLibrary.Wood, 2.6f),
                ("tridente_2m", WeaponLibrary.Trident, null, null, 2.8f),
                ("daga", WeaponLibrary.Dagger, null, null, 1.5f),
                ("maza_clipeus", WeaponLibrary.Mace, null, ShieldLibrary.Iron, 1.9f),
                ("arco", WeaponLibrary.Bow, null, ShieldLibrary.Wood, 9f),
            };
            int n = 0;
            foreach (var lo in loadouts)
            {
                n++;
                Arm(p, lo.main, lo.off, lo.sh);
                Teleport(p, home, Vector3.forward);
                Teleport(e, home + Vector3.forward * lo.dist, Vector3.back);
                e.Health.SetMax(100000f, true);
                yield return Wait(0.6f);
                float hp0 = e.Health.Current;
                var hands = new System.Collections.Generic.List<int>();
                var clips = new System.Collections.Generic.SortedSet<string>();
                bool shot = false;
                if (lo.main.IsBow)
                {
                    // The target is mid-attack when the quick arrow lands: it must keep attacking (no stagger).
                    e.TryAttack(AttackType.Heavy);
                    p.TryAttack(AttackType.Light);
                    bool staggered = false;
                    for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime)
                    {
                        clips.Add(Poser(p));
                        if (e.State == FighterState.Stagger) staggered = true;
                        yield return null;
                    }
                    Log($"QUICK ARROW target staggered={staggered} (expected False) hp lost={hp0 - e.Health.Current:0.0}");
                    yield return WaitIdle(p, 1f);
                    p.SetCharge(true);
                    p.TryAttack(AttackType.Heavy);
                    for (float t = 0f; t < 0.9f; t += Time.unscaledDeltaTime)
                    {
                        clips.Add(Poser(p));
                        if (!shot && t > 0.6f) { shot = true; yield return Shot($"{tag}w{n:00}_{lo.id}"); }
                        yield return null;
                    }
                    Log($"ARSENAL {lo.id} charge={p.ChargeLevel:0.00}");
                    p.SetCharge(false);
                    for (float t = 0f; t < 1.5f; t += Time.unscaledDeltaTime) { clips.Add(Poser(p)); yield return null; }
                }
                else
                {
                    int lights = lo.off != null ? 3 : 1;
                    for (int k = 0; k < lights; k++)
                    {
                        p.TryAttack(AttackType.Light);
                        hands.Add(p.AttackHand);
                        for (float t = 0f; t < 1.5f && p.State != FighterState.Idle; t += Time.unscaledDeltaTime) { clips.Add(Poser(p)); yield return null; }
                        yield return WaitIdle(e, 1f);
                        Teleport(e, home + Vector3.forward * lo.dist, Vector3.back);
                    }
                    p.TryAttack(AttackType.Heavy);
                    hands.Add(p.AttackHand);
                    for (float t = 0f; t < 2f && p.State != FighterState.Idle; t += Time.unscaledDeltaTime)
                    {
                        clips.Add(Poser(p));
                        if (!shot && p.State == FighterState.Active) { shot = true; yield return Shot($"{tag}w{n:00}_{lo.id}"); }
                        yield return null;
                    }
                }
                clips.Remove("-");
                Log($"ARSENAL {lo.id}: main={p.ActiveStats.Name} off={(p.OffWeapon != null ? p.OffWeapon.Stats.Name : "-")} " +
                    $"shield={(p.EquippedShield != null ? p.EquippedShield.Stats.Name + (p.EquippedShield.OnBack ? "(back)" : "(arm)") : "-")} " +
                    $"twoHanded={p.TwoHanded} dual={p.DualWielding} move={p.EquipMoveMul:0.00} block={p.BlockReductionLight:0.00}/{p.BlockReductionHeavy:0.00} " +
                    $"hands=[{string.Join(",", hands)}] clips=[{string.Join(",", clips)}] dmg={hp0 - e.Health.Current:0.0}");
                yield return WaitIdle(p, 1f);
            }

            // Back to the profile's loadout.
            var gm = Gladiator.Core.GameManager.Instance;
            var prof = gm != null ? gm.Profile : null;
            Arm(p, prof != null ? (WeaponLibrary.ByName(prof.MainHand) ?? WeaponLibrary.Gladius) : WeaponLibrary.Gladius,
                prof != null ? WeaponLibrary.ByName(prof.OffHand) : null, prof != null ? ShieldLibrary.ById(prof.ShieldId) : null);
            e.Health.SetMax(enemyMax, true);
            if (cam != null) { cam.Distance = camD; cam.Pitch = camP; cam.Yaw = camY; }
            Teleport(e, home + Vector3.forward * 10f, Vector3.back);
            Teleport(p, home, Vector3.forward);
            pc.enabled = true;
            if (ai != null) ai.enabled = true;
            Log("ARSENAL racks: " + string.Join(", ", System.Linq.Enumerable.Select(arena.RackWeapons, w => w != null ? w.Stats.Name : "?")) +
                "  enemies: " + string.Join(" | ", arena.EnemyLoadouts));
            yield return Wait(0.3f);
        }

        static string Poser(Fighter f)
        {
            var ps = f.GetComponent<Gladiator.Character.HumanoidPoser>();
            return ps != null ? ps.ActionPlaying ?? "-" : "?";
        }

        void OnHit(HitResult r)
        {
            if (r.Outcome == HitOutcome.Hit) hits++;
            else if (r.Outcome == HitOutcome.Blocked) blocks++;
            else guardBreaks++;
            if (r.Disarmed) disarms++;
            Log($"HIT {(r.Attacker != null ? r.Attacker.DisplayName : "?")}->{r.Target.DisplayName} {r.Type} {r.Outcome} zone={r.Zone} armor={r.ArmorAbsorbed:0.00} dmg={r.Damage:0.0} hp={r.Target.Health.Current:0} disarm={r.Disarmed} kill={r.Killed}");
            if (r.ArmorAbsorbed > 0f) { armorHits++; armorStopped += r.ArmorAbsorbed; }
        }

        void OnWeapon(Fighter f, Weapon w)
        {
            weaponChanges++;
            Log($"WEAPON {f.DisplayName} now {(w != null ? w.Stats.Name : "fists")}");
        }

        static void Log(string s) { Debug.Log("[AUTOTEST] " + s); }

        static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(outDir, "autotest_" + name + ".png");
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Log("SCREENSHOT " + path);
        }

        readonly System.Collections.Generic.SortedSet<string> actionsSeen = new System.Collections.Generic.SortedSet<string>();

        /// <summary>Records which MotionBricks action clips play on every gladiator, and grabs one mid-attack shot.</summary>
        IEnumerator TrackActions(string tag)
        {
            var posers = new System.Collections.Generic.List<Gladiator.Character.HumanoidPoser>();
            posers.Add(arena.Player.GetComponent<Gladiator.Character.HumanoidPoser>());
            foreach (var e in arena.Enemies) posers.Add(e.GetComponent<Gladiator.Character.HumanoidPoser>());
            bool shot = false;
            float after = 0f;
            while (after < 0.8f)
            {
                if (arena.Manager.MatchOver) after += Time.unscaledDeltaTime;
                foreach (var ps in posers)
                    if (ps != null && ps.ActionPlaying != null) actionsSeen.Add(ps.ActionPlaying);
                if (!shot && arena.Player.State == FighterState.Active && arena.Player.CurrentAttackType == AttackType.Heavy)
                {
                    shot = true;
                    yield return Shot(tag + "05_attack");
                }
                yield return null;
            }
        }

        IEnumerator Start()
        {
            outDir = Path.GetDirectoryName(Application.dataPath);
            Fighter player = arena.Player;
            Fighter enemy = arena.Enemies[0];
            string tag = "d" + (Gladiator.Core.GameManager.Instance != null ? Gladiator.Core.GameManager.Instance.DayCycle.Day : 0) + "_";
            Log($"START encounter={arena.Manager.Encounter} enemies={arena.Enemies.Count} player={player.ActiveStats.Name} shield={player.GetComponent<Shield>() != null} " +
                $"maxHP={player.Health.Max:0} dmgMul={player.DamageMultiplier:0.00} spdMul={player.AttackSpeedMultiplier:0.00} move={player.MoveSpeed:0.00} " +
                $"enemy0HP={enemy.Health.Max:0} enemy0Dmg={enemy.DamageMultiplier:0.00} res={Screen.width}x{Screen.height}");

            // Simulated keys / mouse must count even when the test window is behind other windows (test only).
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            // The tests frame shots with the classic camera and the PS2 look (not saved: the player's options stay).
            Gladiator.Core.GameSettings.CameraMode = 0;
            Gladiator.Core.GameSettings.GraphicsPreset = 0;
            Gladiator.Core.GameSettings.Apply(false);
            StartCoroutine(TrackActions(tag));
            yield return Wait(1.5f);
            yield return Shot(tag + "01_start");
            var crowd = Gladiator.Town.ArenaCrowdManager.Instance;
            if (crowd != null && crowd.Seated > 0)
            {
                // the townsfolk in the stands (north side), seen from the sand
                var cam0 = ArenaCamera.Instance;
                float d0 = cam0.Distance, p0 = cam0.Pitch;
                cam0.FocusOverride = new Vector3(0f, 2.5f, 9f); cam0.Distance = 13f; cam0.Pitch = 8f;
                yield return Wait(2f);
                yield return Shot(tag + "01b_grada");
                cam0.FocusOverride = null; cam0.Distance = d0; cam0.Pitch = p0;
                yield return Wait(0.5f);
            }

            if (tag == "d1_" && Gladiator.UI.PauseMenu.Instance != null)
            {
                // Pause menu: opens, stops time, mutes the game input, shows every page, closes.
                var pm = Gladiator.UI.PauseMenu.Instance;
                Vector3 enemyAt = enemy.transform.position;
                pm.Open();
                yield return Wait(0.6f);
                Log($"PAUSE open={pm.IsOpen} timeScale={Time.timeScale} inputMuted={Gladiator.Core.GameInput.Paused} enemyMoved={(enemy.transform.position - enemyAt).magnitude:0.000}");
                yield return Shot(tag + "p1_pause");
                pm.ShowPage(Gladiator.UI.PauseMenu.Page.Graphics);
                yield return Wait(0.2f);
                yield return Shot(tag + "p2_graphics");
                pm.ShowPage(Gladiator.UI.PauseMenu.Page.Sound);
                yield return Wait(0.2f);
                yield return Shot(tag + "p3_sound");
                pm.ShowPage(Gladiator.UI.PauseMenu.Page.ConfirmNew);
                yield return Wait(0.2f);
                yield return Shot(tag + "p4_newgame");
                pm.Close();
                yield return Wait(0.3f);
                Log($"PAUSE closed open={pm.IsOpen} timeScale={Time.timeScale} inputMuted={Gladiator.Core.GameInput.Paused}");
            }

            if (arena.Enemies.Count > 1)
            {
                // Free-for-all: player on autopilot, everybody fights everybody.
                pc.enabled = false;
                var ffaBot = player.gameObject.AddComponent<EnemyAI>();
                ffaBot.Aggression = 1.2f;
                ffaBot.BlockChance = 0.35f;
                yield return Wait(5f);
                yield return Shot(tag + "02_ffa");
                float el = 0f;
                while (!arena.Manager.MatchOver && el < 90f) { el += Time.unscaledDeltaTime; yield return null; }
                Log("ACTIONS played: " + string.Join(", ", actionsSeen));
            Log($"MOVES {string.Join(", ", System.Linq.Enumerable.Select(moves, kv => kv.Key + "=" + kv.Value))} evaded={evaded}");
                Log($"FFA over={arena.Manager.MatchOver} playerWon={arena.Manager.PlayerWon} kills={arena.Manager.PlayerKills} reward={arena.Manager.Reward} playerHP={player.Health.Current:0}");
                yield return Wait(1.2f);
                yield return Shot(tag + "04_end");
                Log($"SUMMARY hits={hits} blocks={blocks} guardBreaks={guardBreaks} disarms={disarms}");
                LogAudio();
                Log("ARENA DONE -> continue to next day");
                arena.Manager.Continue();
                yield break;
            }

            // 1) Weapon pickup (same path as pressing E).
            Weapon spare = arena.SpareWeapon;
            spare.PlaceAt(player.transform.position + player.transform.right * 0.9f + Vector3.up * 0.3f);
            yield return Wait(0.8f);
            string before = player.ActiveStats.Name;
            bool ok = player.TryPickup(spare);
            Log($"PICKUP ok={ok} {before} -> {player.ActiveStats.Name}");
            if (tag == "d1_") yield return MovementTest(player, tag);
            if (tag == "d1_") yield return ArsenalTest(player, enemy, tag);
            if (tag == "d1_") yield return ArmorTest(player, enemy, tag);
            if (tag == "d1_") yield return DisarmTest(player, enemy, tag);
            if (tag == "d1_") yield return AirTest(player, enemy, tag);
            if (tag == "d1_") yield return ViewTest(player, enemy, tag);
            if (tag == "d1_") yield return BindTest(tag);

            // 2) Let the player fight on autopilot (same AI as enemies).
            pc.enabled = false;
            var bot = player.gameObject.AddComponent<EnemyAI>();
            bot.Aggression = 1.2f;
            bot.BlockChance = 0.3f;
            yield return Wait(4f);
            yield return Shot(tag + "02_combat");

            // 3) Forced disarm: the enemy must walk to a weapon and re-arm.
            if (enemy.IsAlive && enemy.CurrentWeapon != null)
            {
                enemy.DropWeapon(enemy.transform.right * 3f + Vector3.up * 4f);
                Log("FORCED DISARM enemy");
                float t = 0f;
                while (t < 8f && enemy.IsAlive && enemy.CurrentWeapon == null) { t += Time.unscaledDeltaTime; yield return null; }
                Log($"REARM enemy weapon={(enemy.CurrentWeapon != null ? enemy.CurrentWeapon.Stats.Name : "none")} after {t:0.0}s");
            }
            yield return Shot(tag + "03_rearm");
            foreach (var f in new[] { player, enemy })
            {
                var poser = f.GetComponent<Gladiator.Character.HumanoidPoser>();
                if (poser == null) continue;
                Log($"LOOK {f.DisplayName} -> {(poser.LookTarget != null ? poser.LookTarget.DisplayName : "ahead")} clips={poser.UsingClips}");
            }

            // 4) Fight to the end.
            float elapsed = 0f;
            float limit = arena.Manager.Encounter == Gladiator.Progression.EncounterType.Waves ? 150f : 75f;
            int shotWave = 0;
            while (!arena.Manager.MatchOver && elapsed < limit)
            {
                elapsed += Time.unscaledDeltaTime;
                if (arena.Manager.Encounter == Gladiator.Progression.EncounterType.Waves && arena.Manager.Wave != shotWave && arena.Manager.Wave >= 2)
                {
                    shotWave = arena.Manager.Wave;
                    Log($"WAVE {shotWave} spawned: enemies total={arena.Enemies.Count} down={arena.Manager.EnemiesDown}");
                    yield return Wait(1.5f);
                    yield return Shot(tag + "w_oleada" + shotWave);
                }
                yield return null;
            }
            yield return Wait(1.0f);
            Log("ACTIONS played: " + string.Join(", ", actionsSeen));
            Log($"MOVES {string.Join(", ", System.Linq.Enumerable.Select(moves, kv => kv.Key + "=" + kv.Value))} evaded={evaded}");
            Log("LOADOUTS " + string.Join(" | ", arena.EnemyLoadouts));
            Log($"SHADOWS planar={FindObjectsByType<Gladiator.PS2Style.PlanarShadow>(FindObjectsSortMode.None).Length} available={Gladiator.PS2Style.PlanarShadow.Available}");
            if (enemy != null && !enemy.IsAlive) yield return Shot(tag + "06_death");
            int corpses = 0;
            foreach (var e in arena.Enemies) if (e != null && !e.IsAlive && e.gameObject.activeInHierarchy) corpses++;
            Log($"MATCH mode={arena.Manager.Encounter} over={arena.Manager.MatchOver} playerWon={arena.Manager.PlayerWon} reward={arena.Manager.Reward} " +
                $"wave={arena.Manager.Wave} cleared={arena.Manager.WavesCleared} down={arena.Manager.EnemiesDown} corpses={corpses} pools={FindObjectsByType<BloodPool>(FindObjectsSortMode.None).Length} " +
                $"playerHP={player.Health.Current:0} enemyHP={enemy.Health.Current:0}");
            yield return Wait(1.2f);
            yield return Shot(tag + "04_end");
            yield return Wait(0.5f);

            Log($"SUMMARY hits={hits} blocks={blocks} guardBreaks={guardBreaks} disarms={disarms} weaponChanges={weaponChanges} groundWeapons={Weapon.GroundWeapons.Count}");
            LogAudio();
            Log("ARENA DONE -> continue to next day");
            arena.Manager.Continue();
        }
    }
}
