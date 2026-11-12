using System.Collections.Generic;
using Gladiator.Arena;
using Gladiator.Character;
using Gladiator.Combat;
using Gladiator.Core;
using Gladiator.Progression;
using Gladiator.PS2Style;
using Gladiator.UI;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>Somebody who keeps a public building (the bath attendant, the scribe): stands at his place, looks at
    /// the player when he is near. Cannot be hurt.</summary>
    public class ServiceNPC : MonoBehaviour
    {
        public Fighter Fighter { get; private set; }
        public string Name { get; private set; }
        Vector3 face;

        public static ServiceNPC Spawn(string name, Pose at, int seed, int outfit, int cloth, float fat, bool female = false)
        {
            var look = CharacterAppearance.Random(new System.Random(seed), true);
            look.Name = name;
            look.Armor = 0; look.Helmet = false;
            look.Outfit = outfit; look.ClothColor = cloth; look.Shirt = outfit == 2 ? 3 : 1;
            look.Fat = fat; look.Muscle = 0.35f; look.Femininity = female ? 0.8f : 0f;
            if (female) look.Beard = 0;
            var f = CharacterFactory.Create(name, look, at.position, at.rotation, CitizenSpawner.CitizenTeam);
            f.Harmless = true;
            f.DisarmResistance = 1f;
            f.Health.SetMax(1000000f, true);
            var n = f.gameObject.AddComponent<ServiceNPC>();
            n.Fighter = f; n.Name = name; n.face = at.rotation * Vector3.forward;
            return n;
        }

        void Update()
        {
            if (Fighter == null || !Fighter.IsAlive) return;
            Fighter.Move(Vector3.zero);
            var player = TownBootstrap.Instance != null ? TownBootstrap.Instance.Player : null;
            Vector3 to = player != null ? player.transform.position - transform.position : Vector3.zero; to.y = 0f;
            Fighter.SetFacing(to.sqrMagnitude > 0.01f && to.magnitude < 5f ? to : face);
        }
    }

    public enum ErrandKind { None, Parcel, Message, Drunk }

    /// <summary>One of the day's errands on the basilica's board.</summary>
    public class Errand
    {
        public ErrandKind Kind;
        public int Reward;
        public int LotId = -1, CitizenId = -1;
        public string Title, Detail;
        public bool Done;
        public float Hours = 3f;
    }

    /// <summary>
    /// The two public buildings on the plaza.
    /// The baths: for a few denarii you sit in the pool for an hour; half your health comes back and your next fight
    /// in the arena starts with a little more health and speed (<see cref="Boons"/>).
    /// The basilica: the scribe behind the counter gives out the day's errands (take a parcel to a house, a message to
    /// somebody, throw the drunk out of the tavern: paid work outside the arena, at any hour), takes a wager on your
    /// next fight and collects the fines that clear the watch's warnings.
    /// </summary>
    public class TownServices : MonoBehaviour
    {
        public const int FinePerWarning = 20;
        public static readonly int[] Stakes = { 25, 50, 100 };
        public const float BathSeconds = 4f, BathHours = 1f;

        public HouseInterior Baths { get; private set; }
        public HouseInterior Forum { get; private set; }
        public ServiceNPC BathKeeper { get; private set; }
        public ServiceNPC Scribe { get; private set; }
        public bool Bathing { get; private set; }
        public int BathsTaken { get; private set; }
        /// <summary>What the prompt line says when nothing else does: the errand in hand.</summary>
        public string IdlePrompt { get; private set; } = "";
        /// <summary>How the last errand ended: "done", "late", "beaten", "dropped" (tests).</summary>
        public string LastErrandOutcome { get; private set; } = "";
        public Fighter Drunk { get; private set; }

        // The errands survive a trip to the arena (the town is rebuilt).
        static int offersDay = -1, offersSeed;
        static readonly List<Errand> offers = new List<Errand>();
        static Errand active;
        static float deadline; // day * 24 + hour
        public static IReadOnlyList<Errand> Offers => offers;
        public static Errand Active => active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { offersDay = -1; offers.Clear(); active = null; }

        TownBootstrap town;
        TownManager manager;
        PlayerController pc;
        Fighter player;
        float bathLeft;
        GameObject beacon;
        float drunkGone = -1f;
        bool drunkDown;

        public void Init(TownBootstrap t, TownManager m, PlayerController controller)
        {
            town = t; manager = m; pc = controller; player = t.Player;
            foreach (var h in t.Houses)
            {
                if (h.Service == "baths") Baths = h;
                else if (h.Service == "forum") Forum = h;
            }
            int seed = t.Layout.Seed;
            if (Baths != null && Baths.KeeperSpot.HasValue) BathKeeper = ServiceNPC.Spawn("Balneator Crispus", Baths.KeeperSpot.Value, seed * 29 + 3, 1, 0, 0.45f);
            if (Forum != null && Forum.KeeperSpot.HasValue) Scribe = ServiceNPC.Spawn("Escriba Tiro", Forum.KeeperSpot.Value, seed * 31 + 7, 2, 7, 0.2f);
            Debug.Log($"[TOWN] services: baths={(Baths != null)} forum={(Forum != null)}");
            if (Boons.LastWagerResult != 0)
            {
                int r = Boons.LastWagerResult;
                Boons.LastWagerResult = 0;
                StartCoroutine(Later(3.5f, () => Say(r > 0 ? "APUESTA GANADA: COBRAS " + (r * 2) + " DENARIOS" : "APUESTA PERDIDA: " + (-r) + " DENARIOS MENOS", 5f)));
            }
            if (active != null) Resume();
        }

        System.Collections.IEnumerator Later(float s, System.Action a) { yield return new WaitForSeconds(s); a(); }

        void Say(string s, float seconds = 4f)
        {
            if (town != null && town.Hud != null) town.Hud.ShowMessage(s, seconds);
            Debug.Log("[SERVICES] " + s);
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
        static float Now { get { var s = TownSimulation.Instance; return s != null ? s.Day * 24f + s.Hour : 0f; } }
        static bool Guest { get { var n = Gladiator.Net.NetSession.Instance; return n != null && n.InGame && !n.IsHost; } }
        PlayerProfile Profile => GameManager.Instance != null ? GameManager.Instance.Profile : null;

        // ================================================================== what TownManager asks every frame
        /// <summary>True when the player is at one of the services: it owns the prompt and the E key this frame.</summary>
        public bool Handle(Vector3 p, string key)
        {
            if (town == null || player == null) return false;
            if (Bathing) { UpdateBath(); return true; }
            if (player.State != FighterState.Idle) return false;
            if (Baths != null && Baths.Contains(p))
            {
                bool atPool = Flat(Baths.PoolCenter - p) < Baths.PoolRadius;
                bool atKeeper = BathKeeper != null && Flat(BathKeeper.transform.position - p) < 2.4f;
                if (atPool || atKeeper)
                {
                    town.Hud.SetPrompt(key + "BAÑARSE: " + Boons.BathPrice + " DENARIOS   (cura " + Mathf.RoundToInt(Boons.BathHeal * 100f) + "% y te prepara para la arena)");
                    if (GameInput.InteractPressed) TryBath();
                    return true;
                }
            }
            if (Forum != null && Forum.Contains(p) && (Flat(Forum.CounterFront - p) < 2.0f || Flat(Forum.BoardSpot - p) < 1.6f))
            {
                town.Hud.SetPrompt(key + "ESCRIBA: ENCARGOS, APUESTAS Y MULTAS");
                if (GameInput.InteractPressed) OpenScribe();
                return true;
            }
            return false;
        }

        // ================================================================== the baths
        /// <summary>Pay and get into the pool (public for tests).</summary>
        public bool TryBath()
        {
            if (Bathing || Baths == null || Baths.PoolSpots.Count == 0) return false;
            var prof = Profile;
            if (prof == null) return false;
            if (prof.Denarii < Boons.BathPrice)
            {
                Say("EL BAÑO CUESTA " + Boons.BathPrice + " DENARIOS Y TIENES " + prof.Denarii, 3f);
                return false;
            }
            // a free place in the water
            Pose seat = Baths.PoolSpots[0];
            foreach (var s in Baths.PoolSpots)
            {
                bool taken = false;
                foreach (var o in Fighter.All)
                    if (o != null && o != player && o.OnFurniture && (o.FurnitureAt.position - s.position).sqrMagnitude < 0.2f) { taken = true; break; }
                if (!taken) { seat = s; break; }
            }
            if (!player.TryActivityAt(Fighter.ActSitGround, seat, Baths.PoolStand)) return false;
            prof.Spend(Boons.BathPrice);
            Bathing = true;
            bathLeft = BathSeconds;
            if (pc != null) pc.enabled = false;
            var sim = TownSimulation.Instance;
            if (sim != null && !Guest) sim.ClockRate = BathHours * 60f / BathSeconds; // an hour goes by in the water
            Gladiator.Audio.AudioManager.UI("ui_select");
            Debug.Log("[SERVICES] bathing");
            return true;
        }

        void UpdateBath()
        {
            float dt = Mathf.Min(Time.deltaTime, bathLeft);
            bathLeft -= Time.deltaTime;
            player.Health.Heal(player.Health.Max * Boons.BathHeal * dt / BathSeconds);
            var sim = TownSimulation.Instance;
            town.Hud.SetPrompt("");
            town.Hud.SetSleep(true, "EN LAS TERMAS...   " + (sim != null ? TownSimulation.Clock(sim.Hour) : ""));
            if (bathLeft <= 0f || !player.InActivity) EndBath();
        }

        void EndBath()
        {
            Bathing = false;
            BathsTaken++;
            var sim = TownSimulation.Instance;
            if (sim != null) sim.ClockRate = 1f;
            town.Hud.SetSleep(false);
            if (player.InActivity) player.StopActivity();
            if (pc != null) pc.enabled = true;
            Boons.Bathed = true;
            Say("BAÑO: +" + Mathf.RoundToInt((Boons.BathHealth - 1f) * 100f) + "% VIDA Y +" + Mathf.RoundToInt((Boons.BathSpeed - 1f) * 100f) + "% VELOCIDAD EN TU PROXIMO COMBATE", 6f);
        }

        // ================================================================== the scribe's counter
        public void OpenScribe()
        {
            var gm = GameManager.Instance;
            var guards = town.Guards;
            int warnings = guards != null ? guards.Warnings : 0;
            int fine = warnings * FinePerWarning;
            EnsureOffers();
            int open = 0; foreach (var o in offers) if (!o.Done) open++;
            var opts = new List<ChoiceMenu.Option>
            {
                new ChoiceMenu.Option
                {
                    Label = active != null ? "ENCARGO EN CURSO" : "ENCARGOS DE HOY (" + open + ")",
                    Blurb = active != null ? active.Detail + "   Queda " + Left() + "." : "Trabajo pagado fuera de la arena, a cualquier hora. Uno cada vez.",
                    OnChoose = OpenErrands,
                },
                new ChoiceMenu.Option
                {
                    Label = Boons.Wager > 0 ? "APUESTA HECHA: " + Boons.Wager + " DENARIOS" : "APOSTAR A TU PROXIMO COMBATE",
                    Blurb = Boons.Wager > 0 ? "Si ganas tu proximo combate en la arena cobras " + (Boons.Wager * 2) + ". Si pierdes, nada."
                                            : "Apuestas a que ganas tu proximo combate en la arena: si ganas, cobras el doble.",
                    Enabled = Boons.Wager == 0,
                    OnChoose = OpenWagers,
                },
                new ChoiceMenu.Option
                {
                    Label = warnings > 0 ? "PAGAR LA MULTA: " + fine + " DENARIOS" : "MULTAS: NO DEBES NADA",
                    Blurb = warnings > 0 ? "La guardia te lleva " + warnings + (warnings == 1 ? " aviso" : " avisos") + ". Pagando quedas limpio (" + FinePerWarning + " por aviso)."
                                         : "La guardia no tiene nada contra ti.",
                    Enabled = warnings > 0,
                    OnChoose = () => PayFine(),
                },
                new ChoiceMenu.Option { Label = "NADA MAS", Blurb = "", OnChoose = () => { } },
            };
            ChoiceMenu.Ensure().Open("ESCRIBA TIRO   (" + (gm != null ? gm.Profile.Denarii : 0) + " denarios)", opts);
            manager.MenuOpenedByService();
        }

        /// <summary>Clears the watch's warnings for a fine (public for tests).</summary>
        public bool PayFine()
        {
            var guards = town.Guards;
            var prof = Profile;
            if (guards == null || prof == null || guards.Warnings <= 0) return false;
            int fine = guards.Warnings * FinePerWarning;
            if (!prof.Spend(fine)) { Say("LA MULTA SON " + fine + " DENARIOS Y TIENES " + prof.Denarii, 3f); return false; }
            guards.ClearWarnings();
            Say("MULTA PAGADA: " + fine + " DENARIOS.  QUEDAS LIMPIO", 4f);
            return true;
        }

        void OpenWagers()
        {
            var prof = Profile;
            var opts = new List<ChoiceMenu.Option>();
            foreach (int st in Stakes)
            {
                int stake = st;
                opts.Add(new ChoiceMenu.Option
                {
                    Label = stake + " DENARIOS",
                    Blurb = "Si ganas cobras " + (stake * 2) + ".  Tienes " + (prof != null ? prof.Denarii : 0) + ".",
                    Enabled = prof != null && prof.Denarii >= stake,
                    OnChoose = () => PlaceWager(stake),
                });
            }
            opts.Add(new ChoiceMenu.Option { Label = "MEJOR NO", Blurb = "", OnChoose = () => { } });
            ChoiceMenu.Ensure().Open("¿CUANTO APUESTAS A TU VICTORIA?", opts);
            manager.MenuOpenedByService();
        }

        /// <summary>Stake denarii on winning the next arena fight (public for tests).</summary>
        public bool PlaceWager(int stake)
        {
            var prof = Profile;
            if (prof == null || Boons.Wager > 0 || stake <= 0 || !prof.Spend(stake)) return false;
            Boons.Wager = stake;
            Say("APUESTA ANOTADA: " + stake + " DENARIOS A TU VICTORIA", 4.5f);
            return true;
        }

        // ================================================================== errands
        /// <summary>Three errands a day, the same for the whole day.</summary>
        public void EnsureOffers()
        {
            var sim = TownSimulation.Instance;
            int day = sim != null ? sim.Day : 1;
            if (offersDay == day && offersSeed == town.Layout.Seed && offers.Count > 0) return;
            offersDay = day; offersSeed = town.Layout.Seed;
            offers.Clear();
            var rng = new System.Random(town.Layout.Seed * 131 + day * 17);
            Vector3 from = Forum != null ? Forum.transform.position : town.Layout.Fountain;
            // a parcel to a house
            var homes = town.Houses.FindAll(h => h.Floor == 0 && h.Door != null && !h.Public && h.Lot != null && !h.Lot.ForSale && h.Rooms.Count > 0 && Flat(h.Door.Center - from) > 25f);
            if (homes.Count > 0)
            {
                var h = homes[rng.Next(homes.Count)];
                float dist = Flat(h.Door.Center - from);
                string where = sim != null ? sim.PlaceName("home" + h.Lot.Id).ToUpperInvariant() : "CASA " + h.Lot.Id;
                offers.Add(new Errand
                {
                    Kind = ErrandKind.Parcel, LotId = h.Lot.Id, Reward = Mathf.Clamp(Mathf.RoundToInt((20f + dist * 0.5f) / 5f) * 5, 25, 70),
                    Title = "LLEVAR UN PAQUETE A " + where, Detail = "Deja el paquete en la puerta de " + where + " (la marca una luz).", Hours = 3f,
                });
            }
            // a message for somebody (who is picked when you take it: whoever is out and about)
            offers.Add(new Errand { Kind = ErrandKind.Message, Reward = 35, Title = "LLEVAR UN RECADO", Detail = "Entrega un recado en mano.", Hours = 3f });
            // the drunk at the tavern
            if (TavernFront(out _))
                offers.Add(new Errand
                {
                    Kind = ErrandKind.Drunk, Reward = 80, Hours = 2f,
                    Title = "ECHAR AL BORRACHO DE LA TABERNA", Detail = "Un borracho con un palo molesta en la puerta de la taberna: tumbalo a golpes.",
                });
        }

        bool TavernFront(out Vector3 at)
        {
            at = Vector3.zero;
            var lot = town.Layout.Lots.Find(l => l.Kind == BuildingKind.Tavern && !l.Hidden);
            if (lot == null) return false;
            Vector3 want = lot.Center + lot.Front * (lot.Depth * 0.5f + 3.5f);
            if (!TownNavMesh.Snap(want, out at, 5f)) at = want;
            return true;
        }

        string Left()
        {
            float h = Mathf.Max(0f, deadline - Now);
            int m = Mathf.RoundToInt(h * 60f);
            return (m / 60) + " h " + (m % 60).ToString("00") + " min";
        }

        void OpenErrands()
        {
            EnsureOffers();
            var opts = new List<ChoiceMenu.Option>();
            if (active != null)
            {
                opts.Add(new ChoiceMenu.Option { Label = "SEGUIR CON EL", Blurb = active.Detail + "   Queda " + Left() + ".", OnChoose = () => { } });
                opts.Add(new ChoiceMenu.Option { Label = "DEJAR EL ENCARGO", Blurb = "Lo devuelves sin cobrar. Hoy ya no te lo dan otra vez.", OnChoose = () => Fail("dropped", "ENCARGO DEVUELTO") });
            }
            else
            {
                foreach (var e in offers)
                {
                    var offer = e;
                    opts.Add(new ChoiceMenu.Option
                    {
                        Label = (offer.Done ? "HECHO: " : "") + offer.Title + "   (" + offer.Reward + ")",
                        Blurb = offer.Done ? "Ya esta hecho por hoy. Mañana habra mas." : offer.Detail + "  Paga: " + offer.Reward + " denarios.  Plazo: " + Mathf.RoundToInt(offer.Hours) + " horas.",
                        Enabled = !offer.Done,
                        OnChoose = () => Accept(offer),
                    });
                }
                opts.Add(new ChoiceMenu.Option { Label = "AHORA NO", Blurb = "", OnChoose = () => { } });
            }
            ChoiceMenu.Ensure().Open("ENCARGOS DE HOY", opts);
            manager.MenuOpenedByService();
        }

        /// <summary>Takes an errand from the board (public for tests).</summary>
        public bool Accept(Errand e)
        {
            if (e == null || e.Done || active != null) return false;
            var sim = TownSimulation.Instance;
            if (e.Kind == ErrandKind.Message)
            {
                var c = PickRecipient();
                if (c == null) { Say("NO HAY NADIE EN LA CALLE A ESTA HORA: VUELVE DE DIA", 4.5f); return false; }
                e.CitizenId = c.Id;
                e.Title = "RECADO PARA " + c.Name.ToUpperInvariant();
                e.Detail = "Busca a " + c.Name + " (" + c.JobName + ") y hablale: lo marca una luz.";
            }
            active = e;
            deadline = Now + e.Hours;
            Resume();
            Say("ACEPTADO: " + e.Title + "  (" + Mathf.RoundToInt(e.Hours) + " h)", 5f);
            Gladiator.Audio.AudioManager.UI("ui_select");
            return true;
        }

        /// <summary>Somebody grown who is out in the street now and for a while yet.</summary>
        CitizenData PickRecipient()
        {
            var sim = TownSimulation.Instance;
            if (sim == null) return null;
            Vector3 from = Forum != null ? Forum.transform.position : town.Layout.Fountain;
            CitizenData best = null; float bd = 0f;
            foreach (var c in sim.Citizens)
            {
                if (c.Job == Job.Guardia || c.Age < 16) continue;
                var a = sim.WhereIs(c, sim.Hour);
                var b = sim.WhereIs(c, sim.Hour + 1.5f);
                if (a.Indoors || a.Walking || b.Indoors || b.PlaceId != a.PlaceId) continue;
                float d = Flat(a.Position - from);
                // not next door, not across the whole town: the farthest within reach
                if (d > 12f && d < 90f && d > bd) { bd = d; best = c; }
                else if (best == null) best = c;
            }
            return best;
        }

        /// <summary>Hands the message over if its recipient is right here (E; public for tests).</summary>
        public bool TryDeliver()
        {
            if (active == null || active.Kind != ErrandKind.Message) return false;
            foreach (var n in town.Spawner.Active)
                if (n.Citizen != null && n.Citizen.Id == active.CitizenId && Flat(n.transform.position - player.transform.position) < 2.4f) { Complete(); return true; }
            return false;
        }

        // the errand's marks in the world (again after a trip to the arena)
        void Resume()
        {
            ClearMarks();
            if (active == null) return;
            if (active.Kind == ErrandKind.Drunk) SpawnDrunk();
            else MakeBeacon();
        }

        void MakeBeacon()
        {
            beacon = new GameObject("ErrandBeacon");
            beacon.transform.SetParent(transform, false);
            var mat = PS2Materials.Flat(new Color(1f, 0.85f, 0.2f), 0.2f);
            PS2Spawn.Box(beacon.transform, "Ray", new Vector3(0.14f, 9f, 0.14f), new Vector3(0, 7.5f, 0), mat);
            PS2Spawn.Box(beacon.transform, "Head", new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0, 3.0f, 0), mat, Quaternion.Euler(45f, 0f, 45f));
        }

        public Vector3? ErrandTarget()
        {
            if (active == null) return null;
            var sim = TownSimulation.Instance;
            if (active.Kind == ErrandKind.Parcel)
            {
                var h = town.HouseOfLot(active.LotId);
                return h != null && h.Door != null ? h.Door.Center + h.transform.forward * 0.8f : (Vector3?)null;
            }
            if (active.Kind == ErrandKind.Message && sim != null)
            {
                foreach (var n in town.Spawner.Active) if (n.Citizen != null && n.Citizen.Id == active.CitizenId) return n.transform.position;
                var c = sim.Citizens.Find(x => x.Id == active.CitizenId);
                return c != null ? sim.WhereIs(c, sim.Hour).Position : (Vector3?)null;
            }
            if (active.Kind == ErrandKind.Drunk && Drunk != null) return Drunk.transform.position;
            return null;
        }

        void SpawnDrunk()
        {
            if (!TavernFront(out Vector3 at)) return;
            var look = CharacterAppearance.Random(new System.Random(town.Layout.Seed * 7 + offersDay * 3 + 1), true);
            look.Name = "Borracho";
            look.Armor = 0; look.Helmet = false; look.Outfit = 1; look.ClothColor = 6; look.Shirt = 1;
            look.Fat = 0.55f; look.Muscle = 0.55f; look.Femininity = 0f;
            Drunk = CharacterFactory.Create("Bibulus el borracho", look, at + Vector3.up * 0.05f, Quaternion.Euler(0, 140f, 0), 9);
            Drunk.Health.SetMax(70f, true);
            Drunk.MinHealth = 1f;
            Drunk.DamageMultiplier = 0.55f;
            Drunk.AttackSpeedMultiplier = 0.85f;
            Drunk.MoveSpeed = 3.6f;
            Drunk.DisarmResistance = 1f;
            Drunk.Equip(WeaponFactory.Create(WeaponLibrary.Rudis)); // a stick he swings about
            var ai = Drunk.gameObject.AddComponent<EnemyAI>();
            ai.OnlyTarget = player;
            ai.TauntChance = 0.5f;
            ai.enabled = false;
            drunkDown = false;
            drunkGone = -1f;
        }

        void ClearMarks()
        {
            if (beacon != null) Destroy(beacon);
            beacon = null;
            if (Drunk != null && drunkGone < 0f) Destroy(Drunk.gameObject);
            if (drunkGone < 0f) Drunk = null;
        }

        void Complete()
        {
            var e = active;
            if (e == null) return;
            e.Done = true;
            active = null;
            LastErrandOutcome = "done";
            var prof = Profile;
            if (prof != null) prof.AddDenarii(e.Reward);
            ClearMarks();
            Say("ENCARGO CUMPLIDO: +" + e.Reward + " DENARIOS", 5f);
            Gladiator.Audio.AudioManager.UI("ui_select");
        }

        void Fail(string outcome, string message)
        {
            var e = active;
            if (e == null) return;
            e.Done = true; // not offered again today
            active = null;
            LastErrandOutcome = outcome;
            ClearMarks();
            Say(message, 4.5f);
        }

        void Update()
        {
            if (town == null || player == null) return;
            // the body of a drunk who was dealt with goes after a while
            if (drunkGone >= 0f && Time.time > drunkGone)
            {
                if (Drunk != null) Destroy(Drunk.gameObject);
                Drunk = null; drunkGone = -1f;
            }
            if (active == null) { IdlePrompt = ""; return; }
            IdlePrompt = active.Title + "   (queda " + Left() + ")";
            if (Now > deadline) { Fail("late", "SE ACABO EL PLAZO DEL ENCARGO: NO COBRAS"); return; }
            Vector3 p = player.transform.position;
            Vector3? target = ErrandTarget();
            if (beacon != null && target.HasValue)
            {
                beacon.transform.position = new Vector3(target.Value.x, 0f, target.Value.z);
                beacon.transform.Rotate(0f, 90f * Time.deltaTime, 0f);
            }
            switch (active.Kind)
            {
                case ErrandKind.Parcel:
                    if (target.HasValue && Flat(target.Value - p) < 2.2f) Complete();
                    break;
                case ErrandKind.Message:
                {
                    // hand it over: talk to them (E) with them in front of you
                    if (GameInput.InteractPressed && !Bathing) TryDeliver();
                    break;
                }
                case ErrandKind.Drunk:
                    UpdateDrunk(p);
                    break;
            }
        }

        void UpdateDrunk(Vector3 p)
        {
            if (Drunk == null) { SpawnDrunk(); if (Drunk == null) { Fail("late", "EL BORRACHO SE HA IDO SOLO"); } return; }
            var ai = Drunk.GetComponent<EnemyAI>();
            float d = Flat(Drunk.transform.position - p);
            bool near = d < 9f && !player.KnockedOut && !player.InActivity;
            if (ai != null && ai.enabled != near && !drunkDown) { ai.enabled = near; if (!near) { Drunk.Move(Vector3.zero); Drunk.SetBlock(false); } }
            if (!near && !drunkDown && Drunk.State == FighterState.Idle && Random.value < Time.deltaTime * 0.25f) Drunk.TryTaunt(Random.Range(0, 2));
            if (!drunkDown && Drunk.Health.Normalized <= 0.15f)
            {
                // down he goes: out cold on the street
                drunkDown = true;
                if (ai != null) ai.enabled = false;
                Vector3 dir = Drunk.transform.position - p; dir.y = 0f;
                Drunk.KnockDown(dir.sqrMagnitude > 0.01f ? dir.normalized : -Drunk.transform.forward, 600f);
                drunkGone = Time.time + 25f;
                Complete();
                return;
            }
            if (!drunkDown && player.Health.Normalized <= 0.25f)
            {
                if (ai != null) ai.enabled = false;
                Drunk.Move(Vector3.zero);
                drunkGone = Time.time + 8f;
                Fail("beaten", "EL BORRACHO TE HA DADO UNA PALIZA: NO COBRAS");
            }
        }

        void OnDestroy()
        {
            var sim = TownSimulation.Instance;
            if (Bathing && sim != null) sim.ClockRate = 1f;
        }
    }
}
