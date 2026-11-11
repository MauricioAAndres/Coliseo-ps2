using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>A bed, a chair or an altar: the activity (Fighter.Act*), where the body goes, and where to stand up
    /// (null: back where the player was).</summary>
    public struct FurnitureSpot
    {
        public int Kind;
        public Pose At;
        public Vector3? Stand;
        public FurnitureSpot(int kind, Pose at, Vector3? stand = null) { Kind = kind; At = at; Stand = stand; }
        public string Verb => Kind == Gladiator.Combat.Fighter.ActLie ? "ACOSTARSE EN LA CAMA"
                            : Kind == Gladiator.Combat.Fighter.ActChair ? "SENTARSE EN LA SILLA" : "REZAR ANTE EL ALTAR";
    }

    /// <summary>
    /// The inside of a town house: kitchen at the entrance, then a bedroom and a bathroom at the back (openings, no
    /// inner doors). Sits at the lot's centre with the lot's rotation, so local x runs along the front and +z towards
    /// the street. Knows which room a point is in and hides the house's roof, ceiling and upper floors while the
    /// player is inside, so the camera can look in.
    /// </summary>
    public class HouseInterior : MonoBehaviour
    {
        public struct Room { public string Name; public float X0, X1, Z0, Z1; }

        public TownLot Lot;
        public float HalfX, HalfZ;
        public GameObject Upper;     // the roof (and its camera collider)
        /// <summary>Everything above this storey (upper floor, roof): hidden while the player is inside.</summary>
        public readonly List<GameObject> Hide = new List<GameObject>();
        /// <summary>0 ground floor, 1 first floor (reached by the outside stairs).</summary>
        public int Floor;
        /// <summary>The "SE VENDE" board (houses for sale).</summary>
        public GameObject SaleSign;
        /// <summary>The hearth's light: on only while the player is in this storey.</summary>
        public readonly List<Light> Lights = new List<Light>();
        /// <summary>One entry per bed placed (tests: never two in one bedroom).</summary>
        public readonly List<string> Beds = new List<string>();

        /// <summary>The temple: where you kneel to pray (in front of the altar, facing the god).</summary>
        public Pose? AltarSpot;
        /// <summary>The governor's domus: where the governor stands, in front of his chair of state.</summary>
        public Pose? ThroneSpot;
        /// <summary>A public building (the infirmary): walking in is not trespassing.</summary>
        public bool Public;
        /// <summary>Infirmary beds: where a patient lies (root position on the mattress, facing the foot of the bed).</summary>
        public readonly List<Pose> BedSpots = new List<Pose>();

        /// <summary>Furniture you can use with E: beds (lie down), chairs (sit), the altar (pray).</summary>
        public readonly List<FurnitureSpot> Furniture = new List<FurnitureSpot>();

        /// <summary>The nearest piece of furniture to <paramref name="p"/> (same floor), or null.</summary>
        public FurnitureSpot? NearestFurniture(Vector3 p, float range)
        {
            FurnitureSpot? best = null; float bd = range;
            foreach (var f in Furniture)
            {
                Vector3 d = f.At.position - p;
                if (d.y < -0.4f || d.y > 0.9f) continue;
                d.y = 0f;
                if (d.magnitude < bd) { bd = d.magnitude; best = f; }
            }
            return best;
        }

        public void SetLights(bool on) { foreach (var l in Lights) if (l != null) l.enabled = on; }
        /// <summary>First floor: foot and top of the outside stairs, and the way up (tests).</summary>
        public Vector3 StairBottom, StairTop, StairDir;
        public TownDoor Door;
        public readonly List<Room> Rooms = new List<Room>();
        /// <summary>The partition between the kitchen and the back rooms (local z) and its two openings (local x).</summary>
        public float PartitionZ, BedGapX, BathGapX;

        /// <summary>World point on the kitchen side (+1) or the room side (-1) of a room's opening.</summary>
        public Vector3 GapPoint(string room, int side) =>
            transform.TransformPoint(new Vector3(room == "BANO" ? BathGapX : BedGapX, 0.05f, PartitionZ + side * 0.7f));

        public bool Contains(Vector3 world)
        {
            Vector3 l = transform.InverseTransformPoint(world);
            return Mathf.Abs(l.x) < HalfX - 0.05f && Mathf.Abs(l.z) < HalfZ - 0.05f && l.y > -0.6f && l.y < 3f;
        }

        /// <summary>COCINA, BANO or HABITACION, or null outside.</summary>
        public string RoomAt(Vector3 world)
        {
            if (!Contains(world)) return null;
            Vector3 l = transform.InverseTransformPoint(world);
            foreach (var r in Rooms) if (l.x >= r.X0 && l.x <= r.X1 && l.z >= r.Z0 && l.z <= r.Z1) return r.Name;
            return null;
        }

        /// <summary>World centre of a room (tests, and later furniture).</summary>
        public Vector3 RoomCenter(string name)
        {
            foreach (var r in Rooms) if (r.Name == name) return transform.TransformPoint(new Vector3((r.X0 + r.X1) * 0.5f, 0.05f, (r.Z0 + r.Z1) * 0.5f));
            return transform.position;
        }

        public void ShowUpper(bool show) { foreach (var g in Hide) if (g != null && g.activeSelf != show) g.SetActive(show); }
    }

    /// <summary>The houses the player has bought, per town (saved).</summary>
    public static class HouseDeeds
    {
        const string Key = "Gladiator.HousesOwned.";
        static string K(int seed) => Key + seed;

        public static bool Owns(int seed, int lotId)
        {
            foreach (var part in PlayerPrefs.GetString(K(seed), "").Split(','))
                if (int.TryParse(part, out int id) && id == lotId) return true;
            return false;
        }

        public static void Add(int seed, int lotId)
        {
            if (Owns(seed, lotId)) return;
            string v = PlayerPrefs.GetString(K(seed), "");
            PlayerPrefs.SetString(K(seed), string.IsNullOrEmpty(v) ? lotId.ToString() : v + "," + lotId);
            PlayerPrefs.Save();
        }

        public static void Remove(int seed, int lotId)
        {
            var keep = new List<string>();
            foreach (var part in PlayerPrefs.GetString(K(seed), "").Split(','))
                if (int.TryParse(part, out int id) && id != lotId) keep.Add(part);
            PlayerPrefs.SetString(K(seed), string.Join(",", keep));
            PlayerPrefs.Save();
        }
    }

    /// <summary>A front door on a hinge: E opens / closes it (it swings inwards).</summary>
    public class TownDoor : MonoBehaviour
    {
        public const float OpenAngle = 95f;
        public bool IsOpen { get; private set; }
        public Transform Hinge;
        public float Width = 1.2f;
        /// <summary>The hinge's closed rotation (side doors are turned 90 degrees).</summary>
        public Quaternion BaseRot = Quaternion.identity;
        float angle;

        /// <summary>Middle of the doorway, on the ground (where you stand to use it).</summary>
        public Vector3 Center => Hinge.position + Hinge.parent.rotation * (BaseRot * new Vector3(Width * 0.5f, 0f, 0f));
        public float Angle => angle;

        /// <summary>Online: another player opened or closed it.</summary>
        public void SetOpen(bool open) { if (open != IsOpen) Toggle(); }

        public void Toggle()
        {
            IsOpen = !IsOpen;
            Gladiator.Audio.AudioManager.At3D(IsOpen ? "ui_select" : "ui_back", Center + Vector3.up, 0.8f);
        }

        void Update()
        {
            float target = IsOpen ? OpenAngle : 0f;
            if (Mathf.Approximately(angle, target)) return;
            angle = Mathf.MoveTowards(angle, target, 300f * Time.deltaTime);
            Hinge.localRotation = BaseRot * Quaternion.Euler(0f, angle, 0f);
        }
    }
}
