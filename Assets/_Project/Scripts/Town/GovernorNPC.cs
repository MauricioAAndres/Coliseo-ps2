using Gladiator.Arena;
using Gladiator.Character;
using Gladiator.Combat;
using Gladiator.Core;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>A guard of the governor's domus: stands by the dais, shield up.</summary>
    public class GovernorGuard : MonoBehaviour
    {
        Fighter f;
        Vector3 face;
        void Awake() { f = GetComponent<Fighter>(); }
        public void Init(Vector3 facing) { face = facing; }
        void Update()
        {
            if (f == null || !f.IsAlive) return;
            f.Move(Vector3.zero);
            f.SetFacing(face);
        }
    }

    /// <summary>
    /// The governor of the town: in his domus on the plaza, in front of his chair of state, between two guards. Talk to
    /// him (E): what he says depends on your record in the arena. He cannot be hurt; striking him (or his guards) is an
    /// offence for the watch.
    /// </summary>
    public class GovernorNPC : MonoBehaviour
    {
        static readonly string[] Names = { "Gnaeus Calpurnius Piso", "Lucius Cornelius Balbus", "Quintus Fabius Maximus", "Marcus Aemilius Lepidus" };

        public Fighter Fighter { get; private set; }
        public string Name { get; private set; }
        public int Talks { get; private set; }
        Vector3 face;

        public static GovernorNPC Spawn(TownBootstrap town)
        {
            var house = town.Houses != null ? town.Houses.Find(h => h.ThroneSpot.HasValue) : null;
            if (house == null) return null;
            var spot = house.ThroneSpot.Value;
            string name = Names[Mathf.Abs(town.Layout.Seed) % Names.Length];

            // dressed like the townsfolk (their clothes), in a long purple robe
            var look = CharacterAppearance.Random(new System.Random(town.Layout.Seed * 31 + 7), true);
            look.Name = name;
            look.Outfit = 2;          // long robe
            look.ClothColor = 5;      // purple: a magistrate
            look.Shirt = 3;           // TUNICA LARGA, down to the feet
            look.Pants = 4;           // FALDA LARGA under it
            look.PantsColor = 7;      // white
            look.Armor = 0; look.Helmet = false;
            look.Fat = 0.6f; look.Muscle = 0.2f; look.Femininity = 0f;
            look.HairColor = 4; look.HairStyle = 1; look.Beard = 0;
            var f = CharacterFactory.Create(name, look, spot.position + Vector3.up * 0.05f, spot.rotation, CitizenSpawner.CitizenTeam);
            f.Harmless = true;
            f.DisarmResistance = 1f;
            f.Health.SetMax(1000000f, true);
            var g = f.gameObject.AddComponent<GovernorNPC>();
            g.Fighter = f;
            g.Name = name;
            g.face = spot.rotation * Vector3.forward;

            // two guards flanking the dais
            Vector3 right = spot.rotation * Vector3.right, fwd = spot.rotation * Vector3.forward;
            string[] gn = { "Pretoriano Severus", "Pretoriano Rufus" };
            for (int i = 0; i < 2; i++)
            {
                var gl = CharacterFactory.FromLook(new GladiatorLook { Tunic = CitizenSpawner.GuardRed, Skin = new Color(0.8f, 0.62f, 0.48f), Helmet = true, Crest = new Color(0.75f, 0.1f, 0.08f) }, gn[i]);
                gl.Outfit = 1; gl.ClothColor = 1; gl.ClothOverride = CitizenSpawner.GuardRed; gl.Muscle = 0.75f;
                var guard = CharacterFactory.Create(gn[i], gl, spot.position + right * (i == 0 ? -1.7f : 1.7f) + fwd * 0.3f + Vector3.up * 0.05f, spot.rotation,
                    CitizenSpawner.CitizenTeam, ArmorSet.FromIds(new[] { "galea_bronce", "lorica", "hombreras_hierro", "grebas" }));
                guard.Equip(WeaponFactory.Create(WeaponLibrary.Gladius));
                var sc = ShieldLibrary.ById("scutum");
                if (sc != null) Shield.Attach(guard, sc, CitizenSpawner.GuardRed);
                guard.Harmless = true;
                guard.DisarmResistance = 1f;
                guard.Health.SetMax(1000000f, true);
                guard.gameObject.AddComponent<GovernorGuard>().Init(fwd);
            }
            Debug.Log($"[TOWN] governor {name} in his domus at {spot.position}");
            return g;
        }

        void Update()
        {
            if (Fighter == null || !Fighter.IsAlive) return;
            Fighter.Move(Vector3.zero);
            var player = TownBootstrap.Instance != null ? TownBootstrap.Instance.Player : null;
            Vector3 to = player != null ? player.transform.position - transform.position : Vector3.zero; to.y = 0f;
            Fighter.SetFacing(to.sqrMagnitude > 0.01f && to.magnitude < 6f ? to : face);
        }

        /// <summary>What he tells you: depends on your fights (and how often you come to bother him).</summary>
        public string Talk(Fighter player)
        {
            Talks++;
            var gm = GameManager.Instance;
            int won = gm != null ? gm.Profile.Wins : 0, lost = gm != null ? gm.Profile.Losses : 0, day = gm != null ? gm.DayCycle.Day : 1;
            if (player != null) Fighter.SetFacing(player.transform.position - transform.position);
            if (Fighter.State == FighterState.Idle) Fighter.TryTaunt(Talks % 3 == 0 ? 5 : 3); // a greeting, or "no" when you insist
            var parts = Name.Split(' ');
            string who = "GOBERNADOR " + parts[parts.Length - 1].ToUpperInvariant() + ": ";
            if (Talks > 3) return who + "Ya te escuche. Vuelve con una victoria.";
            if (won + lost == 0) return who + "Asi que eres el nuevo del ludus. Roma te mira.";
            if (won >= 5 && won > lost * 2) return who + "¡" + won + " victorias! La plebe grita tu nombre.";
            if (won > lost) return who + "Vas bien: " + won + "-" + lost + ". El campeon ya oye hablar de ti.";
            if (won == lost) return who + "Tantas victorias como derrotas. Entrena mas.";
            return who + "Mas derrotas que victorias (" + won + "-" + lost + "). No me convences.";
        }
    }
}
