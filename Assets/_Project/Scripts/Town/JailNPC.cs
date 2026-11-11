using Gladiator.Arena;
using Gladiator.Character;
using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// A prisoner in the jail by the arena: sits on the floor of his cell, looks at you through the bars, and talks
    /// (E) when you come to the bars. He cannot be hurt.
    /// </summary>
    public class JailNPC : MonoBehaviour
    {
        static readonly string[] Names = { "Spurius el ladron", "Vibius el desertor", "Kaeso el falsario", "Numerius el tahur" };
        static readonly string[] Lines =
        {
            "Me robaron a mi, ¡lo juro por Jupiter!",
            "Dicen que manana me mandan a la arena... sin espada.",
            "¿Tienes la llave? No... nadie tiene la llave.",
            "El carcelero ronca mas fuerte que un leon.",
            "Si ganas en la arena, pidele al gobernador que me suelte.",
        };

        public Fighter Fighter { get; private set; }
        public string Name { get; private set; }
        public int Talks { get; private set; }
        Pose spot;

        public static JailNPC Spawn(TownBootstrap town)
        {
            var jail = town.Houses != null ? town.Houses.Find(h => h.CellSpots.Count > 1) : null;
            if (jail == null) return null;
            var at = jail.CellSpots[1];
            int seed = town.Layout.Seed;
            string name = Names[Mathf.Abs(seed * 3 + 1) % Names.Length];
            var look = CharacterAppearance.Random(new System.Random(seed * 53 + 11), true);
            look.Name = name;
            look.Armor = 0; look.Helmet = false;
            look.Outfit = 0; look.ClothColor = 0;
            look.Fat = 0.1f; look.Muscle = 0.3f; look.Femininity = 0f;
            look.Beard = 1;
            var f = CharacterFactory.Create(name, look, at.position + Vector3.up * 0.05f, at.rotation, CitizenSpawner.CitizenTeam);
            f.Harmless = true;
            f.DisarmResistance = 1f;
            f.Health.SetMax(1000000f, true);
            var j = f.gameObject.AddComponent<JailNPC>();
            j.Fighter = f; j.Name = name; j.spot = at;
            Debug.Log($"[TOWN] prisoner {name} in the jail at {at.position}");
            return j;
        }

        void Update()
        {
            if (Fighter == null || !Fighter.IsAlive) return;
            Fighter.Move(Vector3.zero);
            if (Fighter.State == FighterState.Idle)
            {
                Fighter.SetFacing(spot.rotation * Vector3.forward);
                Fighter.TryActivity(Fighter.ActSitGround);
            }
        }

        public string Talk()
        {
            Talks++;
            var parts = Name.Split(' ');
            return "PRESO " + parts[0].ToUpperInvariant() + ": " + Lines[(Talks - 1) % Lines.Length];
        }
    }
}
