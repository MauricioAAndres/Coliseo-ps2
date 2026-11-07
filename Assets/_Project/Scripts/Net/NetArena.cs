using System.Collections.Generic;
using Gladiator.Arena;
using Gladiator.Combat;
using Gladiator.UI;
using UnityEngine;

namespace Gladiator.Net
{
    /// <summary>
    /// Online arena (MP2): every player for himself, or teams by colour; with each one's own upgrades or all equal.
    /// Each game is the referee of its own gladiator: when you hit someone's copy, your game sends the blow ("HT") to
    /// that player, whose game applies it (their guard, their roll, their armour decide) and tells everybody their
    /// health ("HP") and, if it comes to that, their death ("DIE"). The host watches who is still standing and, when
    /// only one player or one colour is left, announces the winner ("AEND") and takes everybody back to the ludus.
    /// Nothing here touches the campaign: no denarii, no new day.
    /// </summary>
    public class NetArena : MonoBehaviour
    {
        public static NetArena Instance { get; private set; }
        public const float BackToLudusSeconds = 7f;

        NetSession session;
        NetAvatars avatars;
        Fighter local;
        GameHUD hud;
        readonly HashSet<int> dead = new HashSet<int>();
        float lastHp = -1f, hpTimer, endAt = -1f;
        bool ended;

        public bool Ended => ended;
        public string Winner { get; private set; }
        public int HitsSent { get; private set; }
        public int HitsTaken { get; private set; }
        public bool IsDead(int id) => dead.Contains(id);

        /// <summary>Team id in the arena: the colour (teams) or the player (everyone for himself).</summary>
        public static int TeamOf(NetSession s, NetPlayer p) => s.ArenaTeams ? 100 + p.Color : 100 + p.Id;

        /// <summary>Where each player starts: around the sand, facing the centre.</summary>
        public static Vector3 SpawnFor(int slot, int count)
        {
            float a = (270f + 360f * slot / Mathf.Max(2, count)) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 7f + Vector3.up * 0.05f;
        }

        public void Init(Fighter localFighter, NetAvatars netAvatars, GameHUD gameHud)
        {
            Instance = this;
            session = NetSession.Instance;
            local = localFighter;
            avatars = netAvatars;
            hud = gameHud;
            session.GameMessage += OnGame;
            session.RosterChanged += OnRoster;
            Fighter.AnyHitResolved += OnHit;
            Fighter.AnyDied += OnDied;
            avatars.RemoteSpawned += _ => RefreshBars();
            string mode = session.ArenaTeams ? "POR EQUIPOS" : "TODOS CONTRA TODOS";
            hud?.ShowMessage("ARENA ONLINE  -  " + mode + (session.ArenaUpgrades ? "  -  CON MEJORAS" : "  -  SIN MEJORAS (TODOS IGUALES)"), 3.5f);
            Debug.Log($"[NETARENA] start teams={session.ArenaTeams} upgrades={session.ArenaUpgrades} me={session.LocalId} team={local.TeamId} hp={local.Health.Max:0}");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (session != null) { session.GameMessage -= OnGame; session.RosterChanged -= OnRoster; }
            Fighter.AnyHitResolved -= OnHit;
            Fighter.AnyDied -= OnDied;
        }

        /// <summary>Health bars on the right: the rivals (the other colours, or everybody).</summary>
        void RefreshBars()
        {
            if (hud == null || avatars == null) return;
            var rivals = new List<Fighter>();
            foreach (var r in avatars.Remotes) if (r != null && r.Fighter.TeamId != local.TeamId) rivals.Add(r.Fighter);
            hud.ResetEnemies(rivals);
        }

        // ------------------------------------------------------------------ blows
        void OnHit(HitResult r)
        {
            if (ended) return;
            // We hit someone's copy: their game applies it.
            if (r.Attacker == local && r.Target != null)
            {
                var rf = r.Target.GetComponent<RemoteFighter>();
                if (rf == null || dead.Contains(rf.PlayerId)) return;
                Vector3 d = r.Target.transform.position - local.transform.position; d.y = 0f;
                d = d.sqrMagnitude > 0.01f ? d.normalized : local.transform.forward;
                session.Send("HT", rf.PlayerId + "|" + NetSession.F(r.Target.LastRawDamage) + "|" + (int)r.Type + "|" + NetSession.F(d.x) + "|" + NetSession.F(d.z));
                HitsSent++;
            }
        }

        void OnDied(Fighter f)
        {
            if (f != local || dead.Contains(session.LocalId)) return;
            dead.Add(session.LocalId);
            int killer = local.LastAttacker != null && local.LastAttacker.GetComponent<RemoteFighter>() != null
                ? local.LastAttacker.GetComponent<RemoteFighter>().PlayerId : 0;
            session.Send("DIE", killer.ToString());
            SendHp(true);
            hud?.ShowMessage("HAS CAIDO" + (killer > 0 && session.Get(killer) != null ? "  -  TE VENCIO " + session.Get(killer).G.Name.ToUpperInvariant() : ""), 3f);
            Debug.Log("[NETARENA] I died, killer=" + killer);
            CheckEnd();
        }

        void OnGame(int from, string type, string payload)
        {
            string[] a = payload.Split('|');
            switch (type)
            {
                case "HT":
                {
                    if (a.Length < 5 || !int.TryParse(a[0], out int target) || target != session.LocalId || ended || !local.IsAlive) return;
                    var rf = avatars != null ? avatars.Remote(from) : null;
                    Fighter attacker = rf != null ? rf.Fighter : null;
                    Vector3 dir = new Vector3(NetSession.PF(a[3]), 0f, NetSession.PF(a[4]));
                    var type2 = (AttackType)Mathf.Clamp(int.TryParse(a[2], out int t) ? t : 0, 0, 2);
                    local.ReceiveHit(attacker, NetSession.PF(a[1]), type2, dir.sqrMagnitude > 0.01f ? dir.normalized : -local.transform.forward);
                    HitsTaken++;
                    SendHp(true);
                    break;
                }
                case "HP":
                {
                    if (a.Length < 2) return;
                    var rf = avatars != null ? avatars.Remote(from) : null;
                    if (rf != null) rf.Fighter.Health.SetNet(NetSession.PF(a[0]), NetSession.PF(a[1]));
                    break;
                }
                case "DIE":
                {
                    if (dead.Contains(from)) return;
                    dead.Add(from);
                    var rf = avatars != null ? avatars.Remote(from) : null;
                    if (rf != null && rf.Fighter.IsAlive) { rf.Fighter.Health.SetNet(1f, rf.Fighter.Health.Max); rf.Fighter.Health.TakeDamage(10f); }
                    var p = session.Get(from);
                    int.TryParse(payload, out int killer);
                    string who = p != null ? p.G.Name.ToUpperInvariant() : "UN GLADIADOR";
                    string by = killer == session.LocalId ? "  -  ¡LO HAS VENCIDO TU!" : killer > 0 && session.Get(killer) != null ? "  -  LO VENCIO " + session.Get(killer).G.Name.ToUpperInvariant() : "";
                    hud?.ShowMessage("¡" + who + " CAE!" + by, 2.5f);
                    Debug.Log("[NETARENA] player " + from + " died, killer=" + killer);
                    CheckEnd();
                    break;
                }
                case "AEND":
                    ShowEnd(payload);
                    break;
            }
        }

        void OnRoster() { if (!ended) CheckEnd(); } // somebody left: they are out

        void SendHp(bool force)
        {
            float hp = local.Health.Current;
            if (!force && Mathf.Approximately(hp, lastHp)) return;
            lastHp = hp;
            session.Send("HP", NetSession.F(hp) + "|" + NetSession.F(local.Health.Max));
        }

        // ------------------------------------------------------------------ the end (the host decides)
        void CheckEnd()
        {
            if (ended || session == null || !session.IsHost) return;
            var standing = new List<NetPlayer>();
            foreach (var p in session.Players) if (!dead.Contains(p.Id)) standing.Add(p);
            var sides = new HashSet<int>();
            foreach (var p in standing) sides.Add(TeamOf(session, p));
            if (sides.Count > 1) return;
            string text;
            if (standing.Count == 0) text = "EMPATE";
            else if (session.ArenaTeams) text = "GANA EL EQUIPO " + NetSession.ColorNames[standing[0].Color];
            else text = "GANA " + standing[0].G.Name.ToUpperInvariant();
            session.Send("AEND", text);
            ShowEnd(text);
        }

        void ShowEnd(string text)
        {
            if (ended) return;
            ended = true;
            Winner = text;
            endAt = Time.unscaledTime + BackToLudusSeconds;
            Debug.Log("[NETARENA] end: " + text);
            bool iWon = text.Contains(session.ArenaTeams && session.Local != null ? NetSession.ColorNames[session.Local.Color]
                                                                                   : (session.Local != null ? session.Local.G.Name.ToUpperInvariant() : "#"));
            hud?.ShowBanner(text, session.IsHost ? "VOLVEIS AL LUDUS EN UNOS SEGUNDOS" : "EL ANFITRION OS LLEVARA AL LUDUS",
                            iWon ? new Color(1f, 0.8f, 0.25f) : new Color(0.85f, 0.15f, 0.1f));
        }

        void Update()
        {
            if (session == null || !session.Active) return;
            hpTimer -= Time.deltaTime;
            if (hpTimer <= 0f) { hpTimer = 1f; SendHp(true); } // also tells late joiners
            else SendHp(false);
            if (ended && session.IsHost && endAt > 0f && Time.unscaledTime >= endAt) { endAt = -1f; session.StartGame(); }
        }
    }
}
