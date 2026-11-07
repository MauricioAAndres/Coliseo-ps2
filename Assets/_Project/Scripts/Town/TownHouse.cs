using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.Town
{
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
        public GameObject Upper;     // ceiling, upper floors and roof (and their camera collider)
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
            return Mathf.Abs(l.x) < HalfX - 0.05f && Mathf.Abs(l.z) < HalfZ - 0.05f && l.y < 3f;
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

        public void ShowUpper(bool show) { if (Upper != null && Upper.activeSelf != show) Upper.SetActive(show); }
    }

    /// <summary>A front door on a hinge: E opens / closes it (it swings inwards).</summary>
    public class TownDoor : MonoBehaviour
    {
        public const float OpenAngle = 95f;
        public bool IsOpen { get; private set; }
        public Transform Hinge;
        public float Width = 1.2f;
        float angle;

        /// <summary>Middle of the doorway, on the ground (where you stand to use it).</summary>
        public Vector3 Center => Hinge.position + Hinge.parent.rotation * new Vector3(Width * 0.5f, 0f, 0f);
        public float Angle => angle;

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
            Hinge.localRotation = Quaternion.Euler(0f, angle, 0f);
        }
    }
}
