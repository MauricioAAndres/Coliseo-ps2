using System;
using System.Collections.Generic;
using Gladiator.Arena;
using Gladiator.Combat;
using Gladiator.Core;
using Gladiator.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Gladiator.Net
{
    /// <summary>
    /// Shared ludus: shows the other players' gladiators and sends ours.
    /// Each client owns its own gladiator. 20 times a second it sends where it is (position, speed, facing, guard /
    /// crouch / charge) and, as they happen, its actions (attacks, jumps, rolls, taunts) and what it carries.
    /// The others' copies are ordinary Fighters steered towards the received position and made to repeat the
    /// actions, so they animate exactly like a local gladiator. In the ludus nobody can hurt anybody.
    /// </summary>
    public class NetAvatars : MonoBehaviour
    {
        public static NetAvatars Instance { get; private set; }

        Fighter local;
        Camera cam;
        Func<int, Vector3> spawnFor;
        Action<string> say;
        readonly Dictionary<int, RemoteFighter> remotes = new Dictionary<int, RemoteFighter>();
        NetSession session;
        float sendTimer, profileTimer;
        Vector3 lastSentPos;
        float lastSentTime;
        string lastEquip = "", lastProfile;

        public int RemoteCount => remotes.Count;
        public IEnumerable<RemoteFighter> Remotes => remotes.Values;
        public Fighter Local => local;

        public void Init(Fighter localPlayer, Camera camera, Func<int, Vector3> spawn, Action<string> message)
        {
            Instance = this;
            local = localPlayer;
            cam = camera;
            spawnFor = spawn;
            say = message;
            lastSentPos = local.transform.position;
            lastSentTime = Time.time;
            Fighter.AnyAttackStarted += OnAttack;
            Fighter.AnyMove += OnMove;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Fighter.AnyAttackStarted -= OnAttack;
            Fighter.AnyMove -= OnMove;
            Unhook();
        }

        void Hook(NetSession s)
        {
            if (session == s) return;
            Unhook();
            session = s;
            if (session == null) return;
            session.GameMessage += OnGame;
            session.Notice += OnNotice;
            lastEquip = "";
            lastProfile = null;
        }

        void Unhook()
        {
            if (session == null) return;
            session.GameMessage -= OnGame;
            session.Notice -= OnNotice;
            session = null;
        }

        void OnNotice(string s) { say?.Invoke(s); }

        void Update()
        {
            var s = NetSession.Instance;
            Hook(s);
            if (s == null || !s.InGame || local == null)
            {
                if (remotes.Count > 0) ClearRemotes();
                return;
            }
            SyncRoster(s);

            sendTimer -= Time.deltaTime;
            if (sendTimer <= 0f)
            {
                sendTimer = 0.05f;
                SendState(s);
            }

            string eq = EquipString(local);
            if (eq != lastEquip) { lastEquip = eq; s.Send("EQ", eq); }

            profileTimer -= Time.unscaledDeltaTime;
            if (profileTimer <= 0f)
            {
                profileTimer = 0.5f;
                string sig = NetGladiator.FromProfile(GameManager.Ensure().Profile).LookSignature;
                if (lastProfile != null && sig != lastProfile) s.UpdateProfile();
                lastProfile = sig;
            }
        }

        // ------------------------------------------------------------------ roster -> avatars
        void SyncRoster(NetSession s)
        {
            List<int> gone = null;
            foreach (var kv in remotes)
                if (s.Get(kv.Key) == null || kv.Value == null) (gone ??= new List<int>()).Add(kv.Key);
            if (gone != null)
                foreach (int id in gone)
                {
                    if (remotes[id] != null) Destroy(remotes[id].gameObject);
                    remotes.Remove(id);
                }

            foreach (var p in s.Players)
            {
                if (p.Id == s.LocalId || p.G == null) continue;
                if (!remotes.TryGetValue(p.Id, out var r)) { remotes[p.Id] = Spawn(p, s); continue; }
                if (r.LookSignature != p.G.LookSignature)
                {
                    r.LookSignature = p.G.LookSignature;
                    CharacterFactory.RebuildBody(r.Fighter, p.G.Appearance(), p.G.ArmorSet);
                    r.Fighter.DisplayName = p.G.Name;
                    r.SetName(p.G.Name);
                }
            }
        }

        RemoteFighter Spawn(NetPlayer p, NetSession s)
        {
            Vector3 pos = spawnFor != null ? spawnFor(s.SlotOf(p.Id)) : Vector3.zero;
            var f = CharacterFactory.Create(p.G.Name, p.G.Appearance(), pos, Quaternion.identity, local != null ? local.TeamId : 0, p.G.ArmorSet);
            f.Harmless = true;            // the ludus: nobody gets hurt
            f.DisarmResistance = 1f;
            f.Equip(WeaponFactory.Create(WeaponLibrary.Rudis));
            var r = f.gameObject.AddComponent<RemoteFighter>();
            r.Init(p.Id, f, cam, p.G.Name, p.G.Appearance().ShieldTint);
            r.LookSignature = p.G.LookSignature;
            Debug.Log("[NET] avatar for player " + p.Id + " " + p.G.Name + " at " + pos);
            return r;
        }

        void ClearRemotes()
        {
            foreach (var r in remotes.Values) if (r != null) Destroy(r.gameObject);
            remotes.Clear();
        }

        // ------------------------------------------------------------------ outgoing
        void SendState(NetSession s)
        {
            Vector3 p = local.transform.position;
            float dt = Mathf.Max(0.01f, Time.time - lastSentTime);
            Vector3 v = (p - lastSentPos) / dt;
            v.y = 0f;
            if (v.magnitude > 12f) v = Vector3.zero; // teleports (minigames) are not speed
            lastSentPos = p;
            lastSentTime = Time.time;
            int flags = (local.State == FighterState.Blocking ? 1 : 0) | (local.Crouching ? 2 : 0) |
                        (local.MeleeCharging || local.Charging ? 4 : 0);
            s.Send("ST", NetSession.F(p.x) + "|" + NetSession.F(p.y) + "|" + NetSession.F(p.z) + "|" +
                         NetSession.F(local.transform.eulerAngles.y) + "|" + NetSession.F(v.x) + "|" + NetSession.F(v.z) + "|" + flags);
        }

        string PosYaw()
        {
            Vector3 p = local.transform.position;
            return NetSession.F(p.x) + "|" + NetSession.F(p.y) + "|" + NetSession.F(p.z) + "|" + NetSession.F(local.transform.eulerAngles.y);
        }

        void OnAttack(Fighter f, AttackType type)
        {
            if (f != local || NetSession.Instance == null || !NetSession.Instance.InGame) return;
            NetSession.Instance.Send("AT", (int)type + "|" + PosYaw());
        }

        void OnMove(Fighter f, string kind)
        {
            if (f != local || NetSession.Instance == null || !NetSession.Instance.InGame) return;
            switch (kind)
            {
                case "jump": case "vault": case "climb": case "dodge": case "dodge_back": case "taunt": case "cheer":
                    NetSession.Instance.Send("MV", kind + "|" + local.TauntKind + "|" + PosYaw());
                    break;
            }
        }

        static string EquipString(Fighter f)
        {
            string main = f.CurrentWeapon != null ? f.CurrentWeapon.Stats.Id : "";
            string off = f.OffWeapon != null ? f.OffWeapon.Stats.Id : "";
            string sh = f.EquippedShield != null ? f.EquippedShield.Stats.Id : "";
            return main + "|" + off + "|" + sh;
        }

        // ------------------------------------------------------------------ incoming
        void OnGame(int from, string type, string payload)
        {
            if (!remotes.TryGetValue(from, out var r) || r == null) return;
            string[] a = payload.Split('|');
            switch (type)
            {
                case "ST":
                    if (a.Length < 7) return;
                    r.OnState(new Vector3(NetSession.PF(a[0]), NetSession.PF(a[1]), NetSession.PF(a[2])), NetSession.PF(a[3]),
                              new Vector3(NetSession.PF(a[4]), 0f, NetSession.PF(a[5])), int.TryParse(a[6], out int fl) ? fl : 0);
                    break;
                case "AT":
                    if (a.Length < 5) return;
                    r.OnAttack((AttackType)Mathf.Clamp(int.TryParse(a[0], out int t) ? t : 0, 0, 2),
                               new Vector3(NetSession.PF(a[1]), NetSession.PF(a[2]), NetSession.PF(a[3])), NetSession.PF(a[4]));
                    break;
                case "MV":
                    if (a.Length < 6) return;
                    r.OnMove(a[0], int.TryParse(a[1], out int k) ? k : 0,
                             new Vector3(NetSession.PF(a[2]), NetSession.PF(a[3]), NetSession.PF(a[4])), NetSession.PF(a[5]));
                    break;
                case "EQ":
                    if (a.Length < 3) return;
                    r.OnEquip(a[0], a[1], a[2]);
                    break;
            }
        }
    }

    /// <summary>Another player's gladiator: follows the network state and repeats their actions.</summary>
    public class RemoteFighter : MonoBehaviour
    {
        public int PlayerId { get; private set; }
        public Fighter Fighter { get; private set; }
        public string LookSignature;
        /// <summary>Autotest: furthest it moved and the states it went through.</summary>
        public float Travelled { get; private set; }
        public readonly HashSet<FighterState> StatesSeen = new HashSet<FighterState>();
        public int StatesReceived { get; private set; }

        Vector3 netPos, netVel;
        float netYaw, recvTime;
        int flags;
        bool hasState;
        Color shieldTint;
        Camera cam;
        Transform tag;
        Text tagText;
        Vector3 startPos;

        public void Init(int id, Fighter f, Camera camera, string name, Color shield)
        {
            PlayerId = id;
            Fighter = f;
            cam = camera;
            shieldTint = shield;
            startPos = f.transform.position;
            BuildTag(name);
        }

        public void SetName(string n) { if (tagText != null) tagText.text = n.ToUpperInvariant(); }

        void BuildTag(string name)
        {
            var go = new GameObject("NameTag", typeof(RectTransform));
            tag = go.transform;
            tag.SetParent(transform, false);
            float h = Fighter.Controller != null ? Fighter.Controller.height : 1.8f;
            tag.localPosition = Vector3.up * (h + 0.45f);
            tag.localScale = Vector3.one * 0.01f;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)tag).sizeDelta = new Vector2(400, 50);
            tagText = PS2UI.Text(tag, name.ToUpperInvariant(), 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 50), PS2UI.Gold);
        }

        public void OnState(Vector3 pos, float yaw, Vector3 vel, int fl)
        {
            netPos = pos; netYaw = yaw; netVel = vel; flags = fl;
            recvTime = Time.time;
            StatesReceived++;
            if (!hasState) { hasState = true; Teleport(pos, yaw); }
        }

        public void OnAttack(AttackType type, Vector3 pos, float yaw)
        {
            SnapIfFar(pos, yaw, 0.6f);
            Fighter.SetFacing(Quaternion.Euler(0, yaw, 0) * Vector3.forward);
            Fighter.TryAttack(type);
        }

        public void OnMove(string kind, int tauntKind, Vector3 pos, float yaw)
        {
            SnapIfFar(pos, yaw, 0.35f);
            Vector3 fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            switch (kind)
            {
                case "jump": Fighter.TryJump(netVel.sqrMagnitude > 0.5f ? netVel : Vector3.zero); break;
                case "vault": case "climb": Fighter.TryJump(fwd); break;
                case "dodge": Fighter.TryDodge(fwd); break;
                case "dodge_back": Fighter.TryDodge(Vector3.zero); break;
                case "taunt": case "cheer": Fighter.TryTaunt(tauntKind); break;
            }
        }

        public void OnEquip(string main, string off, string shield)
        {
            Fighter.UnequipAll();
            var m = WeaponLibrary.ByName(main);
            if (m != null) Fighter.Equip(WeaponFactory.Create(m));
            var o = WeaponLibrary.ByName(off);
            if (o != null) Fighter.EquipOff(WeaponFactory.Create(o));
            var s = ShieldLibrary.ById(shield);
            if (s != null) Shield.Attach(Fighter, s, shieldTint);
        }

        void SnapIfFar(Vector3 pos, float yaw, float tolerance)
        {
            if ((pos - transform.position).magnitude > tolerance) Teleport(pos, yaw);
            else transform.rotation = Quaternion.Euler(0, yaw, 0);
        }

        void Teleport(Vector3 pos, float yaw)
        {
            var cc = Fighter.Controller;
            bool was = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            if (cc != null && was) cc.enabled = true;
            Fighter.SetFacing(transform.forward);
        }

        void Update()
        {
            if (Fighter == null || !hasState) return;
            StatesSeen.Add(Fighter.State);
            Travelled = Mathf.Max(Travelled, (transform.position - startPos).magnitude);
            float age = Mathf.Min(Time.time - recvTime, 0.25f);
            Vector3 target = netPos + netVel * age;
            Vector3 err = target - transform.position;
            var st = Fighter.State;
            bool scripted = st == FighterState.Traverse || st == FighterState.Dodge || st == FighterState.Airborne;
            if (!scripted && (err.magnitude > 2.5f || (Time.time - recvTime > 1f && err.magnitude > 0.3f)))
            {
                Teleport(target, netYaw);
                err = Vector3.zero;
            }
            Vector3 flat = new Vector3(err.x, 0f, err.z);
            float speed = Mathf.Max(0.5f, Fighter.MoveSpeed * Fighter.EquipMoveMul * Fighter.ArmorMoveMul);
            Vector3 want = netVel + flat * 3f;
            if (want.magnitude < 0.2f) want = Vector3.zero;
            Fighter.Move(want / speed);
            Fighter.SetFacing(Quaternion.Euler(0, netYaw, 0) * Vector3.forward);
            Fighter.SetBlock((flags & 1) != 0);
            Fighter.SetCrouch((flags & 2) != 0);
            Fighter.SetCharge((flags & 4) != 0);
        }

        void LateUpdate()
        {
            if (tag == null) return;
            var c = cam != null ? cam : Camera.main;
            if (c != null) tag.rotation = c.transform.rotation;
        }
    }
}
