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
            yield return Wait(1f);
            ns.StartGame();
            yield return Until(() => NetAvatars.Instance != null && NetAvatars.Instance.RemoteCount >= 1, 30f);
            var nav = NetAvatars.Instance;
            Log($"LUDUS inGame={ns.InGame} avatars={(nav != null ? nav.RemoteCount : -1)} local={(nav != null && nav.Local != null ? nav.Local.transform.position.ToString() : "?")}");
            if (nav == null || nav.RemoteCount < 1) yield break;
            yield return Wait(12f);
            var r = nav.Remotes.First();
            yield return Shot("mp_host_ludus");
            Log($"REMOTE id={r.PlayerId} name={r.Fighter.DisplayName} received={r.StatesReceived} travelled={r.Travelled:0.00} " +
                $"states=[{string.Join(",", r.StatesSeen.OrderBy(x => x))}] weapon={(r.Fighter.CurrentWeapon != null ? r.Fighter.CurrentWeapon.Stats.Id : "none")}");
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
            yield return Until(() => !ns.Active, 25f);
            yield return Wait(0.5f);
            Log($"KICKED active={ns.Active} notice='{ns.LastNotice}' avatars={nav.RemoteCount}");
        }
    }
}
