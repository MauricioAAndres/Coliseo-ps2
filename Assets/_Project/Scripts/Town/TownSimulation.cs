using System.Collections.Generic;
using System.Text;
using Gladiator.Core;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// The town as a living system, without a single NPC GameObject. Holds ~100 <see cref="CitizenData"/> (made once from
    /// the town seed: families in the houses, a job and a workplace each, a timetable "hour → activity → place"), runs
    /// the town clock (1 game minute per real second; every new game day starts at 7:00) and answers questions such as
    /// "where is the smith at 14:00?" or "who is in the plaza now?". Lives across scenes (ludus, town, arena), so time
    /// keeps passing wherever the player is. What citizens think of the player is saved.
    /// </summary>
    public class TownSimulation : MonoBehaviour
    {
        public static TownSimulation Instance { get; private set; }

        public const int Population = 100;
        /// <summary>Game minutes that pass per real second.</summary>
        public const float MinutesPerSecond = 1f;
        /// <summary>How fast people get across town in the simulation (metres per game minute).</summary>
        public const float WalkMetresPerMinute = 6f;
        public const float DayStartHour = 7f;
        const string RelKey = "Gladiator.TownRel.";

        public TownLayout Layout { get; private set; }
        public readonly List<CitizenData> Citizens = new List<CitizenData>();
        /// <summary>Hour of the day, 0..24.</summary>
        public float Hour { get; set; } = DayStartHour + 1f;
        /// <summary>How fast the clock runs (1 = normal; asleep, 60: an hour a second).</summary>
        [System.NonSerialized] public float ClockRate = 1f;

        static bool Guest { get { var n = Gladiator.Net.NetSession.Instance; return n != null && n.InGame && !n.IsHost; } }

        /// <summary>Time passes at once (a fight in the arena). Midnight starts a new day.</summary>
        public void AdvanceHours(float hours) { Hour += hours; Wrap(); }

        void Wrap()
        {
            while (Hour >= 24f)
            {
                Hour -= 24f;
                scheduleCache.Clear();
                var gm = GameManager.Instance;
                if (!Guest && gm != null && gm.DayCycle != null) { gm.DayCycle.NextDay(); Day = gm.DayCycle.Day; }
            }
        }
        public int Day { get; private set; } = 1;
        /// <summary>Fires when the hour (integer) changes.</summary>
        public event System.Action<int> HourChanged;

        readonly Dictionary<string, TownPlace> places = new Dictionary<string, TownPlace>();
        readonly Dictionary<BuildingKind, List<string>> workByKind = new Dictionary<BuildingKind, List<string>>();
        readonly Dictionary<long, List<ScheduleEntry>> scheduleCache = new Dictionary<long, List<ScheduleEntry>>();
        bool relDirty;
        float relSaveTimer;
        int lastHour = -1;

        // ------------------------------------------------------------------ lifetime
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();

        public static TownSimulation Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("TownSimulation");
            DontDestroyOnLoad(go);
            return go.AddComponent<TownSimulation>();
        }

        void Awake()
        {
            Instance = this;
            Generate(TownBootstrap.CurrentSeed());
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-townhour");
            if (i >= 0 && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float h)) Hour = Mathf.Repeat(h, 24f);
        }

        void OnApplicationQuit() { if (relDirty) SaveRelations(); }

        /// <summary>Online: walk the host's town (rebuilt only if the seed differs).</summary>
        public void EnsureSeed(int seed)
        {
            if (Layout != null && Layout.Seed == seed) return;
            Generate(seed);
        }

        Gladiator.Net.NetSession hooked;
        float clockSend;

        void OnNet(int from, string type, string payload)
        {
            if (type != "TC") return;
            var a = payload.Split('|');
            if (a.Length < 2) return;
            float h = Gladiator.Net.NetSession.PF(a[0]);
            if (int.TryParse(a[1], out int d) && d != Day)
            {
                Day = d; scheduleCache.Clear();
                var gmN = GameManager.Instance;
                if (gmN != null && gmN.DayCycle != null) gmN.DayCycle.SetDay(d);
            }
            Hour = Mathf.Repeat(h, 24f);
        }

        void Update()
        {
            // Online: the host keeps the town's clock for everybody.
            var net = Gladiator.Net.NetSession.Instance;
            if (net != hooked) { if (hooked != null) hooked.GameMessage -= OnNet; hooked = net; if (net != null) net.GameMessage += OnNet; }
            bool online = net != null && net.InGame;
            if (online && net.IsHost && (clockSend -= Time.unscaledDeltaTime) <= 0f)
            {
                clockSend = ClockRate > 1f ? 0.25f : 2f;
                net.Send("TC", Gladiator.Net.NetSession.F(Hour) + "|" + Day);
            }
            // A fight ends the game day: the town wakes up to a new morning (online, the host's day).
            var gm = GameManager.Instance;
            if (!(online && !net.IsHost) && gm != null && gm.DayCycle != null && gm.DayCycle.Day != Day)
            {
                Day = gm.DayCycle.Day;
                scheduleCache.Clear();
            }
            Hour += Time.deltaTime * MinutesPerSecond / 60f * ClockRate;
            Wrap();
            int hNow = Mathf.FloorToInt(Hour);
            if (hNow != lastHour) { lastHour = hNow; HourChanged?.Invoke(hNow); }
            if (relDirty && (relSaveTimer -= Time.unscaledDeltaTime) <= 0f) SaveRelations();
        }

        // ------------------------------------------------------------------ generation
        static readonly string[] MaleNames = { "Marcus", "Gaius", "Lucius", "Publius", "Quintus", "Titus", "Gnaeus", "Aulus", "Decimus",
            "Sextus", "Servius", "Tiberius", "Manius", "Spurius", "Numerius", "Vibius", "Appius", "Kaeso" };
        static readonly string[] FemaleNames = { "Julia", "Claudia", "Cornelia", "Livia", "Octavia", "Valeria", "Aurelia", "Flavia",
            "Antonia", "Tullia", "Caecilia", "Fabia", "Sabina", "Lucilla", "Marcia", "Domitia", "Priscilla", "Camilla" };
        static readonly string[] Families = { "Aemilius", "Cornelius", "Fabius", "Valerius", "Claudius", "Junius", "Licinius", "Sempronius",
            "Tullius", "Annius", "Calpurnius", "Domitius", "Flavius", "Petronius", "Sulpicius", "Vibius", "Marius", "Octavius",
            "Pompeius", "Sergius", "Terentius", "Vettius", "Caelius", "Didius", "Furius", "Herennius", "Lollius", "Minucius" };

        /// <summary>Builds the population from the town seed: same seed, same people, same houses, same jobs.</summary>
        public void Generate(int seed)
        {
            Layout = TownLayout.Generate(seed);
            places.Clear(); workByKind.Clear(); Citizens.Clear(); scheduleCache.Clear();
            foreach (var p in Layout.Places) places[p.Id] = p;
            foreach (var p in Layout.Places)
            {
                if (p.Kind != PlaceKind.Work) continue;
                var kind = Layout.Lots[p.LotId].Kind;
                if (!workByKind.TryGetValue(kind, out var list)) workByKind[kind] = list = new List<string>();
                list.Add(p.Id);
            }

            var rng = new System.Random(seed * 7919 + 17);
            // ---- the jobs the town needs (workplace + shift), filled by the adults first
            var jobs = new Queue<(Job job, string work, Shift shift)>();
            void Need(BuildingKind k, Job j, params Shift[] shifts)
            {
                if (!workByKind.TryGetValue(k, out var ws)) return;
                foreach (var w in ws) foreach (var s in shifts) jobs.Enqueue((j, w, s));
            }
            Need(BuildingKind.Smithy, Job.Herrero, Shift.Manana, Shift.Manana);
            Need(BuildingKind.Bakery, Job.Panadero, Shift.Noche, Shift.Noche);
            Need(BuildingKind.Tavern, Job.Tabernero, Shift.Tarde, Shift.Tarde);
            Need(BuildingKind.Temple, Job.Sacerdote, Shift.Manana, Shift.Manana);
            Need(BuildingKind.Pottery, Job.Alfarero, Shift.Manana, Shift.Manana);
            Need(BuildingKind.Baths, Job.Banero, Shift.Manana, Shift.Tarde);
            Need(BuildingKind.Forum, Job.Escriba, Shift.Manana, Shift.Manana, Shift.Manana);
            Need(BuildingKind.Shop, Job.Tendero, Shift.Manana);
            foreach (var p in Layout.Places) if (p.Kind == PlaceKind.Market) jobs.Enqueue((Job.Mercader, p.Id, Shift.Manana));
            // the town guard (16 men): the two gates round the clock, the plaza corners, the cross street and the forecourt
            var guardJobs = new List<(Job job, string work, Shift shift)>();
            foreach (var g in new[] { "arena_gate", "ludus_gate" })
                foreach (var sh in new[] { Shift.Manana, Shift.Tarde, Shift.Noche }) guardJobs.Add((Job.Guardia, g, sh));
            foreach (var g in new[] { "guard_plaza0", "guard_plaza1", "guard_plaza2", "guard_plaza3" }) guardJobs.Add((Job.Guardia, g, Shift.Manana));
            guardJobs.Add((Job.Guardia, "guard_plaza0", Shift.Tarde)); guardJobs.Add((Job.Guardia, "guard_plaza3", Shift.Tarde));
            guardJobs.Add((Job.Guardia, "guard_east", Shift.Manana)); guardJobs.Add((Job.Guardia, "guard_west", Shift.Tarde));
            guardJobs.Add((Job.Guardia, "guard_forum_l", Shift.Manana)); guardJobs.Add((Job.Guardia, "guard_forum_r", Shift.Tarde));
            foreach (var lot in Layout.Lots)
                if (lot.Kind == BuildingKind.Garden && !lot.Hidden) jobs.Enqueue((Job.Hortelano, "garden" + lot.Id, Shift.Manana));
            jobs.Enqueue((Job.Aguador, "fountain", Shift.Manana));
            // gardens are workplaces too (outdoors)
            foreach (var lot in Layout.Lots)
                if (lot.Kind == BuildingKind.Garden && !lot.Hidden)
                    places["garden" + lot.Id] = new TownPlace { Id = "garden" + lot.Id, Name = "Huerto", Kind = PlaceKind.Work, Position = lot.Center, LotId = lot.Id };

            // ---- families in the houses until the town has its population
            var homes = Layout.Places.FindAll(p => p.Kind == PlaceKind.Home);
            Shuffle(homes, rng);
            var familyNames = new List<string>(Families);
            Shuffle(familyNames, rng);
            int fam = 0;
            foreach (var home in homes)
            {
                if (Citizens.Count >= Population) break;
                bool couple = rng.NextDouble() < 0.85;
                bool largeHome = Layout.Lots[home.LotId].Large;
                // one bed per bedroom: a normal home sleeps a couple (double bed) or one person; a large home one more
                int size = couple ? (largeHome ? 3 : 2) : (largeHome ? 2 : 1);
                string nomen = familyNames[fam++ % familyNames.Count];
                for (int m = 0; m < size && Citizens.Count < Population; m++)
                {
                    var c = new CitizenData { Id = Citizens.Count, HomeId = home.Id, Family = nomen, LookSeed = rng.Next() };
                    // first two are adults, then children or an elder
                    if (m < 2) { c.Female = couple ? m == 1 : rng.NextDouble() < 0.5; c.Age = rng.Next(19, 56); }
                    else if (rng.NextDouble() < 0.7) { c.Female = rng.NextDouble() < 0.5; c.Age = rng.Next(6, 15); }
                    else { c.Female = rng.NextDouble() < 0.5; c.Age = rng.Next(61, 80); }
                    string first = c.Female ? FemaleNames[rng.Next(FemaleNames.Length)] : MaleNames[rng.Next(MaleNames.Length)];
                    if (c.Female && first == Feminine(nomen)) first = FemaleNames[(System.Array.IndexOf(FemaleNames, first) + 1) % FemaleNames.Length];
                    c.Name = c.Female ? first + " " + Feminine(nomen) : first + " " + nomen;

                    if (c.Age < 16) { c.Job = Job.Nino; c.Shift = Shift.Ninguno; }
                    else if (c.Age > 60) { c.Job = Job.Anciano; c.Shift = Shift.Ninguno; }
                    else if (!c.Female && guardJobs.Count > 0 && (m == 0 || rng.NextDouble() < 0.7))
                    {   // the guard first: only men
                        var j = guardJobs[0]; guardJobs.RemoveAt(0); c.Job = j.job; c.WorkId = j.work; c.Shift = j.shift;
                    }
                    else if (jobs.Count > 0 && (m == 0 || m == 1 || rng.NextDouble() < 0.6)) { var j = jobs.Dequeue(); c.Job = j.job; c.WorkId = j.work; c.Shift = j.shift; }
                    else { c.Job = Job.AmaDeCasa; c.Shift = Shift.Ninguno; }

                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 0.4f + (float)rng.NextDouble() * 0.6f;
                    c.SpotOffset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    c.Relation = Mathf.Clamp(rng.Next(-15, 26) + (c.Job == Job.Guardia ? 10 : 0) + (c.Job == Job.Nino ? 15 : 0), -100, 100);
                    Citizens.Add(c);
                }
            }
            LoadRelations(seed);
            foreach (var c in Citizens) c.Schedule = ScheduleFor(c, Day);
        }

        static string Feminine(string nomen) => nomen.EndsWith("ius") ? nomen.Substring(0, nomen.Length - 3) + "ia"
                                              : nomen.EndsWith("us") ? nomen.Substring(0, nomen.Length - 2) + "a" : nomen;

        static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        }

        // ------------------------------------------------------------------ timetables
        string First(BuildingKind k, string fallback) => workByKind.TryGetValue(k, out var l) && l.Count > 0 ? l[0] : fallback;
        string Tavern => First(BuildingKind.Tavern, "plaza");
        string Temple => First(BuildingKind.Temple, "plaza");
        string Baths => First(BuildingKind.Baths, "fountain");

        /// <summary>
        /// The timetable of a citizen for one day: a fixed skeleton per job and shift, and the free-time stops chosen
        /// from the day number (some days to the baths, others to the tavern or the market). Pure: same input, same rows.
        /// </summary>
        public List<ScheduleEntry> ScheduleFor(CitizenData c, int day)
        {
            long key = ((long)day << 16) | (uint)c.Id;
            if (scheduleCache.TryGetValue(key, out var cached)) return cached;
            var rng = new System.Random(c.Id * 92821 + day * 68903 + 11);
            float J() => ((float)rng.NextDouble() - 0.5f) * 0.8f; // +-24 min so the town does not move in lockstep
            string home = c.HomeId, work = c.WorkId;
            (Activity, string) Leisure()
            {
                switch (rng.Next(7))
                {
                    case 0: return (Activity.Mercado, "stall" + rng.Next(Mathf.Max(1, Layout.Stalls.Count)));
                    case 1: return (Activity.Rezar, Temple);
                    case 2: return (Activity.Banarse, Baths);
                    case 3: return (Activity.Taberna, Tavern);
                    case 4: return (Activity.Agua, "fountain");
                    default: return (Activity.Pasear, rng.NextDouble() < 0.6 ? "plaza" : "fountain");
                }
            }
            (Activity, string) Lunch()
            {
                double r = rng.NextDouble();
                return r < 0.4 ? (Activity.Comer, Tavern) : r < 0.7 ? (Activity.Mercado, "stall" + rng.Next(Mathf.Max(1, Layout.Stalls.Count))) : (Activity.Comer, home);
            }
            var s = new List<ScheduleEntry>();
            void Add(float h, Activity a, string p) => s.Add(new ScheduleEntry(Mathf.Clamp(h, 0f, 23.9f), a, p));
            void AddL(float h, (Activity a, string p) x) => Add(h, x.a, x.p);

            switch (c.Job)
            {
                case Job.Panadero: // bread before dawn
                    Add(0, Activity.Dormir, home); Add(3 + J() * 0.3f, Activity.Trabajar, work); AddL(11 + J(), Lunch());
                    AddL(12.5f + J(), Leisure()); Add(15 + J(), Activity.EnCasa, home); Add(19.5f + J(), Activity.Dormir, home);
                    break;
                case Job.Tabernero: // the tavern closes late
                    Add(0, Activity.Trabajar, work); Add(1 + J() * 0.3f, Activity.Dormir, home); Add(9 + J(), Activity.EnCasa, home);
                    Add(10 + J() * 0.5f, Activity.Mercado, "stall" + rng.Next(Mathf.Max(1, Layout.Stalls.Count))); Add(11.2f + J() * 0.3f, Activity.Trabajar, work);
                    break;
                case Job.AmaDeCasa:
                    Add(0, Activity.Dormir, home); Add(6.5f + J(), Activity.EnCasa, home); Add(8.5f + J(), Activity.Agua, "fountain");
                    Add(9.5f + J(), Activity.Mercado, "stall" + rng.Next(Mathf.Max(1, Layout.Stalls.Count))); Add(11 + J(), Activity.EnCasa, home);
                    AddL(15 + J(), Leisure()); Add(17.5f + J(), Activity.EnCasa, home); Add(22 + J(), Activity.Dormir, home);
                    break;
                case Job.Anciano:
                    Add(0, Activity.Dormir, home); Add(7.5f + J(), Activity.EnCasa, home); Add(9 + J(), Activity.Pasear, "plaza");
                    Add(11 + J(), Activity.Rezar, Temple); Add(12.5f + J(), Activity.EnCasa, home); Add(16 + J(), Activity.Pasear, rng.NextDouble() < 0.5 ? "plaza" : "fountain");
                    if (rng.NextDouble() < 0.5) Add(18 + J(), Activity.Taberna, Tavern); else Add(18 + J(), Activity.EnCasa, home);
                    Add(20.5f + J(), Activity.EnCasa, home); Add(21.5f + J(), Activity.Dormir, home);
                    break;
                case Job.Nino: // playing in the plaza
                    Add(0, Activity.Dormir, home); Add(7 + J(), Activity.EnCasa, home); Add(8.5f + J(), Activity.Pasear, rng.NextDouble() < 0.6 ? "plaza" : "fountain");
                    Add(12.5f + J(), Activity.EnCasa, home); Add(14 + J(), Activity.Pasear, "plaza"); Add(19 + J(), Activity.EnCasa, home);
                    Add(21 + J(), Activity.Dormir, home);
                    break;
                default:
                    if (c.Shift == Shift.Noche) // night watch
                    {
                        Add(0, Activity.Trabajar, work); Add(5 + J() * 0.3f, Activity.EnCasa, home); Add(6 + J() * 0.3f, Activity.Dormir, home);
                        Add(14 + J(), Activity.EnCasa, home); AddL(15.5f + J(), Leisure()); AddL(18 + J(), Lunch()); Add(21 + J() * 0.2f, Activity.Trabajar, work);
                    }
                    else if (c.Shift == Shift.Tarde)
                    {
                        Add(0, Activity.Dormir, home); Add(8 + J(), Activity.EnCasa, home); AddL(9.5f + J(), Leisure()); AddL(11.5f + J(), Lunch());
                        Add(13 + J() * 0.3f, Activity.Trabajar, work);
                        if (rng.NextDouble() < 0.5) Add(21 + J() * 0.3f, Activity.Taberna, Tavern); else Add(21 + J() * 0.3f, Activity.EnCasa, home);
                        Add(23 + J() * 0.3f, Activity.Dormir, home);
                    }
                    else // morning shift
                    {
                        Add(0, Activity.Dormir, home); Add(6.3f + J(), Activity.EnCasa, home); Add(7 + J() * 0.4f, Activity.Trabajar, work);
                        AddL(12 + J() * 0.5f, Lunch()); Add(13 + J() * 0.3f, Activity.Trabajar, work); AddL(15.5f + J(), Leisure());
                        if (rng.NextDouble() < 0.5) Add(18 + J(), Activity.Taberna, Tavern); else AddL(18 + J(), Leisure());
                        Add(20.5f + J(), Activity.EnCasa, home); Add(22.5f + J(), Activity.Dormir, home);
                    }
                    break;
            }
            s.Sort((a, b) => a.Start.CompareTo(b.Start));
            scheduleCache[key] = s;
            return s;
        }

        // ------------------------------------------------------------------ queries
        /// <summary>Where a citizen is (or is walking to) at an hour of today, without any GameObject.</summary>
        public CitizenLocation WhereIs(CitizenData c, float hour) => WhereIs(c, hour, Day);

        public CitizenLocation WhereIs(CitizenData c, float hour, int day)
        {
            hour = Mathf.Repeat(hour, 24f);
            var s = ScheduleFor(c, day);
            int i = 0;
            for (int k = 0; k < s.Count; k++) if (s[k].Start <= hour) i = k;
            var cur = s[i];
            // where they were before: the previous row (the first row of the day follows yesterday's last one)
            var prev = i > 0 ? s[i - 1] : ScheduleFor(c, day - 1)[ScheduleFor(c, day - 1).Count - 1];
            Vector3 to = SpotOf(c, cur), from = SpotOf(c, prev);
            float travelMin = prev.PlaceId == cur.PlaceId ? 0f : Vector3.Distance(from, to) / WalkMetresPerMinute;
            float elapsedMin = (hour - cur.Start) * 60f;
            var loc = new CitizenLocation { Activity = cur.Activity, PlaceId = cur.PlaceId };
            if (elapsedMin < travelMin)
            {
                loc.Walking = true;
                loc.FromPlaceId = prev.PlaceId;
                loc.Progress = Mathf.Clamp01(elapsedMin / travelMin);
                loc.Position = Vector3.Lerp(from, to, loc.Progress);
            }
            else
            {
                loc.Position = to;
                loc.Indoors = IsIndoors(cur);
            }
            return loc;
        }

        /// <summary>Indoors: at home, or working inside a building (gates, stalls, the fountain and gardens are outdoors).</summary>
        bool IsIndoors(ScheduleEntry e)
        {
            if (e.Activity == Activity.Dormir || e.Activity == Activity.EnCasa) return true;
            return places.TryGetValue(e.PlaceId, out var p) && p.Kind == PlaceKind.Work && !e.PlaceId.StartsWith("garden")
                   && (e.Activity == Activity.Trabajar || e.Activity == Activity.Comer || e.Activity == Activity.Taberna
                       || e.Activity == Activity.Rezar || e.Activity == Activity.Banarse);
        }

        /// <summary>The exact spot of a citizen at a place: spread around open places so a crowd does not stack up.</summary>
        Vector3 SpotOf(CitizenData c, ScheduleEntry e)
        {
            if (!places.TryGetValue(e.PlaceId, out var p)) p = places["plaza"];
            Vector3 o = new Vector3(c.SpotOffset.x, 0f, c.SpotOffset.y);
            switch (p.Kind)
            {
                case PlaceKind.Plaza: return p.Position + o * 6f;
                case PlaceKind.Fountain: return Layout.Fountain + o.normalized * 3.3f;
                case PlaceKind.Market: return p.Position + o * 1.2f;
                case PlaceKind.ArenaGate:
                case PlaceKind.LudusGate: return p.Position + o * 2.2f;
                case PlaceKind.GuardPost: return p.Position + o * 0.8f;
                default: return p.Position + o * (e.PlaceId.StartsWith("garden") ? 2f : 0.6f);
            }
        }

        /// <summary>Everyone at a place at an hour (arrived, not walking). Example: At("plaza", 14f).</summary>
        public List<CitizenData> At(string placeId, float hour)
        {
            var list = new List<CitizenData>();
            foreach (var c in Citizens)
            {
                var l = WhereIs(c, hour);
                if (!l.Walking && l.PlaceId == placeId) list.Add(c);
            }
            return list;
        }

        /// <summary>Everyone outdoors within a radius of a point (walking or standing): what the spawner materialises.</summary>
        public List<CitizenData> Near(Vector3 point, float radius, float hour)
        {
            var list = new List<CitizenData>();
            float r2 = radius * radius;
            foreach (var c in Citizens)
            {
                var l = WhereIs(c, hour);
                if (l.Indoors) continue;
                Vector3 d = l.Position - point; d.y = 0f;
                if (d.sqrMagnitude <= r2) list.Add(c);
            }
            return list;
        }

        /// <summary>How many citizens are doing what at an hour: sleeping, at home, working, walking, out and about.</summary>
        public Dictionary<string, int> Census(float hour)
        {
            var d = new Dictionary<string, int> { { "durmiendo", 0 }, { "en casa", 0 }, { "trabajando", 0 }, { "de camino", 0 }, { "fuera", 0 } };
            foreach (var c in Citizens)
            {
                var l = WhereIs(c, hour);
                string k = l.Walking ? "de camino" : l.Activity == Activity.Dormir ? "durmiendo" : l.Activity == Activity.EnCasa ? "en casa"
                         : l.Activity == Activity.Trabajar ? "trabajando" : "fuera";
                d[k]++;
            }
            return d;
        }

        public CitizenData Find(string name) => Citizens.Find(c => c.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase));
        public CitizenData FindJob(Job job) => Citizens.Find(c => c.Job == job);
        public TownPlace Place(string id) => places.TryGetValue(id, out var p) ? p : null;
        public string PlaceName(string id) => places.TryGetValue(id, out var p) ? p.Name : id;

        /// <summary>A citizen's whole day as text, row by row (for the info panel and the logs).</summary>
        public string Describe(CitizenData c, int day)
        {
            var sb = new StringBuilder();
            sb.Append(c.Name).Append(" - ").Append(c.JobName).Append(", ").Append(c.Age).Append(" anos, ").Append(c.RelationLabel).Append('\n');
            foreach (var e in ScheduleFor(c, day)) sb.Append(Clock(e.Start)).Append(' ').Append(ActivityName(e.Activity)).Append(" (").Append(PlaceName(e.PlaceId)).Append(")\n");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ relation with the player
        /// <summary>Your reputation in the town: what the townsfolk think of you on average (-100..100).</summary>
        public float Reputation
        {
            get
            {
                if (Citizens.Count == 0) return 0f;
                float sum = 0f;
                foreach (var c in Citizens) sum += c.Relation;
                return sum / Citizens.Count;
            }
        }

        public static string ReputationLabel(float r) => r >= 40f ? "IDOLO DEL PUEBLO" : r >= 15f ? "QUERIDO" : r > -15f ? "UN GLADIADOR MAS" : r > -40f ? "MAL VISTO" : "ODIADO";

        public void ChangeRelation(CitizenData c, int delta)
        {
            c.Relation = Mathf.Clamp(c.Relation + delta, -100, 100);
            relDirty = true;
            relSaveTimer = 2f;
        }

        void LoadRelations(int seed)
        {
            string s = PlayerPrefs.GetString(RelKey + seed, "");
            if (string.IsNullOrEmpty(s)) return;
            foreach (var part in s.Split(';'))
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out int id) && int.TryParse(kv[1], out int rel) && id >= 0 && id < Citizens.Count)
                    Citizens[id].Relation = rel;
            }
        }

        void SaveRelations()
        {
            relDirty = false;
            var sb = new StringBuilder();
            foreach (var c in Citizens) { if (sb.Length > 0) sb.Append(';'); sb.Append(c.Id).Append(':').Append(c.Relation); }
            PlayerPrefs.SetString(RelKey + Layout.Seed, sb.ToString());
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ names
        public static string Clock(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            int h = Mathf.FloorToInt(hour), m = Mathf.FloorToInt((hour - h) * 60f);
            return h.ToString("00") + ":" + m.ToString("00");
        }

        public static string ActivityName(Activity a)
        {
            switch (a)
            {
                case Activity.Dormir: return "durmiendo";
                case Activity.EnCasa: return "en casa";
                case Activity.Trabajar: return "trabajando";
                case Activity.Comer: return "comiendo";
                case Activity.Mercado: return "en el mercado";
                case Activity.Rezar: return "rezando";
                case Activity.Banarse: return "en las termas";
                case Activity.Taberna: return "en la taberna";
                case Activity.Agua: return "por agua";
                default: return "paseando";
            }
        }

        public static string JobName(Job j, bool female)
        {
            switch (j)
            {
                case Job.Herrero: return female ? "herrera" : "herrero";
                case Job.Panadero: return female ? "panadera" : "panadero";
                case Job.Tabernero: return female ? "tabernera" : "tabernero";
                case Job.Sacerdote: return female ? "sacerdotisa" : "sacerdote";
                case Job.Alfarero: return female ? "alfarera" : "alfarero";
                case Job.Banero: return female ? "banera" : "banero";
                case Job.Escriba: return "escriba";
                case Job.Tendero: return female ? "tendera" : "tendero";
                case Job.Mercader: return female ? "mercadera" : "mercader";
                case Job.Guardia: return "guardia";
                case Job.Hortelano: return female ? "hortelana" : "hortelano";
                case Job.Aguador: return female ? "aguadora" : "aguador";
                case Job.AmaDeCasa: return female ? "ama de casa" : "jornalero";
                case Job.Anciano: return female ? "anciana" : "anciano";
                default: return female ? "nina" : "nino";
            }
        }
    }
}
