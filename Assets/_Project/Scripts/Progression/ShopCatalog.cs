using System.Collections.Generic;
using Gladiator.Combat;

namespace Gladiator.Progression
{
    public class ShopItem
    {
        public string Id, Name, Description;
        public int Price;
        public WeaponStats Weapon;   // or
        public ShieldStats Shield;   // or
        public Character.ArmorPiece Armor;
    }

    /// <summary>
    /// The mercator's goods: the whole arsenal (11 weapons, the gladius is the starting one) and 3 shields.
    /// Buying equips the item at once; the EQUIPO tab of the shop changes the loadout among what you own.
    /// </summary>
    public static class ShopCatalog
    {
        public const string SwordId = "hispaniensis";
        public const string ShieldId = "scutum";

        public static readonly List<ShopItem> Items = Build();

        static List<ShopItem> Build()
        {
            var list = new List<ShopItem>();
            // The two original items first (the autotest buys by index), then the rest of the arsenal and shields.
            list.Add(FromShield(ShieldLibrary.Scutum));
            list.Add(FromWeapon(WeaponLibrary.Hispaniensis));
            foreach (var w in WeaponLibrary.Arsenal)
                if (w != WeaponLibrary.Gladius && w != WeaponLibrary.Hispaniensis) list.Add(FromWeapon(w));
            list.Add(FromShield(ShieldLibrary.Wood));
            list.Add(FromShield(ShieldLibrary.Iron));
            foreach (var a in Character.ArmorLibrary.All)
                list.Add(new ShopItem
                {
                    Id = a.Id, Price = a.Price, Armor = a,
                    Name = a.Name.ToUpperInvariant() + "  (" + Character.ArmorLibrary.SlotName(a.Slot).ToLowerInvariant() + (a.Heavy ? ", pesada" : ", ligera") + ")",
                    Description = $"{a.Description}  Peso {a.Weight:0.#} kg: cuanto mas peso, mas lento andas, ruedas, saltas y trepas.",
                });
            return list;
        }

        static ShopItem FromWeapon(WeaponStats w) => new ShopItem
        {
            Id = w.Id, Name = w.Name.ToUpperInvariant() + "  (" + w.GripLabel + ")", Price = w.Price, Weapon = w,
            Description = $"Daño {w.LightDamage:0}/{w.HeavyDamage:0}  alcance {w.Reach:0.0}  vel x{w.Speed:0.00}. " + w.Description
        };

        static ShopItem FromShield(ShieldStats s) => new ShopItem
        {
            Id = s.Id, Name = s.Name.ToUpperInvariant() + "  (escudo)", Price = s.Price, Shield = s, Description = s.Description
        };

        public static bool Owns(PlayerProfile p, ShopItem item) => p.Owned.Contains(item.Id);

        public enum BuyResult { Ok, AlreadyOwned, NotEnoughMoney }

        public static BuyResult Buy(PlayerProfile p, ShopItem item)
        {
            if (Owns(p, item)) return BuyResult.AlreadyOwned;
            if (!p.Spend(item.Price)) return BuyResult.NotEnoughMoney;
            p.Owned.Add(item.Id);
            if (item.Weapon != null) Loadout.SetMain(p, item.Weapon.Id);
            if (item.Shield != null) Loadout.SetLeft(p, item.Shield.Id);
            if (item.Armor != null) p.Armor[(int)item.Armor.Slot] = item.Armor.Id;
            p.NotifyChanged();
            return BuyResult.Ok;
        }
    }

    /// <summary>Loadout rules: what can go in each hand given what you own.</summary>
    public static class Loadout
    {
        public static WeaponStats Main(PlayerProfile p) => WeaponLibrary.ByName(p.MainHand) ?? WeaponLibrary.Gladius;
        static bool NeedsBothHands(WeaponStats w) => w.Grip == Grip.TwoHand || w.IsBow;

        public static void SetMain(PlayerProfile p, string id)
        {
            var w = WeaponLibrary.ByName(id);
            if (w == null) return;
            p.MainHand = w.Id;
            // A two-hander or a bow takes both hands: a second weapon is put away (a shield goes on the back).
            if (NeedsBothHands(w) || !w.CanDualWield) p.OffHand = "";
        }

        /// <summary>Left hand options, in order: nothing, owned shields, then owned one-handers (dual wielding).</summary>
        public static List<string> LeftOptions(PlayerProfile p)
        {
            var list = new List<string> { "" };
            foreach (var s in ShieldLibrary.All) if (p.Owned.Contains(s.Id)) list.Add(s.Id);
            var main = Main(p);
            if (main.CanDualWield)
                foreach (var w in WeaponLibrary.Arsenal) if (w.CanDualWield && p.Owned.Contains(w.Id)) list.Add(w.Id);
            return list;
        }

        public static string LeftValue(PlayerProfile p) => !string.IsNullOrEmpty(p.ShieldId) ? p.ShieldId : p.OffHand ?? "";

        public static void SetLeft(PlayerProfile p, string id)
        {
            if (ShieldLibrary.ById(id) != null) { p.ShieldId = id; p.OffHand = ""; return; }
            var w = WeaponLibrary.ByName(id);
            if (w != null && w.CanDualWield && Main(p).CanDualWield) { p.OffHand = w.Id; p.ShieldId = ""; return; }
            p.OffHand = ""; p.ShieldId = "";
        }

        public static List<string> MainOptions(PlayerProfile p)
        {
            var list = new List<string>();
            foreach (var w in WeaponLibrary.Arsenal) if (p.Owned.Contains(w.Id)) list.Add(w.Id);
            return list;
        }

        public static string Label(string id)
        {
            if (string.IsNullOrEmpty(id)) return "NADA";
            var s = ShieldLibrary.ById(id);
            if (s != null) return s.Name.ToUpperInvariant();
            var w = WeaponLibrary.ByName(id);
            return w != null ? w.Name.ToUpperInvariant() : id;
        }

        /// <summary>Armour options for a slot: nothing, then the owned pieces.</summary>
        public static List<string> ArmorOptions(PlayerProfile p, Character.ArmorSlot slot)
        {
            var list = new List<string> { "" };
            foreach (var a in Character.ArmorLibrary.All) if (a.Slot == slot && p.Owned.Contains(a.Id)) list.Add(a.Id);
            return list;
        }

        public static string ArmorLabel(string id)
        {
            var a = Character.ArmorLibrary.ById(id);
            return a == null ? "NADA" : a.Name.ToUpperInvariant() + (a.Heavy ? " (P)" : " (L)");
        }

        /// <summary>Weight line: kilos and what they cost in speed.</summary>
        public static string WeightLine(PlayerProfile p)
        {
            var set = p.ArmorSet;
            float load = set.Load;
            return $"Armadura {set.Weight:0.#} kg: velocidad -{35f * load:0}%, rodar -{40f * load:0}%, recuperacion +{35f * load:0}%.  " +
                   $"Protege: cabeza {set.Protection(Character.ArmorZone.Head) * 100f:0}%  torso {set.Protection(Character.ArmorZone.Torso) * 100f:0}%  " +
                   $"brazos {set.Protection(Character.ArmorZone.Arms) * 100f:0}%  piernas {set.Protection(Character.ArmorZone.Legs) * 100f:0}%";
        }

        /// <summary>One line describing how the loadout works.</summary>
        public static string Describe(PlayerProfile p)
        {
            var main = Main(p);
            var shield = ShieldLibrary.ById(p.ShieldId);
            var off = WeaponLibrary.ByName(p.OffHand);
            bool both = NeedsBothHands(main);
            string grip = main.IsBow ? "Arco a dos manos" :
                          both ? "A dos manos" :
                          off != null ? "Dos armas: combo alternado, golpe fuerte con ambas" :
                          main.Grip == Grip.Versatile ? (shield != null ? "A una mano con escudo" : "A dos manos (+daño, +alcance)") :
                          "A una mano";
            string sh = shield == null ? "" : both ? ".  Escudo a la espalda" : ".  Escudo en el brazo";
            float mul = main.MoveMul * (off != null ? off.MoveMul : 1f) * (shield != null ? shield.MoveMul : 1f);
            string mv = mul < 0.995f ? $".  Velocidad {mul * 100f:0}%" : mul > 1.005f ? $".  Velocidad {mul * 100f:0}%" : "";
            return grip + sh + mv;
        }
    }
}
