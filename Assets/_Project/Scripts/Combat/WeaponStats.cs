using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.Combat
{
    public enum WeaponShape { Fists, Sword, Axe, Club, Mace, Dagger, Greatsword, GreatAxe, Maul, Spear, Trident, Bow }

    /// <summary>How the weapon is held. Versatile (spear, trident): one hand with a shield or a second weapon, else two.</summary>
    public enum Grip { OneHand, TwoHand, Versatile }

    /// <summary>Which animation family the weapon uses.</summary>
    public enum WeaponStyle { Fists, Blade, Dagger, Heavy2H, Spear, Bow }

    [System.Serializable]
    public class WeaponStats
    {
        public string Name;
        public WeaponShape Shape;
        public float LightDamage;
        public float HeavyDamage;
        [Tooltip("Max distance (m) from the attacker's edge to the target's edge.")]
        public float Reach;
        [Tooltip("Attack speed multiplier (1 = normal, <1 slower).")]
        public float Speed;
        public float Length;
        public Color Tint;

        // ---- arsenal
        public string Id = "";
        public Grip Grip = Grip.OneHand;
        [Tooltip("Movement speed multiplier while carried (heavy two-handers slow you down).")]
        public float MoveMul = 1f;
        public int Price;
        public string Description = "";
        /// <summary>Versatile weapons held in both hands: damage and reach bonus.</summary>
        public float TwoHandDamageMul = 1.25f, TwoHandReachBonus = 0.3f;

        public WeaponStats(string name, WeaponShape shape, float light, float heavy, float reach, float speed, float length, Color tint)
        {
            Name = name; Shape = shape; LightDamage = light; HeavyDamage = heavy;
            Reach = reach; Speed = speed; Length = length; Tint = tint;
        }

        public bool IsBow => Shape == WeaponShape.Bow;
        public bool IsRanged => IsBow;
        /// <summary>Can go in the left hand next to another one-handed weapon.</summary>
        public bool CanDualWield => Grip == Grip.OneHand && !IsBow && Shape != WeaponShape.Fists;

        public WeaponStyle Style
        {
            get
            {
                switch (Shape)
                {
                    case WeaponShape.Fists: return WeaponStyle.Fists;
                    case WeaponShape.Dagger: return WeaponStyle.Dagger;
                    case WeaponShape.Greatsword: case WeaponShape.GreatAxe: case WeaponShape.Maul: return WeaponStyle.Heavy2H;
                    case WeaponShape.Spear: case WeaponShape.Trident: return WeaponStyle.Spear;
                    case WeaponShape.Bow: return WeaponStyle.Bow;
                    default: return WeaponStyle.Blade;
                }
            }
        }

        public string GripLabel => Grip == Grip.OneHand ? "una mano" : Grip == Grip.TwoHand ? "dos manos" : "una o dos manos";

        WeaponStats With(string id, Grip grip, float moveMul, int price, string desc)
        {
            Id = id; Grip = grip; MoveMul = moveMul; Price = price; Description = desc;
            return this;
        }

        internal static WeaponStats Make(string id, string name, WeaponShape shape, Grip grip, float light, float heavy, float reach,
                                         float speed, float length, Color tint, float moveMul, int price, string desc)
            => new WeaponStats(name, shape, light, heavy, reach, speed, length, tint).With(id, grip, moveMul, price, desc);
    }

    /// <summary>
    /// The arsenal: 12 weapons (one-handed, two-handed, versatile, ranged) plus the training rudis and fists.
    /// Prices are for the mercator; the gladius is free (starting weapon).
    /// </summary>
    public static class WeaponLibrary
    {
        static readonly Color Steel = new Color(0.9f, 0.9f, 0.95f), Iron = new Color(0.75f, 0.75f, 0.78f), Bronze = new Color(1f, 0.82f, 0.5f);

        public static readonly WeaponStats Fists = new WeaponStats("Puños", WeaponShape.Fists, 5f, 11f, 0.75f, 1.25f, 0f, Color.white);
        /// <summary>Wooden training sword used in the ludus.</summary>
        public static readonly WeaponStats Rudis = WeaponStats.Make("rudis", "Gladius de madera", WeaponShape.Sword, Grip.OneHand,
            4f, 9f, 1.2f, 1.05f, 0.72f, Color.white, 1f, 0, "Espada de entrenamiento del ludus.");

        // ---- one hand
        public static readonly WeaponStats Gladius = WeaponStats.Make("gladius", "Gladius", WeaponShape.Sword, Grip.OneHand,
            11f, 24f, 1.25f, 1.0f, 0.75f, Steel, 1f, 0, "Espada corta romana. Equilibrada. Se puede llevar una en cada mano.");
        public static readonly WeaponStats Hispaniensis = WeaponStats.Make("hispaniensis", "Hispaniensis", WeaponShape.Sword, Grip.OneHand,
            15f, 33f, 1.45f, 1.1f, 0.9f, new Color(1f, 0.95f, 0.8f), 1f, 140, "Mejor gladius: mas daño, alcance y velocidad. Una mano.");
        public static readonly WeaponStats Spatha = WeaponStats.Make("spatha", "Spatha", WeaponShape.Sword, Grip.OneHand,
            13f, 28f, 1.55f, 0.85f, 1.0f, new Color(0.85f, 0.88f, 1f), 1f, 110, "Espada larga a una mano: mas alcance, algo mas lenta.");
        public static readonly WeaponStats Dagger = WeaponStats.Make("pugio", "Pugio", WeaponShape.Dagger, Grip.OneHand,
            7f, 16f, 0.85f, 1.5f, 0.28f, Steel, 1.06f, 45, "Daga: muy rapida, poco alcance. Te mueves un poco mas rapido.");
        public static readonly WeaponStats Axe = WeaponStats.Make("hacha", "Hacha", WeaponShape.Axe, Grip.OneHand,
            15f, 34f, 1.3f, 0.75f, 0.85f, Iron, 1f, 90, "Hacha a una mano: golpes duros y lentos. Se puede llevar una en cada mano.");
        public static readonly WeaponStats Mace = WeaponStats.Make("maza", "Maza", WeaponShape.Mace, Grip.OneHand,
            13f, 32f, 1.2f, 0.8f, 0.75f, Iron, 1f, 85, "Maza de hierro: rompe guardias con el golpe fuerte (mas aturdimiento).");

        // ---- two hands (a shield goes on the back)
        public static readonly WeaponStats GreatAxe = WeaponStats.Make("hacha_guerra", "Hacha de guerra", WeaponShape.GreatAxe, Grip.TwoHand,
            24f, 52f, 1.6f, 0.62f, 1.15f, Iron, 0.85f, 190, "Dos manos. Daño enorme pero lenta y te mueves un 15% mas lento. El escudo va a la espalda.");
        public static readonly WeaponStats Greatsword = WeaponStats.Make("mandoble", "Mandoble", WeaponShape.Greatsword, Grip.TwoHand,
            20f, 44f, 1.85f, 0.75f, 1.3f, Steel, 0.92f, 230, "Espada a dos manos: el mayor alcance cuerpo a cuerpo. El escudo va a la espalda.");
        public static readonly WeaponStats Maul = WeaponStats.Make("mazo", "Mazo", WeaponShape.Maul, Grip.TwoHand,
            22f, 56f, 1.5f, 0.58f, 1.1f, new Color(0.65f, 0.6f, 0.55f), 0.82f, 200, "Mazo a dos manos: el golpe mas fuerte. Muy lento. El escudo va a la espalda.");

        // ---- versatile (one hand with a shield or a second weapon, two hands otherwise)
        public static readonly WeaponStats Spear = WeaponStats.Make("lanza", "Lanza", WeaponShape.Spear, Grip.Versatile,
            12f, 26f, 2.0f, 0.95f, 1.9f, Iron, 1f, 110, "Estocadas de largo alcance. Con escudo, a una mano; sin nada en la izquierda, a dos manos (+25% daño, mas alcance).");
        public static readonly WeaponStats Trident = WeaponStats.Make("tridente", "Tridente", WeaponShape.Trident, Grip.Versatile,
            14f, 30f, 1.9f, 0.85f, 1.8f, Bronze, 1f, 150, "El arma del retiarius: como la lanza, algo mas lenta y con mas daño.");

        // ---- ranged
        public static readonly WeaponStats Bow = WeaponStats.Make("arco", "Arco", WeaponShape.Bow, Grip.TwoHand,
            9f, 24f, 0.6f, 1f, 1.3f, new Color(0.7f, 0.5f, 0.3f), 1f, 130,
            "Flechas infinitas. Clic izq.: disparo rapido. Mantener clic der.: tensar (mas daño). El escudo va a la espalda.");

        /// <summary>Everything the mercator sells (and the arena racks can hold), in shop order.</summary>
        public static readonly WeaponStats[] Arsenal =
            { Gladius, Dagger, Mace, Axe, Spatha, Hispaniensis, Spear, Trident, Bow, GreatAxe, Maul, Greatsword };

        public static readonly WeaponStats[] EnemyPool = { Spatha, Axe, Mace };

        public static WeaponStats ByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var w in Arsenal) if (w.Name == name || w.Id == name) return w;
            if (name == Rudis.Name || name == Rudis.Id || name == "Rudis") return Rudis;
            if (name == "Maza" || name == "Club") return Mace;
            return null;
        }

        /// <summary>Opponents' weapons get more varied every day.</summary>
        public static WeaponStats[] PoolForDay(int day)
        {
            var list = new List<WeaponStats> { Gladius, Spatha, Axe, Mace, Dagger };
            if (day >= 2) { list.Add(Spear); list.Add(Trident); list.Add(Bow); }
            if (day >= 3) { list.Add(GreatAxe); list.Add(Maul); list.Add(Greatsword); }
            return list.ToArray();
        }
    }

    public enum ShieldShape { Scutum, RoundWood, RoundIron }

    /// <summary>The three shields: block strength, guard-break stagger, disarm resistance, weight.</summary>
    public class ShieldStats
    {
        public string Id, Name, Description;
        public ShieldShape Shape;
        public float BlockLight, BlockHeavy, GuardBreakStagger, DisarmResistance, MoveMul;
        public int Price;
    }

    public static class ShieldLibrary
    {
        public static readonly ShieldStats Wood = new ShieldStats
        {
            Id = "parma", Name = "Parma de madera", Shape = ShieldShape.RoundWood, BlockLight = 0.92f, BlockHeavy = 0.65f,
            GuardBreakStagger = 0.45f, DisarmResistance = 0.3f, MoveMul = 1f, Price = 40,
            Description = "Escudo redondo ligero. Bloqueo: ligero 92%, fuerte 65%. No te frena."
        };
        public static readonly ShieldStats Scutum = new ShieldStats
        {
            Id = "scutum", Name = "Scutum", Shape = ShieldShape.Scutum, BlockLight = 0.97f, BlockHeavy = 0.8f,
            GuardBreakStagger = 0.35f, DisarmResistance = 0.5f, MoveMul = 0.97f, Price = 75,
            Description = "El gran escudo legionario. Bloqueo: ligero 97%, fuerte 80%. Mitad de desarmes."
        };
        public static readonly ShieldStats Iron = new ShieldStats
        {
            Id = "clipeus", Name = "Clipeus de hierro", Shape = ShieldShape.RoundIron, BlockLight = 0.98f, BlockHeavy = 0.88f,
            GuardBreakStagger = 0.3f, DisarmResistance = 0.6f, MoveMul = 0.93f, Price = 140,
            Description = "Escudo redondo de hierro: el que mejor bloquea (fuerte 88%), pero pesa (-7% velocidad)."
        };
        public static readonly ShieldStats[] All = { Wood, Scutum, Iron };

        public static ShieldStats ById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in All) if (s.Id == id) return s;
            return null;
        }
    }
}
