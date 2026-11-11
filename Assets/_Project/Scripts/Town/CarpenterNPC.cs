using Gladiator.Arena;
using Gladiator.Character;
using Gladiator.Combat;
using Gladiator.PS2Style;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// The carpenter (faber lignarius) at his workbench on the plaza: sells the furniture for the houses you buy
    /// (bed, table and chairs, kitchen, bath, chest...). Talk to him with E. He cannot be hurt.
    /// </summary>
    public class CarpenterNPC : MonoBehaviour
    {
        public Fighter Fighter { get; private set; }
        public string Name { get; private set; }
        Vector3 face;

        public static CarpenterNPC Spawn(TownBootstrap town)
        {
            var L = town.Layout;
            Vector3 at = L.ToWorld(new Vector3(6.5f, 0f, -8.6f));
            Vector3 fwd = L.ToWorld(Vector3.forward).normalized; // looking at the fountain
            Quaternion q = Quaternion.LookRotation(fwd);

            // the workbench in front of him: planks, tools; a stack of boards beside it; a board with his wares
            var bench = new GameObject("CarpenterBench").transform;
            bench.SetParent(town.transform, false);
            bench.SetPositionAndRotation(at + fwd * 0.9f, q);
            var wood = PS2Materials.Get("furn_wood", PS2Textures.Wood, new Color(0.62f, 0.44f, 0.26f));
            var dark = PS2Materials.Get("furn_dark", PS2Textures.Wood, new Color(0.36f, 0.24f, 0.14f));
            var iron = PS2Materials.Get("furn_iron", PS2Textures.Metal, new Color(0.3f, 0.3f, 0.32f));
            PS2Spawn.Box(bench, "Top", new Vector3(1.9f, 0.1f, 0.75f), new Vector3(0, 0.85f, 0), wood).AddComponent<BoxCollider>().size = new Vector3(1.9f, 1.8f, 0.75f);
            foreach (float sx in new[] { -0.85f, 0.85f }) foreach (float sz in new[] { -0.3f, 0.3f })
                PS2Spawn.Box(bench, "Leg", new Vector3(0.1f, 0.8f, 0.1f), new Vector3(sx, 0.4f, sz), dark);
            PS2Spawn.Box(bench, "Plank", new Vector3(1.3f, 0.05f, 0.22f), new Vector3(-0.15f, 0.93f, 0.1f), wood, Quaternion.Euler(0, 12f, 0));
            PS2Spawn.Box(bench, "Saw", new Vector3(0.5f, 0.02f, 0.12f), new Vector3(0.55f, 0.92f, -0.15f), iron, Quaternion.Euler(0, -25f, 0));
            PS2Spawn.Box(bench, "Mallet", new Vector3(0.12f, 0.12f, 0.2f), new Vector3(-0.7f, 0.96f, -0.2f), dark);
            for (int i = 0; i < 5; i++)
                PS2Spawn.Box(bench, "Board", new Vector3(0.28f, 0.05f, 1.9f), new Vector3(1.5f, 0.05f + i * 0.055f, -0.9f + (i % 2) * 0.06f), i % 2 == 0 ? wood : dark);
            var sign = PS2Spawn.Box(bench, "Sign", new Vector3(1.1f, 0.4f, 0.05f), new Vector3(0, 0.45f, 0.4f), dark);
            PS2Spawn.Box(sign.transform, "Chair", new Vector3(0.14f, 0.2f, 0.02f), new Vector3(-0.25f, 0f, 0.03f), wood);
            PS2Spawn.Box(sign.transform, "Bed", new Vector3(0.34f, 0.1f, 0.02f), new Vector3(0.2f, -0.04f, 0.03f), wood);
            LowPolyMesh.BakeAmbient(bench.gameObject, TownBuilder.AmbientSky, TownBuilder.AmbientGround);

            const string name = "Faber el carpintero";
            var look = CharacterAppearance.Random(new System.Random(town.Layout.Seed * 17 + 5), true);
            look.Name = name;
            look.Armor = 0; look.Helmet = false;
            look.Outfit = 1; look.ClothColor = 3;
            look.Fat = 0.35f; look.Muscle = 0.6f; look.Femininity = 0f;
            look.Beard = 1;
            var f = CharacterFactory.Create(name, look, at + Vector3.up * 0.05f, q, CitizenSpawner.CitizenTeam);
            f.Harmless = true;
            f.DisarmResistance = 1f;
            f.Health.SetMax(1000000f, true);
            var c = f.gameObject.AddComponent<CarpenterNPC>();
            c.Fighter = f; c.Name = name; c.face = fwd;
            Debug.Log($"[TOWN] carpenter at {at}");
            return c;
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
}
