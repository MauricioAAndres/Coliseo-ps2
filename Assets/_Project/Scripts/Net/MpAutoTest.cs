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

            // ---- the town online: both walk in through the ludus' east door
            // the ludus is inside the town: already there
            yield return Until(() => Gladiator.Town.TownBootstrap.Instance != null && NetAvatars.Instance != null, 20f);
            var townH = Gladiator.Town.TownBootstrap.Instance;
            yield return Until(() => NetAvatars.Instance.Remotes.Any(x => x.Present), 30f);
            var simH = Gladiator.Town.TownSimulation.Instance;
            Log($"TOWN host seed={simH.Layout.Seed} netSeed={ns.TownSeed} hour={simH.Hour:0.00} seesClient={NetAvatars.Instance.Remotes.Any(x => x.Present)}");
            yield return Wait(2f);
            var doorH = townH.Houses.Find(hh => hh.Door != null).Door;
            doorH.Toggle(); Gladiator.Town.TownManager.SendDoor(doorH);
            Log($"TOWN host opened door of house {townH.Houses.Find(hh => hh.Door != null).Lot.Id}");
            // a cell gate of the jail, and lying on a bed: the client must see both
            townH.Guards.Suspended = true;
            var jailH = townH.Jail;
            if (jailH != null) { jailH.ExtraDoors[1].Toggle(); Gladiator.Town.TownManager.SendDoor(jailH.ExtraDoors[1]); }
            var bedHouseH = townH.Houses.Find(hh => !hh.Public && hh.Floor == 0 && hh.Furniture.Exists(f => f.Kind == Fighter.ActLie));
            var bedH = bedHouseH.Furniture.Find(f => f.Kind == Fighter.ActLie);
            bool lay = townH.GetComponent<Gladiator.Town.TownManager>().UseFurniture(bedH);
            yield return Wait(3.3f);
            {
                // in bed before the other player: the host cannot start the sleep yet
                var tm0 = townH.GetComponent<Gladiator.Town.TownManager>();
                tm0.BedCount(out int ly0, out int tot0);
                bool early = ly0 < tot0 && tm0.StartSleep(2);
                Log($"SLEEP host first in bed: lying={ly0}/{tot0} sleepStarts={early} (expected 1/2 and False) sleeping={tm0.Sleeping}");
            }
            yield return Wait(2.7f);
            Log($"TOWN host on the bed of house {bedHouseH.Lot.Id}: ok={lay} onFurniture={townH.Player.OnFurniture} offSpot={(townH.Player.transform.position - bedH.At.position).magnitude:0.00}m cellGateOpen={(jailH != null && jailH.ExtraDoors[1].IsOpen)}");
            yield return Shot("mp_host_pueblo");
            // sleeping online: not while the other player is up; once both are in bed the host picks the hours
            {
                var tmS = townH.GetComponent<Gladiator.Town.TownManager>();
                tmS.BedCount(out int ly, out int tot);
                bool alone = tmS.StartSleep(2);
                Log($"SLEEP host alone in bed: lying={ly}/{tot} sleepStarts={alone} (expected False)");
                yield return Until(() => { tmS.BedCount(out ly, out tot); return ly >= tot; }, 20f);
                float hS = simH.Hour; int dS = simH.Day;
                bool all = tmS.StartSleep(2);
                yield return Wait(0.5f);
                bool asleep = tmS.Sleeping;
                yield return Until(() => !tmS.Sleeping, 6f);
                Log($"SLEEP host with everybody in bed: lying={ly}/{tot} sleepStarts={all} (expected True) asleep={asleep} hours {hS:0.0}->{(simH.Day - dS) * 24f + simH.Hour:0.0} (expected +2) stillSleeping={tmS.Sleeping}");
            }
            townH.Player.StopActivity();
            yield return Wait(2.5f);
            // the host buys a house for sale and a bed for it: the client must see whose it is, and the bed
            var tmH = townH.GetComponent<Gladiator.Town.TownManager>();
            var saleH = townH.Houses.Find(hh => hh.Floor == 0 && hh.Lot.ForSale && !Gladiator.Town.HouseDeeds.Owns(townH.Layout.Seed, hh.Lot.Id));
            if (saleH != null)
            {
                var gmH = Gladiator.Core.GameManager.Instance;
                int moneyH = gmH.Profile.Denarii;
                gmH.Profile.AddDenarii(1000);
                bool boughtH = tmH.TryBuy(saleH), bedBought = tmH.BuyFurniture("bed");
                Log($"TOWN host bought house {saleH.Lot.Id}={boughtH} bed={bedBought}");
                yield return Wait(4f);
                Gladiator.Town.HouseDeeds.Remove(townH.Layout.Seed, saleH.Lot.Id);
                Gladiator.Town.HomeFurniture.Clear(townH.Layout.Seed, saleH.Lot.Id);
                gmH.Profile.AddDenarii(moneyH - gmH.Profile.Denarii);
            }

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
            yield return Wait(2f);

            // ---- MP3: co-op against the host's bots, "double" (two rivals per player)
            ns.StartArena(false, false, NetSession.ModeWaves);
            yield return Until(() => NetArena.Instance != null && NetArena.Instance.Coop && NetAvatars.Instance != null && NetAvatars.Instance.RemoteCount >= 1, 30f);
            var nc = NetArena.Instance;
            if (nc == null) { Log("COOP NOT LOADED"); yield break; }
            var meC = NetAvatars.Instance.Local;
            var pcC = meC.GetComponent<PlayerController>(); if (pcC != null) pcC.enabled = false;
            var partnerC = NetAvatars.Instance.Remotes.First();
            Log($"COOP host bots={nc.Rivals.Count} (expected 2, wave 1 x1.5) myTeam={meC.TeamId} partnerTeam={partnerC.Fighter.TeamId}");
            yield return Wait(5f);
            yield return Shot("mp_host_coop");
            yield return Until(() => nc.Ended, 10f);
            int botsDown = nc.Rivals.Count(b => !b.IsAlive);
            Log($"COOP host after fight: ended={nc.Ended} botHitsFromClient={nc.BotHitsApplied} botsDown={botsDown} hostHp={meC.Health.Current:0} " +
                $"rivalsHp=[{string.Join(",", nc.Rivals.Select(b => b.Health.Current.ToString("0")))}]");
            // bring every wave down until the last one (the client must see each new wave)
            for (float t = 0f; t < 60f && !nc.Ended; t += 4f)
            {
                nc.DebugKillAllBots();
                yield return Wait(4f);
                Log($"COOP host wave={nc.Wave} rivals={nc.Rivals.Count}");
            }
            Log($"COOP END winner='{nc.Winner}' waves={nc.Wave} rivalsTotal={nc.Rivals.Count}");
            yield return Wait(1f);
            yield return Shot("mp_host_coop_final");
            yield return Until(() => NetArena.Instance == null && NetAvatars.Instance != null && NetAvatars.Instance.RemoteCount >= 1 && ns.Phase == NetSession.PhaseLudus, 20f);
            Log($"BACK2 phase={ns.Phase}");
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

            // the town online: go in; the host is there too; same seed and clock; the host's door opens here too
            yield return Wait(3f);
            // the ludus is inside the town: already there
            yield return Until(() => Gladiator.Town.TownBootstrap.Instance != null && NetAvatars.Instance != null, 20f);
            var townC = Gladiator.Town.TownBootstrap.Instance;
            yield return Until(() => NetAvatars.Instance.Remotes.Any(x => x.Present), 30f);
            var simC = Gladiator.Town.TownSimulation.Instance;
            Log($"TOWN client seed={simC.Layout.Seed} netSeed={ns.TownSeed} hour={simC.Hour:0.00} seesHost={NetAvatars.Instance.Remotes.Any(x => x.Present)}");
            yield return Until(() => townC.Houses.Find(hh => hh.Door != null).Door.IsOpen, 15f);
            Log($"TOWN client sees door of house {townC.Houses.Find(hh => hh.Door != null).Lot.Id} open={townC.Houses.Find(hh => hh.Door != null).Door.IsOpen} (expected True) hour={simC.Hour:0.00}");
            // the host lies on a bed and opens a cell of the jail: seen here too, on the bed (not on the floor next to it)
            var bedHouseC = townC.Houses.Find(hh => !hh.Public && hh.Floor == 0 && hh.Furniture.Exists(f => f.Kind == Fighter.ActLie));
            var bedC = bedHouseC.Furniture.Find(f => f.Kind == Fighter.ActLie);
            RemoteFighter hostC = null;
            yield return Until(() => (hostC = NetAvatars.Instance.Remotes.FirstOrDefault(x => x.Present && x.Fighter != null && x.Fighter.OnFurniture)) != null, 15f);
            yield return Wait(1.5f);
            var jailC = townC.Jail;
            bool mat = GameObject.Find("ActivityProps") is GameObject apg && apg.transform.childCount > 0;
            Log($"TOWN client sees host on the bed: onFurniture={(hostC != null)} (expected True) kind={(hostC != null ? hostC.Fighter.ActivityKind : -1)} " +
                $"offSpot={(hostC != null ? (hostC.Fighter.transform.position - bedC.At.position).magnitude : -1f):0.00}m (expected ~0) matOnTheFloor={mat} (expected False) " +
                $"cellGateOpen={(jailC != null && jailC.ExtraDoors[1].IsOpen)} (expected True)");
            if (hostC != null)
            {
                var camC = townC.Cam;
                for (float t = 0f; t < 0.8f; t += Time.deltaTime) { camC.FocusOverride = bedC.At.position + Vector3.up * 0.3f; camC.Distance = 4f; camC.Pitch = 50f; yield return null; }
            }
            yield return Shot("mp_client_pueblo");
            if (townC.Cam != null) townC.Cam.FocusOverride = null;
            // sleeping online: the guest gets into a bed and waits; the host decides, both sleep and wake together
            {
                var tmS = townC.GetComponent<Gladiator.Town.TownManager>();
                townC.PutInInfirmaryBed(out _);
                yield return Until(() => Gladiator.Town.TownManager.InBed(townC.Player), 6f);
                yield return Wait(0.3f);
                tmS.BedCount(out int lyC, out int totC);
                bool ownChoice = tmS.StartSleep(5);
                float hC = simC.Hour; int dC = simC.Day; float hpC = townC.Player.Health.Current;
                townC.Player.Health.TakeDamage(townC.Player.Health.Current - townC.Player.Health.Max * 0.5f);
                yield return Until(() => tmS.Sleeping, 20f);
                bool put = tmS.Sleeping;
                float tSl = Time.time;
                yield return Until(() => !tmS.Sleeping, 8f);
                yield return Wait(0.6f);
                Log($"SLEEP guest in bed: lying={lyC}/{totC} own choice starts it={ownChoice} (expected False); put to sleep by the host={put} (expected True) for {Time.time - tSl - 0.6f:0.0} s " +
                    $"hours {hC:0.0}->{(simC.Day - dC) * 24f + simC.Hour:0.0} (expected about +2) health {townC.Player.Health.Normalized:0.00} (expected about 0.70) awake={!tmS.Sleeping}");
                townC.Player.Health.Heal(townC.Player.Health.Max);
                if (townC.Player.InActivity) townC.Player.StopActivity();
            }
            yield return Until(() => hostC == null || !hostC.Fighter.InActivity, 10f);
            Log($"TOWN client: host got up={(hostC == null || !hostC.Fighter.InActivity)} ccOn={(hostC != null && hostC.Fighter.Controller.enabled)}");
            yield return Until(() => townC.OtherOwners.Count > 0, 15f);
            foreach (var kv in townC.OtherOwners)
            {
                var hC = townC.HouseOfLot(kv.Key);
                Log($"TOWN client sees house {kv.Key} owned by '{kv.Value}' saleSignHidden={(hC != null && hC.SaleSign != null && !hC.SaleSign.activeSelf)} bedInside={(hC != null && hC.Furniture.Exists(f => f.Kind == Fighter.ActLie))} (expected True) " +
                    $"canBuyIt={townC.GetComponent<Gladiator.Town.TownManager>().TryBuy(hC)} (expected False)");
            }

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
            Log($"ARENA END winner='{na.Winner}' hitsSent={na.HitsSent} hostHpSeen={hostA.Fighter.Health.Current:0} hostDeadSeen={!hostA.Fighter.IsAlive} hostRagdoll={hostA.Fighter.Ragdolled} ragdolls={Ragdoll.Activations} myHp={meA.Health.Current:0}");
            yield return Shot("mp_client_final");
            yield return Until(() => NetArena.Instance == null && ns.Phase == NetSession.PhaseLudus && NetAvatars.Instance != null && NetAvatars.Instance.RemoteCount >= 1, 20f);
            Log($"BACK phase={ns.Phase} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");

            // MP3: co-op against the host's bots: hit the nearest rival copy until the match ends
            yield return Until(() => NetArena.Instance != null && NetArena.Instance.Coop && NetAvatars.Instance != null, 45f);
            var nc = NetArena.Instance;
            if (nc == null) { Log("COOP NOT LOADED"); yield break; }
            var meC = NetAvatars.Instance.Local;
            var pcC = meC.GetComponent<PlayerController>(); if (pcC != null) pcC.enabled = false;
            yield return Until(() => nc.Rivals.Count >= 2, 10f);
            Log($"COOP client copies={nc.Rivals.Count} (expected 2) myTeam={meC.TeamId}");
            float swingC = 0f;
            for (float t = 0f; t < 90f && !nc.Ended; t += Time.deltaTime)
            {
                Fighter tgt = null; float best = 1e9f;
                foreach (var b in nc.Rivals) { if (!b.IsAlive) continue; float d = (b.transform.position - meC.transform.position).sqrMagnitude; if (d < best) { best = d; tgt = b; } }
                if (tgt == null || !meC.IsAlive) { meC.Move(Vector3.zero); yield return null; continue; }
                Vector3 to = tgt.transform.position - meC.transform.position; to.y = 0f;
                if (to.magnitude > 1.4f) meC.Move(to.normalized);
                else { meC.Move(Vector3.zero); meC.SetFacing(to); if ((swingC -= Time.deltaTime) <= 0f) { meC.TryAttack(AttackType.Light); swingC = 0.75f; } }
                if (t > 6f && t < 6.05f) yield return Shot("mp_client_coop");
                yield return null;
            }
            meC.Move(Vector3.zero);
            Log($"COOP client END winner='{nc.Winner}' wave={nc.Wave} copies={nc.Rivals.Count} statesReceived={nc.BotStatesReceived} botHitsSent={nc.BotHitsSent} hitsTakenFromBots={nc.HitsTaken} " +
                $"myHp={meC.Health.Current:0} copiesHp=[{string.Join(",", nc.Rivals.Select(b => b.Health.Current.ToString("0")))}] copiesMoved={nc.Rivals.Count(b => b.GetComponent<RemoteFighter>().Travelled > 0.5f)}");
            yield return Shot("mp_client_coop_final");
            yield return Until(() => NetArena.Instance == null && ns.Phase == NetSession.PhaseLudus && NetAvatars.Instance != null && NetAvatars.Instance.RemoteCount >= 1, 25f);
            Log($"BACK2 phase={ns.Phase}");
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
