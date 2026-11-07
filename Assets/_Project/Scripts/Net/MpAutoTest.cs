using System.Collections;
using System.IO;
using System.Linq;
using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Net
{
    /// <summary>
    /// Two-process multiplayer smoke test. Run one copy with "-mptest host" and another with "-mptest join":
    /// the host creates the room, waits for the other, starts the shared ludus, watches the other gladiator move and
    /// attack, then kicks them. The client finds the game on the network, joins by 127.0.0.1, walks, attacks, jumps
    /// and taunts, and waits to be kicked. Both log "[MPTEST] ..." lines, take a screenshot and quit.
    /// </summary>
    public class MpAutoTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-mptest");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("MpAutoTest");
            DontDestroyOnLoad(go);
            go.AddComponent<MpAutoTest>().role = args[i + 1];
        }

        string role;

        IEnumerator Start()
        {
            yield return role == "host" ? HostFlow() : JoinFlow();
            Log("DONE");
            yield return Wait(0.5f);
            Application.Quit();
        }

        static void Log(string s) { Debug.Log("[MPTEST] " + s); }

        static IEnumerator Wait(float s)
        {
            for (float t = 0f; t < s; t += Time.unscaledDeltaTime) yield return null;
        }

        static IEnumerator Until(System.Func<bool> cond, float max)
        {
            for (float t = 0f; t < max && !cond(); t += Time.unscaledDeltaTime) yield return null;
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "autotest_" + name + ".png");
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Log("SCREENSHOT " + path);
        }

        static string Roster(NetSession ns) => string.Join(",", ns.Players.Select(p => p.Id + ":" + p.G.Name + (p.Host ? "(host)" : "")));

        IEnumerator HostFlow()
        {
            yield return Wait(2f);
            var ns = NetSession.Ensure();
            bool ok = ns.Host();
            Log($"HOST ok={ok} port={ns.Port} ips={string.Join(",", NetSession.LocalAddresses())}");
            yield return Until(() => ns.Players.Count >= 2, 60f);
            Log($"ROSTER n={ns.Players.Count} [{Roster(ns)}]");
            if (ns.Players.Count < 2) yield break;
            Log("ADDRESSES [" + string.Join(" ; ", NetSession.AddressLines()) + "]");
            var pm = Gladiator.UI.PauseMenu.Instance;
            if (pm != null)
            {
                pm.Open();
                pm.ShowPage(Gladiator.UI.PauseMenu.Page.Lobby);
                yield return Wait(0.6f);
                yield return Shot("mp_host_sala");
                pm.Close();
            }
            yield return Wait(1f);
            ns.StartGame();
            yield return Until(() => NetAvatars.Instance != null && NetAvatars.Instance.RemoteCount >= 1, 30f);
            var nav = NetAvatars.Instance;
            Log($"LUDUS inGame={ns.InGame} avatars={(nav != null ? nav.RemoteCount : -1)} local={(nav != null && nav.Local != null ? nav.Local.transform.position.ToString() : "?")}");
            if (nav == null || nav.RemoteCount < 1) yield break;
            int complaints0 = 0; // the partners' complaints before the client starts hitting them
            foreach (var n in Gladiator.Training.TrainingNPC.All) complaints0 += n.Complaints;
            yield return Wait(12f);
            var r = nav.Remotes.First();
            var rp = ns.Get(r.PlayerId);
            Log($"COLORS host={NetSession.ColorNames[ns.Local.Color]} client={(rp != null ? NetSession.ColorNames[rp.Color] : "?")}");
            yield return Shot("mp_host_ludus");
            Log($"REMOTE id={r.PlayerId} name={r.Fighter.DisplayName} received={r.StatesReceived} travelled={r.Travelled:0.00} " +
                $"states=[{string.Join(",", r.StatesSeen.OrderBy(x => x))}] weapon={(r.Fighter.CurrentWeapon != null ? r.Fighter.CurrentWeapon.Stats.Id : "none")}");
            // ---- ludus: the client hits a training partner and throws its rudis; we must see both
            int maxGhosts = 0;
            for (float t = 0f; t < 20f; t += Time.unscaledDeltaTime)
            {
                maxGhosts = Mathf.Max(maxGhosts, nav.GhostCount);
                int c = 0; foreach (var n in Gladiator.Training.TrainingNPC.All) c += n.Complaints;
                if (c > complaints0 && maxGhosts > 0) break;
                yield return null;
            }
            int complaints1 = 0; foreach (var n in Gladiator.Training.TrainingNPC.All) complaints1 += n.Complaints;
            Log($"SYNC partnerHitsSeen={complaints1 - complaints0} (expected >0) thrownCopiesSeen={maxGhosts} (expected >0)");
            yield return Shot("mp_host_lanzada");

            // ---- MP2: online arena, everyone for himself, no upgrades
            ns.StartArena(false, false);
            yield return Until(() => NetArena.Instance != null && NetAvatars.Instance != null && NetAvatars.Instance.RemoteCount >= 1, 30f);
            var na = NetArena.Instance;
            var nav2 = NetAvatars.Instance;
            if (na == null || nav2 == null) { Log("ARENA NOT LOADED"); yield break; }
            var meA = nav2.Local;
            var pcA = meA.GetComponent<PlayerController>(); if (pcA != null) pcA.enabled = false; // the host stands and takes it
            var rivalA = nav2.Remotes.First();
            Log($"ARENA host team={meA.TeamId} hp={meA.Health.Current:0} rival={rivalA.Fighter.DisplayName} rivalTeam={rivalA.Fighter.TeamId} " +
                $"weapon={(meA.CurrentWeapon != null ? meA.CurrentWeapon.Stats.Id : "-")} pos={meA.transform.position}");
            yield return Wait(4f);
            Log($"HUD rivalBars={(Object.FindAnyObjectByType<Gladiator.UI.GameHUD>() != null ? Object.FindAnyObjectByType<Gladiator.UI.GameHUD>().EnemyBarCount : -1)} (expected 1)");
            yield return Shot("mp_host_arena");
            yield return Until(() => na.Ended, 60f);
            Log($"ARENA END winner='{na.Winner}' hostHp={meA.Health.Current:0} hostDead={na.IsDead(ns.LocalId)} hitsTaken={na.HitsTaken} rivalHpSeen={rivalA.Fighter.Health.Current:0}");
            yield return Wait(1f);
            yield return Shot("mp_host_final");
            yield return Until(() => NetArena.Instance == null && NetAvatars.Instance != null && NetAvatars.Instance.RemoteCount >= 1 && ns.Phase == NetSession.PhaseLudus, 20f);
            Log($"BACK phase={ns.Phase} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            nav = NetAvatars.Instance;
            r = nav.Remotes.First();
            yield return Wait(2f);

            ns.Kick(r.PlayerId);
            yield return Wait(1.5f);
            Log($"KICK roster={ns.Players.Count} avatars={nav.RemoteCount}");
            ns.Leave(true);
        }

        IEnumerator JoinFlow()
        {
            yield return Wait(3f);
            var ns = NetSession.Ensure();
            ns.StartBrowsing();
            yield return Wait(2.5f);
            var games = ns.FoundGames();
            Log($"DISCOVERY found={games.Count} [{string.Join(",", games.Select(g => g.HostName + "@" + g.Address + ":" + g.Port + " " + g.Count + "p"))}]");
            ns.StopBrowsing();
            ns.Join("127.0.0.1");
            yield return Until(() => ns.LocalId > 0 || !ns.Active, 10f);
            Log($"WELCOME id={ns.LocalId} active={ns.Active}");
            yield return Until(() => NetAvatars.Instance != null && NetAvatars.Instance.RemoteCount >= 1, 30f);
            var nav = NetAvatars.Instance;
            Log($"LUDUS inGame={ns.InGame} avatars={(nav != null ? nav.RemoteCount : -1)} roster=[{Roster(ns)}]");
            if (nav == null || nav.RemoteCount < 1) yield break;
            yield return Wait(1f);

            var me = nav.Local;
            var host = nav.Remotes.First();
            var pc = me.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = false;
            Vector3 start = me.transform.position;
            for (float t = 0f; t < 1.6f; t += Time.deltaTime) { me.Move(Vector3.forward); me.SetFacing(Vector3.forward); yield return null; }
            me.Move(Vector3.zero);
            yield return Wait(0.3f);
            me.TryAttack(AttackType.Light);
            yield return Wait(1f);
            me.TryAttack(AttackType.Heavy);
            yield return Wait(1.3f);
            me.TryJump(Vector3.zero);
            yield return Wait(1.2f);
            me.TryTaunt(0);
            yield return Wait(1.2f);
            for (float t = 0f; t < 1.2f; t += Time.deltaTime) { me.Move(Vector3.right); yield return null; }
            me.Move(Vector3.zero);
            Log($"CLIENT moved={(me.transform.position - start).magnitude:0.00} hostAvatar={host.Fighter.DisplayName} received={host.StatesReceived}");
            yield return Shot("mp_client_ludus");
            ns.SetColor(1); // verde

            // hit a training partner (the host must see it) and throw the rudis (the host must see it fly)
            var partner = Gladiator.Training.TrainingNPC.All.Count > 0 ? Gladiator.Training.TrainingNPC.All[0] : null;
            if (partner != null)
            {
                Vector3 pp = partner.Fighter.transform.position;
                Vector3 from = pp - partner.Fighter.transform.forward * 1.2f;
                me.Controller.enabled = false; me.transform.position = from + Vector3.up * 0.05f; me.Controller.enabled = true;
                me.SetFacing(pp - from);
                me.transform.rotation = Quaternion.LookRotation(new Vector3(pp.x - from.x, 0f, pp.z - from.z));
                yield return Wait(0.4f);
                for (int i = 0; i < 3; i++) { me.SetFacing(partner.Fighter.transform.position - me.transform.position); me.TryAttack(AttackType.Light); yield return Wait(0.7f); }
                Log($"CLIENT hit partner {partner.DisplayName} complaints={partner.Complaints}");
            }
            yield return Wait(0.5f);
            bool threw = me.TryThrow();
            Log($"CLIENT threw={threw}");
            yield return Wait(2f);

            // MP2: the host starts the online arena; fight the host until the match ends
            yield return Until(() => NetArena.Instance != null && NetAvatars.Instance != null && NetAvatars.Instance.RemoteCount >= 1, 45f);
            var na = NetArena.Instance;
            var navA = NetAvatars.Instance;
            if (na == null || navA == null) { Log("ARENA NOT LOADED"); yield break; }
            var meA = navA.Local;
            var pcA = meA.GetComponent<PlayerController>(); if (pcA != null) pcA.enabled = false;
            var hostA = navA.Remotes.First();
            Log($"ARENA client team={meA.TeamId} hp={meA.Health.Current:0} rival={hostA.Fighter.DisplayName} rivalTeam={hostA.Fighter.TeamId}");
            yield return Wait(1.5f);
            float swing = 0f;
            for (float t = 0f; t < 50f && !na.Ended; t += Time.deltaTime)
            {
                Vector3 to = hostA.transform.position - meA.transform.position; to.y = 0f;
                if (to.magnitude > 1.4f) meA.Move(to.normalized);
                else
                {
                    meA.Move(Vector3.zero);
                    meA.SetFacing(to);
                    if ((swing -= Time.deltaTime) <= 0f) { meA.TryAttack(AttackType.Light); swing = 0.75f; }
                }
                yield return null;
            }
            meA.Move(Vector3.zero);
            yield return Wait(0.5f);
            Log($"ARENA END winner='{na.Winner}' hitsSent={na.HitsSent} hostHpSeen={hostA.Fighter.Health.Current:0} hostDeadSeen={!hostA.Fighter.IsAlive} myHp={meA.Health.Current:0}");
            yield return Shot("mp_client_final");
            yield return Until(() => NetArena.Instance == null && ns.Phase == NetSession.PhaseLudus && NetAvatars.Instance != null && NetAvatars.Instance.RemoteCount >= 1, 20f);
            Log($"BACK phase={ns.Phase} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            nav = NetAvatars.Instance;
            yield return Until(() => !ns.Active, 30f);
            yield return Wait(0.5f);
            Log($"KICKED active={ns.Active} notice='{ns.LastNotice}' avatars={nav.RemoteCount}");

            yield return Wait(2.5f); // the host has closed its room by now
            // Joining failures must say why.
            ns.Join("127.0.0.1:7790"); // nobody listens there
            yield return Until(() => !ns.Active, 10f);
            Log($"FAIL refused notice='{ns.LastNotice}' help='{ns.LastHelp}'");
            var mine = NetSession.LocalAddresses();
            if (mine.Count > 0)
            {
                ns.Join(mine[0]);
                yield return Until(() => !ns.Active, 10f);
                Log($"FAIL own-ip notice='{ns.LastNotice}'");
            }
            ns.Join("192.168.250.77"); // a LAN address of another network: nobody answers
            yield return Until(() => !ns.Active, 12f);
            Log($"FAIL lan notice='{ns.LastNotice}' help='{ns.LastHelp}'");
            var pm = Gladiator.UI.PauseMenu.Instance;
            if (pm != null)
            {
                pm.Open();
                pm.ShowPage(Gladiator.UI.PauseMenu.Page.Multi);
                yield return Wait(0.6f);
                yield return Shot("mp_client_ayuda");
                pm.Close();
            }
        }
    }
}
