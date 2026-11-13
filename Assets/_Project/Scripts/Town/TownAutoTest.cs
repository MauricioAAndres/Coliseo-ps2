using System.Collections;
using System.IO;
using Gladiator.Combat;
using Gladiator.Core;
using Gladiator.Progression;
using Gladiator.Training;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gladiator.Town
{
    /// <summary>
    /// "-towntest" (optionally "-townseed N"): loads the town, logs the plan, takes shots (arrival, bird's-eye view,
    /// plaza, street from the shoulder, PS3 look), walks the gladiator from the ludus gate through the plaza to the
    /// arena gate (checks nothing blocks the way), then goes back to the ludus and out again through the east door.
    /// Lines start with "[TOWNTEST]". Nothing is saved.
    /// </summary>
    public class TownAutoTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-towntest") < 0) return;
            var go = new GameObject("TownAutoTest");
            DontDestroyOnLoad(go);
            go.AddComponent<TownAutoTest>();
        }

        static void Log(string s) { Debug.Log("[TOWNTEST] " + s); }

        static IEnumerator Wait(float s) { for (float t = 0f; t < s; t += Time.unscaledDeltaTime) yield return null; }

        /// <summary>Keeps the camera in front of a fighter, a little to the side, for s seconds.</summary>
        static IEnumerator HoldCam(Gladiator.Arena.ArenaCamera cam, Fighter f, float s, float h = 0.9f, float dist = 4f, float pitch = 8f, float side = 25f)
        {
            for (float t = 0f; t < s; t += Time.unscaledDeltaTime)
            {
                cam.FocusOverride = f.transform.position + Vector3.up * h; cam.Distance = dist; cam.Pitch = pitch;
                cam.Yaw = Quaternion.LookRotation(-f.transform.forward).eulerAngles.y + side;
                yield return null;
            }
        }

        static void Teleport(Fighter f, Vector3 at)
        {
            var cc = f.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            f.transform.position = at;
            if (cc != null) cc.enabled = true;
        }

        /// <summary>
        /// The ludus inside the town: the yard with its stations and partners, the gate that opens by itself, walking
        /// out to the street and back in (same scene), and a station that still works.
        /// </summary>
        IEnumerator LudusTest(TownBootstrap town)
        {
            var pl = town.Player;
            var pcL = pl.GetComponent<PlayerController>();
            if (pcL != null) pcL.enabled = false;
            var gate = town.Ludus.GetComponentInChildren<Gladiator.Training.LudusGateDoors>();
            int partners = Gladiator.Training.TrainingNPC.All.Count;
            Log($"LUDUS inTown={(town.Ludus != null)} stations={town.Training.Stations.Count} partners={partners} gate={(gate != null)} scene={SceneManager.GetActiveScene().name}");
            town.GetComponent<TownManager>().GoToLudus();
            yield return Wait(0.5f);
            bool inYard = town.InLudus(pl.transform.position);
            // overview of the yard
            var gcam = town.Cam;
            for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime) { gcam.FocusOverride = town.Ludus.position; gcam.Distance = 30f; gcam.Pitch = 60f; yield return null; }
            yield return Shot("10_ludus_en_el_pueblo");
            gcam.FocusOverride = null;
            // walk out: to the gate, through it, into the street; then back in
            Vector3[] route =
            {
                town.Ludus.TransformPoint(new Vector3(0f, 0f, 8.5f)),
                town.Ludus.TransformPoint(new Vector3(0f, 0f, 16f)),
                town.Ludus.TransformPoint(new Vector3(0f, 0f, 2f)),
            };
            bool gateOpened = false, wentOut = false, cameBack = false;
            float walked = 0f;
            for (int i = 0; i < route.Length; i++)
            {
                float t = 0f;
                while (t < 12f)
                {
                    Vector3 to = route[i] - pl.transform.position; to.y = 0f;
                    if (to.magnitude < 1.0f) break;
                    pl.Move(to.normalized);
                    pl.SetFacing(to);
                    if (gate != null && gate.IsOpen) gateOpened = true;
                    t += Time.deltaTime;
                    yield return null;
                }
                walked += t;
                pl.Move(Vector3.zero);
                if (i == 0) yield return Shot("10b_puerta_abierta");
                if (i == 1) { wentOut = !town.InLudus(pl.transform.position) && (route[1] - pl.transform.position).magnitude < 1.5f; yield return Shot("10c_fuera_del_ludus"); }
                if (i == 2) cameBack = town.InLudus(pl.transform.position);
            }
            yield return Wait(4f);
            Log($"LUDUS spawnInYard={inYard} gateOpened={gateOpened} wentOut={wentOut} cameBack={cameBack} gateClosedAfter={(gate != null && !gate.IsOpen)} sameScene={(TownBootstrap.Instance == town)} time={walked:0.0}s");
            // a station still works in the town (free practice, then stop)
            var st = town.Training.Stations[0];
            bool started = town.Training.StartStation(st, true);
            yield return Wait(4f);
            bool running = Gladiator.Training.TrainingMinigame.PlayerRunning == st;
            yield return Shot("10d_entrenando_en_el_pueblo");
            st.StopRequested = true;
            float t1 = Time.time;
            while (town.Training.Busy && Time.time - t1 < 15f) yield return null;
            Log($"LUDUS station {st.Id} started={started} running={running} finished={!town.Training.Busy}");
            if (pcL != null) pcL.enabled = true;
        }

        /// <summary>
        /// A three-storey home: up the first stair, along the front balcony, up the second stair to the second floor's
        /// door, in. Also: nobody floats over a vegetable garden any more.
        /// </summary>
        IEnumerator ThreeStoreyTest(TownBootstrap town)
        {
            var pl = town.Player;
            var pcS = pl.GetComponent<PlayerController>();
            if (pcS != null) pcS.enabled = false;
            int lots3 = 0; foreach (var l in town.Layout.Lots) if (l.IsHome && l.Floors >= 3) lots3++;
            int floor2 = town.Houses.FindAll(h => h.Floor == 2).Count;
            HouseInterior top = town.Houses.Find(h => h.Floor == 2);
            HouseInterior mid = top != null ? town.Houses.Find(h => h.Floor == 1 && h.Lot.Id == top.Lot.Id) : null;
            Log($"STAIRS3 threeStoreyHomes={lots3} secondFloors={floor2} (expected equal)");
            if (top != null && mid != null)
            {
                var gcam = town.Cam;
                Vector3 balconyA = mid.StairTop + mid.StairDir * 1.9f;
                Vector3[] route = { mid.StairTop, balconyA, top.StairBottom, top.StairTop };
                Teleport(pl, mid.StairBottom, mid.StairDir);
                yield return Wait(0.4f);
                int reached = 0;
                for (int i = 0; i < route.Length; i++)
                {
                    float t = 0f;
                    while (t < 10f)
                    {
                        Vector3 to = route[i] - pl.transform.position; to.y = 0f;
                        if (to.magnitude < 0.45f) break;
                        pl.Move(to.normalized * 0.7f);
                        pl.SetFacing(to);
                        gcam.FocusOverride = pl.transform.position + Vector3.up * 1f; gcam.Distance = 9f; gcam.Pitch = 25f;
                        t += Time.deltaTime;
                        yield return null;
                    }
                    pl.Move(Vector3.zero);
                    if ((route[i] - pl.transform.position).magnitude < 0.9f) reached++;
                    Log($"STAIRS3 point {i} reached={(route[i] - pl.transform.position).magnitude < 0.9f} y={pl.transform.position.y:0.00} (want {route[i].y:0.00})");
                    if (i == 1) yield return Shot("11a_balcon");
                }
                yield return Shot("11b_escalera_tercer_piso");
                if (top.Door != null && !top.Door.IsOpen) top.Door.Toggle();
                yield return Wait(0.6f);
                // straight in through the side door (towards the middle of the house), then stop
                Vector3 inDir = Vector3.Project(top.transform.position - top.StairTop, top.transform.right); inDir.y = 0f;
                Vector3 inside = top.StairTop + inDir.normalized * 2.2f;
                for (float t = 0f; t < 5f; t += Time.deltaTime)
                {
                    Vector3 to = inside - pl.transform.position; to.y = 0f;
                    if (to.magnitude < 0.6f) break;
                    pl.Move(to.normalized * 0.7f); pl.SetFacing(to);
                    yield return null;
                }
                pl.Move(Vector3.zero);
                yield return Wait(0.5f);
                var tm = town.GetComponent<TownManager>();
                Log($"STAIRS3 climbed={reached}/{route.Length} insideSecondFloor={top.Contains(pl.transform.position)} y={pl.transform.position.y:0.00} (expected ~6.2) roofHidden={!top.Upper.activeSelf} house={(tm.CurrentHouse == top)}");
                yield return Shot("11c_dentro_tercer_piso");
                gcam.FocusOverride = null;
            }
            // a vegetable garden: stand on it, nobody floats
            var garden = town.Layout.Lots.Find(l => l.Kind == BuildingKind.Garden);
            if (garden != null)
            {
                Teleport(pl, garden.Center + Vector3.up * 0.05f, garden.Front);
                yield return Wait(1f);
                Log($"GARDEN standY={pl.transform.position.y:0.00} (expected ~0.0, was 1.2 floating)");
            }
            if (pcS != null) pcS.enabled = true;
        }

        /// <summary>Shops: a real door on its hinge and a home inside; walking in is no offence for the watch.</summary>
        IEnumerator ShopsTest(TownBootstrap town)
        {
            var pl = town.Player;
            var pcS = pl.GetComponent<PlayerController>();
            if (pcS != null) pcS.enabled = false;
            int shopLots = town.Layout.Lots.FindAll(l => l.IsShop).Count;
            var shops = town.Houses.FindAll(h => h.Lot.IsShop);
            Log($"SHOPS lots={shopLots} enterable={shops.Count} withDoor={shops.FindAll(h => h.Door != null).Count} public={shops.TrueForAll(h => h.Public)} (expected all)");
            var smithy = shops.Find(h => h.Lot.Kind == BuildingKind.Smithy && (h.Lot.Center - town.Layout.Fountain).magnitude < 30f) ?? shops.Find(h => h.Lot.Kind == BuildingKind.Smithy);
            if (smithy != null)
            {
                town.Guards.Suspended = false;
                int w0 = town.Guards.Warnings;
                var gcam = town.Cam;
                Vector3 front = smithy.Door.Center + smithy.transform.forward * 1.4f;
                Teleport(pl, front, -smithy.transform.forward);
                for (float t = 0f; t < 1f; t += Time.deltaTime) { gcam.FocusOverride = pl.transform.position + Vector3.up; gcam.Distance = 8f; gcam.Pitch = 30f; yield return null; }
                yield return Shot("12a_herreria_puerta");
                if (!smithy.Door.IsOpen) smithy.Door.Toggle();
                yield return Wait(0.6f);
                // straight through the doorway first, then into the kitchen
                Vector3 inside = smithy.Door.Center - smithy.transform.forward * 1.3f;
                for (float t = 0f; t < 6f; t += Time.deltaTime)
                {
                    Vector3 to = inside - pl.transform.position; to.y = 0f;
                    if (to.magnitude < 0.6f) break;
                    pl.Move(to.normalized * 0.7f); pl.SetFacing(to);
                    gcam.FocusOverride = pl.transform.position + Vector3.up; gcam.Distance = 8f; gcam.Pitch = 45f;
                    yield return null;
                }
                pl.Move(Vector3.zero);
                yield return Wait(0.8f);
                var tm = town.GetComponent<TownManager>();
                Log($"SHOPS walked into {smithy.Lot.Kind} inside={smithy.Contains(pl.transform.position)} room={tm.CurrentRoom} roofHidden={!smithy.Upper.activeSelf} warnings {w0}->{town.Guards.Warnings} (expected no change)");
                yield return Shot("12b_dentro_herreria");
                gcam.FocusOverride = null;
                Teleport(pl, front, -smithy.transform.forward);
                yield return Wait(0.3f);
                if (smithy.Door.IsOpen) smithy.Door.Toggle(); // leave it as found (later tests use this door)
                town.Guards.Suspended = true;
            }
            if (pcS != null) pcS.enabled = true;
        }

        /// <summary>The new rules: beds for one / two, sleeping the hours away, one arena a day, doors locked at night,
        /// the carpenter's furniture in a bought house, the chest's inventory, townspeople with knives.</summary>
        IEnumerator RulesTest(TownBootstrap town)
        {
            var pl = town.Player;
            var pcR = pl.GetComponent<PlayerController>();
            if (pcR != null) pcR.enabled = false;
            var gcam = town.Cam;
            var tm = town.GetComponent<TownManager>();
            var sim = TownSimulation.Instance;
            var gm = GameManager.Instance;
            int seed = town.Layout.Seed;
            IEnumerator GetUp()
            {
                if (pl.InActivity) pl.StopActivity();
                float t0 = Time.time;
                while (pl.State == FighterState.Activity && Time.time - t0 < 5f) { pl.Move(Vector3.zero); yield return null; }
            }

            // ---- beds: a double bed takes two (one per side), a single bed one
            HouseInterior dblHouse = null, sglHouse = null;
            foreach (var h in town.Houses)
            {
                if (h.Public || h.Floor != 0 || h.Lot.ForSale) continue;
                var beds = h.Furniture.FindAll(f => f.Kind == Fighter.ActLie);
                if (beds.Count == 2 && (beds[0].At.position - beds[1].At.position).magnitude < 1f && dblHouse == null) dblHouse = h;
                if (beds.Count == 1 && sglHouse == null) sglHouse = h;
            }
            if (dblHouse != null)
            {
                var beds = dblHouse.Furniture.FindAll(f => f.Kind == Fighter.ActLie);
                Teleport(pl, beds[0].At.position + Vector3.up * 0.1f, dblHouse.transform.forward);
                yield return Wait(0.2f);
                bool ok = tm.UseFurniture(beds[0]);
                yield return Wait(0.5f);
                var next = dblHouse.NearestFurniture(beds[0].At.position, 1.25f, null);
                Log($"BEDS double: lay={ok} sideA occupied={HouseInterior.Occupied(beds[0], null)} sideB occupied={HouseInterior.Occupied(beds[1], null)} " +
                    $"aSecondPersonGets={(next.HasValue && (next.Value.At.position - beds[1].At.position).magnitude < 0.05f ? "the other side" : "nothing free")} (expected the other side)");
                yield return GetUp();
            }
            else Log("BEDS no double bed found");
            if (sglHouse != null)
            {
                var bed = sglHouse.Furniture.Find(f => f.Kind == Fighter.ActLie);
                Teleport(pl, bed.At.position + Vector3.up * 0.1f, sglHouse.transform.forward);
                yield return Wait(0.2f);
                bool ok = tm.UseFurniture(bed);
                yield return Wait(0.5f);
                var next = sglHouse.NearestFurniture(bed.At.position, 1.25f, null);
                Log($"BEDS single: lay={ok} aSecondPersonFinds={(next.HasValue && HouseInterior.Occupied(next.Value, null) ? "CAMA OCUPADA" : "a free bed")} (expected CAMA OCUPADA) " +
                    $"sleepAllowedInSomebodyElsesHouse={tm.CanSleepIn(sglHouse)} (expected True: at your own risk)");
                yield return GetUp();
            }

            // ---- sleeping in the infirmary: 3 hours in ~3 seconds, health back; then past midnight: a new day
            town.PutInInfirmaryBed(out _);
            yield return Wait(0.8f);
            sim.Hour = 14f;
            pl.Health.TakeDamage(pl.Health.Current - pl.Health.Max * 0.4f);
            float h0 = sim.Hour, hp0 = pl.Health.Normalized; int d0 = sim.Day;
            bool started = tm.StartSleep(3);
            yield return Wait(1.5f);
            yield return Shot("16a_durmiendo");
            float tS = Time.time;
            while (tm.Sleeping && Time.time - tS < 6f) yield return null;
            Log($"SLEEP started={started} canSleepInInfirmary={tm.CanSleepIn(town.Infirmary)} hours {h0:0.0}->{sim.Hour:0.0} (expected ~17) in {Time.time - tS + 1.5f:0.0}s " +
                $"hp {hp0:0.00}->{pl.Health.Normalized:0.00} (expected ~+0.30) stillInBed={pl.InActivity} clockRate={sim.ClockRate}");
            // one arena a day: fought today -> the gate stays shut; sleeping past midnight opens it again
            gm.DayCycle.MarkFought();
            tm.OpenArenaGate();
            bool gateOpenedAfterFight = Gladiator.UI.ArenaGateMenu.Instance != null && Gladiator.UI.ArenaGateMenu.Instance.IsOpen;
            sim.Hour = 22.5f;
            tm.StartSleep(3);
            tS = Time.time;
            while (tm.Sleeping && Time.time - tS < 6f) yield return null;
            Log($"ARENA ONCE A DAY foughtToday->gateOpens={gateOpenedAfterFight} (expected False); slept past midnight: day {d0}->{sim.Day} cycleDay={gm.DayCycle.Day} hour={sim.Hour:0.0} " +
                $"foughtToday={gm.DayCycle.FoughtToday} (expected False)");
            yield return GetUp();

            // ---- night: houses that are not yours stay locked from outside; the jail never closes
            var someHome = sglHouse ?? dblHouse;
            sim.Hour = 23f;
            Vector3 outside = someHome.Door.Center + someHome.transform.forward * 1.5f;
            Log($"NIGHT 23:00 lockedFromOutside={tm.LockedAtNight(someHome, outside)} (expected True) fromInside={tm.LockedAtNight(someHome, someHome.transform.position)} (expected False) " +
                $"jail={tm.LockedAtNight(town.Jail, town.Jail.Door.Center + town.Jail.transform.forward * 1.5f)} (expected False)");
            sim.Hour = 12f;
            Log($"NIGHT 12:00 locked={tm.LockedAtNight(someHome, outside)} (expected False)");
            if (!someHome.Door.IsOpen) someHome.Door.Toggle();
            sim.Hour = 21.985f;
            yield return Wait(2.5f);
            Log($"NIGHT at 22:00 the open door closed={!someHome.Door.IsOpen} (expected True) hour={sim.Hour:0.00}");
            sim.Hour = 10f;

            // ---- buy a house, then its furniture from the carpenter; the chest shows the inventory
            var sale = town.Houses.Find(h => h.Floor == 0 && h.Lot.ForSale && !HouseDeeds.Owns(seed, h.Lot.Id));
            if (sale != null && town.Carpenter != null)
            {
                int money0 = gm.Profile.Denarii;
                gm.Profile.AddDenarii(2000);
                bool poorHouse = tm.BuyFurniture("bed");   // no house yet
                bool bought = tm.TryBuy(sale);
                int spent0 = gm.Profile.Denarii;
                int builtN = 0;
                foreach (var id in new[] { "bed", "table", "kitchen", "bath", "chest", "shelf" }) if (tm.BuyFurniture(id)) builtN++;
                bool again = tm.BuyFurniture("chest");     // already there
                int bedsSingle = sale.Furniture.FindAll(f => f.Kind == Fighter.ActLie).Count;
                bool dblBought = tm.BuyFurniture("bed2");
                yield return null;
                int bedsDouble = sale.Furniture.FindAll(f => f.Kind == Fighter.ActLie).Count;
                Log($"CARPENTER withoutHouse={poorHouse} (expected False) houseBought={bought} pieces={builtN}/6 twice={again} (expected False) spent={spent0 - gm.Profile.Denarii} " +
                    $"bedSpots single={bedsSingle} (expected 1) -> double={bedsDouble} (expected 2) chairs={sale.Furniture.FindAll(f => f.Kind == Fighter.ActChair).Count} chests={sale.Chests.Count} " +
                    $"saved=[{string.Join(",", HomeFurniture.Owned(seed, sale.Lot.Id))}]");
                // the furnished house, from the door
                if (!sale.Door.IsOpen) sale.Door.Toggle();
                Teleport(pl, sale.Door.Center - sale.transform.forward * 1.3f, -sale.transform.forward);
                for (float t = 0f; t < 1.2f; t += Time.deltaTime)
                {
                    gcam.FocusOverride = sale.transform.position + Vector3.up * 0.8f; gcam.Distance = 9.5f; gcam.Pitch = 52f;
                    gcam.Yaw = Quaternion.LookRotation(-sale.transform.forward).eulerAngles.y; yield return null;
                }
                yield return Shot("16b_casa_amueblada");
                // sleep is allowed in your own bed; the chest opens the inventory
                var myBed = sale.Furniture.Find(f => f.Kind == Fighter.ActLie);
                Teleport(pl, myBed.At.position + Vector3.up * 0.1f, sale.transform.forward);
                yield return Wait(0.2f);
                bool layHome = tm.UseFurniture(myBed);
                yield return Wait(0.4f);
                Log($"HOME bed lay={layHome} canSleepInMyHouse={tm.CanSleepIn(sale)} (expected True)");
                yield return GetUp();
                tm.OpenInventory();
                yield return Wait(0.6f);
                var inv = Gladiator.UI.InventoryPanel.Instance;
                Log($"CHEST inventoryOpen={(inv != null && inv.IsOpen)} listsGladius={(inv != null && inv.Shown.Contains("Gladius"))} listsFurniture={(inv != null && inv.Shown.Contains("cama doble"))}");
                yield return Shot("16c_cofre_inventario");
                if (inv != null) inv.Close();
                yield return Wait(0.3f);
                // the carpenter at his bench on the plaza
                Vector3 cp = town.Carpenter.Fighter.transform.position, cf = town.Carpenter.Fighter.transform.forward;
                Teleport(pl, cp + town.Layout.ToWorld(Vector3.forward).normalized * 2.6f, cp - pl.transform.position);
                yield return Wait(0.5f);
                tm.OpenCarpenter();
                yield return Wait(0.6f);
                for (float t = 0f; t < 0.6f; t += Time.deltaTime) { gcam.FocusOverride = cp + Vector3.up; gcam.Distance = 6f; gcam.Pitch = 25f; yield return null; }
                yield return Shot("16d_carpintero");
                if (Gladiator.UI.ChoiceMenu.Instance != null) Gladiator.UI.ChoiceMenu.Instance.Close();
                yield return Wait(0.3f);
                // leave the save as it was
                HouseDeeds.Remove(seed, sale.Lot.Id);
                HomeFurniture.Clear(seed, sale.Lot.Id);
                gm.Profile.AddDenarii(money0 - gm.Profile.Denarii);
                if (sale.Door.IsOpen) sale.Door.Toggle();
            }
            else Log("CARPENTER no house for sale / no carpenter");

            // ---- knives: some grown townspeople carry one and fight back; no child does
            int armed = 0, adults = 0, kidsArmed = 0;
            foreach (var c in sim.Citizens)
            {
                bool k = CitizenDefender.CarriesKnife(c);
                if (c.Job != Job.Guardia && c.Age >= 16 && c.Age <= 60) { adults++; if (k) armed++; }
                if ((c.Age < 16 || c.Job == Job.Nino) && k) kidsArmed++;
            }
            CitizenNPC knifeNpc = null;
            sim.Hour = 17f; // everybody out
            yield return Wait(2.5f);
            Teleport(pl, town.Layout.Fountain + new Vector3(3f, 0.05f, 3f), Vector3.forward);
            yield return Wait(2.5f);
            foreach (var n in town.Spawner.Active)
                if (n.Citizen != null && CitizenDefender.CarriesKnife(n.Citizen) && n.gameObject.activeInHierarchy && !n.Controlled) { knifeNpc = n; break; }
            Log($"KNIVES adults={adults} carryKnife={armed} childrenWithKnife={kidsArmed} (expected 0) oneNearby={(knifeNpc != null)}");
            if (knifeNpc != null)
            {
                // away from the watch (a guard nearby ends it at once): the main street, north of the plaza
                Vector3 np = town.Layout.ToWorld(new Vector3(0f, 0f, 48f)) + Vector3.up * 0.05f;
                knifeNpc.Fighter.Controller.enabled = false;
                knifeNpc.transform.position = np;
                knifeNpc.Fighter.Controller.enabled = true;
                Teleport(pl, np + Vector3.forward * 1.3f, -Vector3.forward);
                pl.Health.Heal(pl.Health.Max);
                float hpB = pl.Health.Current;
                bool hasKnife = knifeNpc.Fighter.CurrentWeapon != null;
                CitizenDefender.Begin(knifeNpc, pl);
                for (float t = 0f; t < 6f; t += Time.deltaTime)
                {
                    pl.Move(Vector3.zero);
                    gcam.FocusOverride = pl.transform.position + Vector3.up; gcam.Distance = 5f; gcam.Pitch = 20f;
                    if (t > 2.5f && t < 2.5f + Time.deltaTime * 1.5f) yield return Shot("16e_vecino_con_cuchillo");
                    yield return null;
                }
                Log($"KNIVES {knifeNpc.Citizen.Name} ({knifeNpc.Citizen.Age}) knifeInHand={hasKnife} defending={CitizenDefender.Active} playerHp {hpB:0}->{pl.Health.Current:0} (expected lower) alive={pl.IsAlive}");
                Teleport(pl, np + Vector3.forward * 25f, Vector3.forward);
                yield return Wait(1.0f);
                Log($"KNIVES after backing off: defending={CitizenDefender.Active} (expected 0) outcome={CitizenDefender.LastOutcome} harmlessAgain={knifeNpc.Fighter.Harmless}");
                pl.Health.Heal(pl.Health.Max);
            }
            // ---- the new spear and trident blows (both hands, then the trident in one hand)
            Teleport(pl, town.Layout.ToWorld(new Vector3(0f, 0f, 40f)) + Vector3.up * 0.05f, Vector3.right);
            foreach (var ws in new[] { WeaponLibrary.Spear, WeaponLibrary.Trident })
            {
                pl.UnequipAll();
                pl.Equip(WeaponFactory.Create(ws));
                yield return HoldCam(gcam, pl, 0.6f, 0.9f, 4.2f, 12f, 70f);
                foreach (bool heavy in new[] { false, true })
                {
                    bool ok = pl.TryAttack(heavy ? AttackType.Heavy : AttackType.Light);
                    float t0 = Time.time; bool shot = false; FighterState seen = pl.State;
                    while (Time.time - t0 < 3f && (pl.IsAttacking || Time.time - t0 < 0.15f))
                    {
                        gcam.FocusOverride = pl.transform.position + Vector3.up * 0.9f; gcam.Distance = 4.2f; gcam.Pitch = 12f;
                        gcam.Yaw = pl.transform.eulerAngles.y + 70f;
                        if (!shot && pl.State == FighterState.Active) { shot = true; seen = pl.State; yield return Shot("17_" + ws.Id + (heavy ? "_fuerte" : "_rapido")); }
                        yield return null;
                    }
                    Log($"POLEARM {ws.Name} twoHanded={pl.TwoHanded} {(heavy ? "heavy" : "light")} started={ok} reachedBlow={shot} took={Time.time - t0:0.00}s");
                    yield return HoldCam(gcam, pl, 0.3f, 0.9f, 4.2f, 12f, 70f);
                }
            }
            pl.UnequipAll();
            pl.Equip(WeaponFactory.Create(WeaponLibrary.Gladius));
            sim.Hour = 10f;
            gcam.FocusOverride = null;
            if (pcR != null) pcR.enabled = true;
        }

        /// <summary>The jail beside the arena: the front door, the jailer's room, two cells behind bars whose gates open
        /// with E (closed you cannot walk in), the prisoner who talks through the bars.</summary>
        IEnumerator JailTest(TownBootstrap town)
        {
            var pl = town.Player;
            var pcJ = pl.GetComponent<PlayerController>();
            if (pcJ != null) pcJ.enabled = false;
            var gcam = town.Cam;
            var tm = town.GetComponent<TownManager>();
            var jail = town.Jail;
            if (jail == null) { Log("JAIL none"); if (pcJ != null) pcJ.enabled = true; yield break; }
            var L = town.Layout;
            float toArenaEdge = Vector3.ProjectOnPlane(jail.transform.position - L.ArenaCenter, Vector3.up).magnitude - TownLayout.ArenaRadius;
            int hidden = L.Lots.FindAll(l => l.Hidden).Count;
            Log($"JAIL lot={jail.Lot.Id} cells={jail.CellSpots.Count} gates={jail.ExtraDoors.Count} prisoner={(town.Prisoner != null ? town.Prisoner.Name : "none")} " +
                $"toArenaEdge={toArenaEdge:0}m infirmaryToArenaEdge={Vector3.ProjectOnPlane(L.InfirmaryLot.Center - L.ArenaCenter, Vector3.up).magnitude - TownLayout.ArenaRadius:0}m hiddenPlots={hidden}");
            IEnumerator WalkTo(Vector3 target, float seconds)
            {
                for (float t = 0f; t < seconds; t += Time.deltaTime)
                {
                    Vector3 to = target - pl.transform.position; to.y = 0f;
                    if (to.magnitude < 0.4f) break;
                    pl.Move(to.normalized * 0.6f); pl.SetFacing(to);
                    gcam.FocusOverride = pl.transform.position + Vector3.up; gcam.Distance = 6f; gcam.Pitch = 45f;
                    yield return null;
                }
                pl.Move(Vector3.zero);
            }
            Vector3 fwd = jail.transform.forward;
            // from above: the jail next to the colosseum
            Vector3 mid = (jail.transform.position + L.ArenaCenter) * 0.5f;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                gcam.FocusOverride = jail.transform.position; gcam.Distance = 34f; gcam.Pitch = 42f;
                gcam.Yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(L.ArenaCenter - jail.transform.position, Vector3.up)).eulerAngles.y; yield return null;
            }
            yield return Shot("15a_calabozo_junto_al_coliseo");
            // in through the front door
            Vector3 front = jail.Door.Center + fwd * 1.5f;
            Teleport(pl, front, -fwd);
            yield return Wait(0.3f);
            int w0 = town.Guards.Warnings;
            if (!jail.Door.IsOpen) jail.Door.Toggle();
            yield return Wait(0.6f);
            yield return WalkTo(jail.Door.Center - fwd * 1.6f, 5f);
            yield return Wait(0.4f);
            Log($"JAIL inside={jail.Contains(pl.transform.position)} room={tm.CurrentRoom} roofHidden={!jail.Upper.activeSelf} warnings {w0}->{town.Guards.Warnings}");
            for (float t = 0f; t < 1.0f; t += Time.deltaTime)
            {
                gcam.FocusOverride = jail.transform.position + Vector3.up * 1.2f; gcam.Distance = 7.5f; gcam.Pitch = 38f;
                gcam.Yaw = Quaternion.LookRotation(-fwd).eulerAngles.y; yield return null;
            }
            yield return Shot("15b_celdas");
            // cell 1: closed, you cannot walk in; open it with E, walk in
            var gate = jail.ExtraDoors[0];
            Vector3 atGate = gate.Center + fwd * 0.9f;
            yield return WalkTo(atGate, 5f);
            var near = town.NearestDoor(pl.transform.position, 1.7f);
            Vector3 inCell = gate.Center - fwd * 2.0f;
            yield return WalkTo(inCell, 2.5f);
            float depthClosed = Vector3.Dot(pl.transform.position - gate.Center, -fwd);
            Log($"JAIL gate prompt={(near == gate ? "ABRIR " + gate.Noun : "none")} closed: walkedIntoCell={depthClosed > 0.3f} (expected False, depth {depthClosed:0.00})");
            yield return WalkTo(atGate, 3f);
            gate.Toggle();
            yield return Wait(0.7f);
            yield return WalkTo(inCell, 4f);
            yield return Wait(0.3f);
            float depthOpen = Vector3.Dot(pl.transform.position - gate.Center, -fwd);
            Log($"JAIL gate open={gate.IsOpen} walkedIntoCell={depthOpen > 0.8f} (expected True, depth {depthOpen:0.00}) room={tm.CurrentRoom}");
            for (float t = 0f; t < 1.0f; t += Time.deltaTime)
            {
                gcam.FocusOverride = pl.transform.position + Vector3.up * 1.0f; gcam.Distance = 4.5f; gcam.Pitch = 30f;
                gcam.Yaw = Quaternion.LookRotation(-fwd).eulerAngles.y + 180f; yield return null;
            }
            yield return Shot("15c_dentro_de_la_celda");
            yield return WalkTo(atGate, 4f);
            gate.Toggle();
            yield return Wait(0.5f);
            // the prisoner in cell 2, through the bars
            if (town.Prisoner != null && jail.ExtraDoors.Count > 1)
            {
                var g2 = jail.ExtraDoors[1];
                yield return WalkTo(g2.Center + fwd * 0.7f, 6f);
                Vector3 pp = town.Prisoner.Fighter.transform.position; pp.y = pl.transform.position.y;
                yield return WalkTo(pp + fwd * 1.45f, 4f);
                var nearDoor = town.NearestDoor(pl.transform.position, 1.7f);
                Log($"JAIL at the bars by the prisoner: doorPromptWins={(nearDoor != null && !tm.PrisonerFirst(nearDoor, pl.transform.position))} (expected False) nearestDoor={(nearDoor != null ? nearDoor.Noun : "none")}");
                pl.SetFacing(town.Prisoner.Fighter.transform.position - pl.transform.position);
                yield return Wait(0.4f);
                string said = town.Prisoner.Talk();
                town.Hud.ShowMessage(said, 4f);
                Log($"JAIL prisoner dist={(town.Prisoner.Fighter.transform.position - pl.transform.position).magnitude:0.0}m sitting={town.Prisoner.Fighter.InActivity} says='{said}'");
                for (float t = 0f; t < 0.8f; t += Time.deltaTime)
                {
                    gcam.FocusOverride = (pl.transform.position + town.Prisoner.Fighter.transform.position) * 0.5f + Vector3.up; gcam.Distance = 4.5f; gcam.Pitch = 25f;
                    gcam.Yaw = Quaternion.LookRotation(-fwd).eulerAngles.y + 35f; yield return null;
                }
                yield return Shot("15d_preso");
            }
            Teleport(pl, front, -fwd);
            yield return Wait(0.3f);
            if (jail.Door.IsOpen) jail.Door.Toggle();
            gcam.FocusOverride = null;
            if (pcJ != null) pcJ.enabled = true;
        }

        /// <summary>E by a bed (lie on it), a chair (sit on it), the temple's altar (pray); getting up puts you back on
        /// the floor; the X wheel no longer has lying down or sitting.</summary>
        IEnumerator FurnitureTest(TownBootstrap town)
        {
            var pl = town.Player;
            var pcF = pl.GetComponent<PlayerController>();
            if (pcF != null) pcF.enabled = false;
            var gcam = town.Cam;
            var tm = town.GetComponent<TownManager>();
            Log($"WHEEL activity labels=[{string.Join(",", System.Array.ConvertAll(Gladiator.UI.TauntWheel.ActivityLabels, l => l ?? "-"))}]");
            // a free floor point next to a spot (same floor), where a capsule fits
            Vector3 FloorNear(Pose at, float floorY)
            {
                Vector3 best = at.position; best.y = floorY + 0.05f;
                foreach (float r in new[] { 0.9f, 1.1f })
                    for (int a = 0; a < 12; a++)
                    {
                        Vector3 c = at.position + Quaternion.Euler(0, a * 30f, 0) * Vector3.forward * r; c.y = floorY + 0.05f;
                        if (!Physics.CheckCapsule(c + Vector3.up * 0.45f, c + Vector3.up * 1.5f, 0.32f, ~0, QueryTriggerInteraction.Ignore)) return c;
                    }
                return best;
            }
            IEnumerator Use(HouseInterior h, int kind, string label, float floorY, float camSide)
            {
                FurnitureSpot? spot = null;
                foreach (var f in h.Furniture) if (f.Kind == kind) { spot = f; break; }
                if (!spot.HasValue) { Log($"FURNITURE {label} none in {h.name}"); yield break; }
                var fs = spot.Value;
                Vector3 from = FloorNear(fs.At, floorY);
                Teleport(pl, from, fs.At.position - from);
                yield return HoldCam(gcam, pl, 0.4f);
                var near = h.NearestFurniture(pl.transform.position, 1.25f);
                bool ok = tm.UseFurniture(fs);
                yield return HoldCam(gcam, pl, pl.ActivityInTime + 1.2f, 0.5f, 3.6f, 30f, camSide);
                float dy = pl.transform.position.y - fs.At.position.y, dxz = Vector3.ProjectOnPlane(pl.transform.position - fs.At.position, Vector3.up).magnitude;
                Log($"FURNITURE {label} promptFound={(near.HasValue ? near.Value.Verb : "none")} ok={ok} state={pl.State} kind={pl.ActivityKind} onFurniture={pl.OnFurniture} " +
                    $"offSpot={dxz:0.00}m dy={dy:0.00} y={pl.transform.position.y:0.00} ccOff={!pl.Controller.enabled} mat={(GameObject.Find("ActivityProps") is GameObject apg && apg.transform.childCount > 0)}");
                yield return Shot("14_" + label);
                pl.StopActivity();
                float t0 = Time.time;
                while (pl.State == FighterState.Activity && Time.time - t0 < 5f) { pl.Move(Vector3.zero); yield return null; }
                yield return HoldCam(gcam, pl, 0.4f);
                Log($"FURNITURE {label} got up in {Time.time - t0:0.00}s state={pl.State} ccOn={pl.Controller.enabled} backAtStart={(pl.transform.position - from).magnitude:0.00}m y={pl.transform.position.y:0.00}");
            }
            var home = town.Houses.Find(h => !h.Public && h.Floor == 0 && h.Furniture.Exists(f => f.Kind == Fighter.ActLie) && h.Furniture.Exists(f => f.Kind == Fighter.ActChair));
            if (home != null)
            {
                float fy = home.transform.position.y;
                if (home.Door != null && !home.Door.IsOpen) home.Door.Toggle();
                yield return Use(home, Fighter.ActLie, "cama", fy, 70f);
                yield return Use(home, Fighter.ActChair, "silla", fy, 35f);
                if (home.Door != null && home.Door.IsOpen) home.Door.Toggle();
            }
            else Log("FURNITURE no home with a bed and a chair");
            var temple = town.Houses.Find(h => h.AltarSpot.HasValue);
            if (temple != null) yield return Use(temple, Fighter.ActPray, "rezar", temple.AltarSpot.Value.position.y - 0.05f, 180f);
            // the infirmary beds are furniture too
            var inf = town.Infirmary;
            Log($"FURNITURE infirmary beds={(inf != null ? inf.Furniture.FindAll(f => f.Kind == Fighter.ActLie).Count : 0)}");
            gcam.FocusOverride = null;
            if (pcF != null) pcF.enabled = true;
        }

        /// <summary>The temple (up the steps, into the cella) and the governor's domus (door, the governor, talk to him).</summary>
        IEnumerator TempleDomusTest(TownBootstrap town)
        {
            var pl = town.Player;
            var pcT = pl.GetComponent<PlayerController>();
            if (pcT != null) pcT.enabled = false;
            var gcam = town.Cam;
            var temple = town.Houses.Find(h => h.AltarSpot.HasValue);
            var domus = town.Houses.Find(h => h.ThroneSpot.HasValue);
            // seen from the plaza, looking at the arena: temple on the left, domus on the right?
            bool templeLeft = false;
            if (temple != null && domus != null)
            {
                Vector3 right = Vector3.Cross(Vector3.up, -town.Layout.ArenaFacing);
                templeLeft = Vector3.Dot(temple.transform.position - domus.transform.position, right) < 0f;
            }
            Log($"TEMPLE found={(temple != null)} domus={(domus != null)} governor={(town.Governor != null ? town.Governor.Name : "none")} templeOnTheLeft={templeLeft}");
            Teleport(pl, town.Layout.Find("plaza").Position, -town.Layout.ArenaFacing);
            for (float t = 0f; t < 1.2f; t += Time.deltaTime) { gcam.FocusOverride = town.Layout.ToWorld(new Vector3(0, 0, 8f)) + Vector3.up * 2f; gcam.Distance = 16f; gcam.Pitch = 20f; gcam.Yaw = Quaternion.LookRotation(-town.Layout.ArenaFacing).eulerAngles.y; yield return null; }
            yield return Shot("13a_templo_y_domus");
            IEnumerator WalkTo(Vector3 target, float seconds)
            {
                for (float t = 0f; t < seconds; t += Time.deltaTime)
                {
                    Vector3 to = target - pl.transform.position; to.y = 0f;
                    if (to.magnitude < 0.5f) break;
                    pl.Move(to.normalized * 0.7f); pl.SetFacing(to);
                    gcam.FocusOverride = pl.transform.position + Vector3.up; gcam.Distance = 7f; gcam.Pitch = 35f;
                    yield return null;
                }
                pl.Move(Vector3.zero);
            }
            if (temple != null)
            {
                // from the plaza in front of the steps, up and in, to the altar
                Vector3 fwd = temple.transform.forward;
                Vector3 start = temple.AltarSpot.Value.position + fwd * 7.5f; start.y = 0.05f;
                Teleport(pl, start, -fwd);
                yield return Wait(0.3f);
                yield return WalkTo(temple.AltarSpot.Value.position, 9f);
                yield return Wait(0.5f);
                var tm = town.GetComponent<TownManager>();
                Log($"TEMPLE walkedIn inside={temple.Contains(pl.transform.position)} room={tm.CurrentRoom} y={pl.transform.position.y:0.00} (expected ~1.2) roofHidden={!temple.Upper.activeSelf} warnings={town.Guards.Warnings}");
                yield return Shot("13b_dentro_templo");
            }
            if (domus != null && town.Governor != null)
            {
                Vector3 fwd = domus.transform.forward;
                Vector3 front = domus.Door.Center + fwd * 1.4f;
                Teleport(pl, front, -fwd);
                yield return Wait(0.3f);
                if (!domus.Door.IsOpen) domus.Door.Toggle();
                yield return Wait(0.6f);
                yield return WalkTo(domus.Door.Center - fwd * 1.2f, 4f);
                yield return WalkTo(town.Governor.Fighter.transform.position + fwd * 1.6f, 6f);
                yield return Wait(0.5f);
                string said = town.Governor.Talk(pl);
                town.Hud.ShowMessage(said, 5f);
                yield return Wait(0.8f);
                Log($"DOMUS inside={domus.Contains(pl.transform.position)} roofHidden={!domus.Upper.activeSelf} distToGovernor={(town.Governor.Fighter.transform.position - pl.transform.position).magnitude:0.0} says='{said}'");
                yield return Shot("13c_gobernador");
                Teleport(pl, front, -fwd);
                yield return Wait(0.3f);
                if (domus.Door.IsOpen) domus.Door.Toggle();
            }
            gcam.FocusOverride = null;
            if (pcT != null) pcT.enabled = true;
        }

        /// <summary>The infirmary: ten beds, public (no trespassing), you wake up lying in a bed and get up by moving.</summary>
        IEnumerator InfirmaryTest(TownBootstrap town)
        {
            var inf = town.Infirmary;
            if (inf == null) { Log("INFIRMARY none"); yield break; }
            var g0 = town.Guards.Warnings;
            bool put = town.PutInInfirmaryBed(out Vector3 at);
            yield return HoldCam(town.Cam, town.Player, 2.0f, 0.6f, 5f, 35f, 70f);
            var tm = town.GetComponent<TownManager>();
            Log($"INFIRMARY beds={inf.BedSpots.Count} put={put} bed={town.LastBed} state={town.Player.State} lying={town.Player.InActivity && town.Player.ActivityKind == 0} " +
                $"skipIn={town.Player.ActivitySkipIn} inside={inf.Contains(town.Player.transform.position)} room={tm.CurrentRoom} roofHidden={!inf.Upper.activeSelf} " +
                $"y={town.Player.transform.position.y - at.y:0.00} warnings {g0}->{town.Guards.Warnings} doorToStreet={(inf.Lot.Door - town.Layout.ArenaGate).magnitude:0}m");
            yield return Shot("03a_enfermeria");
            // a wide look at the hall
            yield return HoldCam(town.Cam, town.Player, 0.6f, 0.5f, 10f, 55f, 70f);
            yield return Shot("03c_enfermeria_sala");
            town.Player.StopActivity();
            float t0 = Time.time;
            while (town.Player.State == FighterState.Activity && Time.time - t0 < 5f) yield return null;
            Log($"INFIRMARY got up in {Time.time - t0:0.00}s state={town.Player.State}");
            town.Cam.FocusOverride = null;
        }

        /// <summary>Town watch: two warnings (a blow to a townsperson, walking into a house), the third offence = a fight, then the knockout.</summary>
        /// <summary>The baths (a paid bath: health back, a bonus for the arena) and the basilica (fines, wagers, the day's errands).</summary>
        /// <summary>Crates and jars: scattered about, shoved by walking, picked up, thrown, smashed.</summary>
        IEnumerator PropsTest(TownBootstrap town)
        {
            var pl = town.Player;
            var cam = town.Cam;
            var pc = pl.GetComponent<PlayerController>();
            Vector3 p0 = pl.transform.position;
            int crates = 0, jars = 0, sunk = 0;
            yield return Wait(1.5f);
            foreach (var o in TownProp.All) { if (o.Type == TownProp.Kind.Crate) crates++; else jars++; if (o.transform.position.y < -0.5f || o.transform.position.y > 1.2f) sunk++; }
            Log($"PROPS scattered: indoors={TownProps.Indoors} outdoors={TownProps.Outdoors} crates={crates} jars={jars} odd height={sunk} (expected 0)");
            // one outdoors and one indoors, as the town has them
            TownProp outP = null, inP = null;
            foreach (var o in TownProp.All) { bool ins = town.HouseAt(o.transform.position) != null; if (ins && inP == null) inP = o; if (!ins && outP == null) outP = o; }
            if (outP != null)
            {
                var lot = town.Layout.Lots[0]; float bd = 1e9f;
                foreach (var l in town.Layout.Lots) { float d = (l.Center - outP.transform.position).sqrMagnitude; if (d < bd) { bd = d; lot = l; } }
                Vector3 away = outP.transform.position - lot.Center; away.y = 0f; away.Normalize();
                Teleport(pl, outP.transform.position + away * 2.4f + Vector3.up * 0.1f, -away);
                yield return Wait(1.2f);
                yield return Shot("05a_cajas_fuera");
            }
            if (inP != null)
            {
                var h = town.HouseAt(inP.transform.position);
                Teleport(pl, h.transform.position + Vector3.up * 0.2f, inP.transform.position - h.transform.position);
                yield return Wait(1.5f);
                yield return Shot("05b_cajas_dentro");
            }
            // an open stretch of the plaza
            Vector3 at = town.Layout.Fountain + new Vector3(5f, 0.1f, 5f), fwd = Vector3.right;
            Teleport(pl, at, fwd);
            pc.enabled = false;
            var root = new GameObject("TestProps").transform;
            var crate = TownProp.Create(root, TownProp.Kind.Crate, 9001, at + fwd * 1.3f, 0f, 1f);
            yield return Wait(0.8f);
            Vector3 c0 = crate.transform.position;
            for (float t = 0f; t < 1.4f; t += Time.deltaTime) { pl.SetFacing(fwd); pl.Move(fwd); yield return null; }
            pl.Move(Vector3.zero);
            yield return Wait(0.4f);
            float pushed = Vector3.Dot(crate.transform.position - c0, fwd);
            Log($"PROPS push: the crate moved {pushed:0.00} m walking into it (expected over 0.5)");
            string prompt0 = "";
            pc.enabled = true;
            yield return Wait(0.3f);
            bool picked = crate.PickUp(pl);
            yield return Wait(0.7f);
            bool carried = pl.Carried == crate;
            float hold = crate.transform.position.y - pl.transform.position.y;
            cam.Yaw = Quaternion.LookRotation(-fwd + Vector3.forward * 0.8f).eulerAngles.y;
            yield return Wait(0.3f);
            yield return Shot("05c_caja_en_brazos");
            Vector3 t0 = crate.transform.position;
            pl.TryAttack(AttackType.Light);
            yield return Wait(1.6f);
            bool crateAlive = crate != null && !crate.Broken;
            float flew = crateAlive ? Vector3.Dot(crate.transform.position - t0, fwd) : -1f;
            Log($"PROPS carry: picked={picked} carried={carried} held {hold:0.00} m up (expected about 1); thrown: carried after={(pl.Carried != null)} (expected False) flew {flew:0.0} m intact={crateAlive}");
            if (!crateAlive) { crate = TownProp.Create(root, TownProp.Kind.Crate, 9002, pl.transform.position + fwd * 1.2f, 0f, 1f); yield return Wait(0.6f); }
            // smash it
            Teleport(pl, crate.transform.position - fwd * 1.0f - Vector3.up * crate.transform.position.y + Vector3.up * 0.1f, fwd);
            yield return Wait(0.4f);
            int money = GameManager.Instance.Profile.Denarii;
            float tb = Time.time; int swings = 0;
            while (crate != null && !crate.Broken && Time.time - tb < 12f)
            {
                pl.SetFacing(crate.transform.position - pl.transform.position);
                if (pl.State == FighterState.Idle && pl.TryAttack(AttackType.Light)) swings++;
                yield return null;
                if (swings == 2 && crate != null) { /* mid-way picture */ }
            }
            yield return Wait(0.25f);
            yield return Shot("05d_caja_rota");
            Log($"PROPS smash: crate broken={(crate == null || crate.Broken)} (expected True) after {swings} swings in {Time.time - tb:0.0} s; coins {money}->{GameManager.Instance.Profile.Denarii}");
            // a jar thrown shatters where it lands
            yield return Wait(0.8f);
            var jar = TownProp.Create(root, TownProp.Kind.Jar, 9003, pl.transform.position + fwd * 1.0f, 0f, 1f);
            yield return Wait(0.6f);
            bool jp = jar.PickUp(pl);
            yield return Wait(0.5f);
            yield return Shot("05e_jarron_en_brazos");
            pl.TryAttack(AttackType.Light);
            yield return Wait(1.8f);
            Log($"PROPS jar: picked={jp} shattered on landing={(jar == null || jar.Broken)} (expected True) player armed again can attack={(pl.Carried == null)}");
            Destroy(root.gameObject);
            Teleport(pl, p0);
            yield return Wait(0.3f);
        }

        /// <summary>The two taverns: sign, tables, bar, barkeeper, buying food and drink; the armourer in the open.</summary>
        IEnumerator TavernTest(TownBootstrap town)
        {
            var pl = town.Player;
            var cam = town.Cam;
            var sv = town.Services;
            var prof = GameManager.Instance.Profile;
            Vector3 p0 = pl.transform.position;
            var lots = town.Layout.Lots.FindAll(l => l.Kind == BuildingKind.Tavern && !l.Hidden);
            string where = "";
            foreach (var l in lots) where += $" lot {l.Id} {l.Width:0.0}x{l.Depth:0.0} m, {(l.Center - town.Layout.Fountain).magnitude:0} m from the fountain;";
            Log($"TAVERN lots={lots.Count} (expected 2):{where} halls={sv.Taverns.Count} barkeepers={sv.Barkeepers.Count}");
            for (int i = 0; i < sv.Taverns.Count; i++)
            {
                var tv = sv.Taverns[i];
                var lot = tv.Lot;
                int chairs = 0; foreach (var f in tv.Furniture) if (f.Kind == Fighter.ActChair) chairs++;
                var bk = i < sv.Barkeepers.Count ? sv.Barkeepers[i] : null;
                Teleport(pl, lot.Center + lot.Front * (lot.Depth * 0.5f + 6.5f) + lot.Right * 1.5f + Vector3.up * 0.1f, -lot.Front);
                cam.FocusOverride = lot.Center + lot.Front * (lot.Depth * 0.5f) + Vector3.up * 2.2f; cam.Distance = 8.5f; cam.Pitch = 8f;
                cam.Yaw = Quaternion.LookRotation(-lot.Front - lot.Right * 0.45f).eulerAngles.y;
                yield return Wait(1.5f);
                yield return Shot("05f_taberna" + (i + 1) + "_fuera");
                cam.FocusOverride = null; cam.Distance = 6f; cam.Pitch = 35f;
                Teleport(pl, tv.CounterFront + lot.Front * 2.2f + Vector3.up * 0.1f, -lot.Front);
                cam.Yaw = Quaternion.LookRotation(-lot.Front).eulerAngles.y;
                yield return Wait(1.8f);
                yield return Shot("05g_taberna" + (i + 1) + "_dentro");
                Teleport(pl, tv.CounterFront + Vector3.up * 0.1f, -lot.Front);
                yield return Wait(1.0f);
                bool inside = tv.Contains(pl.transform.position);
                bool keeperBehind = bk != null && Vector3.Dot(bk.transform.position - tv.CounterFront, -lot.Front) > 1.2f && tv.Contains(bk.transform.position);
                Log($"TAVERN {i + 1}: lot {lot.Id} chairs={chairs} (expected 16) barkeeper={(bk != null ? bk.Name : "-")} behind the bar={keeperBehind} player inside at the bar={inside} public={tv.Public}");
                if (i == 0)
                {
                    yield return Shot("05h_taberna_barra");
                    prof.AddDenarii(30);
                    int m0 = prof.Denarii;
                    pl.Health.TakeDamage(pl.Health.Current - pl.Health.Max * 0.3f);
                    float h0 = pl.Health.Normalized;
                    bool beer = sv.BuyAtTavern(0);
                    float h1 = pl.Health.Normalized;
                    bool stew = sv.BuyAtTavern(3);
                    float h2 = pl.Health.Normalized;
                    prof.Spend(prof.Denarii);
                    bool broke = sv.BuyAtTavern(0);
                    Log($"TAVERN buy: beer={beer} health {h0:0.00}->{h1:0.00} (expected +0.12) stew={stew} ->{h2:0.00} (expected +0.55) paid={m0 - 14 == m0 - (4 + 12)} money {m0}->{m0 - 16} with no money={broke} (expected False)");
                    sv.OpenTavern(0);
                    yield return Wait(0.6f);
                    yield return Shot("05i_taberna_menu");
                    Gladiator.UI.ChoiceMenu.Ensure().Close();
                    yield return Wait(0.3f);
                    pl.Health.Heal(pl.Health.Max);
                    // sit at a table
                    var seat = tv.Furniture.Find(f => f.Kind == Fighter.ActChair);
                    bool sat = pl.TryActivityAt(seat.Kind, seat.At, pl.transform.position);
                    yield return Wait(1.6f);
                    yield return Shot("05j_taberna_sentado");
                    Log($"TAVERN sit at a table: {sat} state={pl.State}");
                    pl.StopActivity();
                    yield return Wait(1.0f);
                }
            }
            // the armourer: out in the open by his table
            var smithy = town.Layout.Lots.Find(l => l.Kind == BuildingKind.Smithy);
            if (smithy != null && town.Armorers.Count > 0)
            {
                Teleport(pl, smithy.Door + smithy.Front * 6f + Vector3.up * 0.1f, -smithy.Front);
                cam.FocusOverride = smithy.ArmorerSpot + Vector3.up * 1.1f; cam.Distance = 5.5f; cam.Pitch = 12f;
                cam.Yaw = Quaternion.LookRotation(-smithy.Front + smithy.Right * 0.3f).eulerAngles.y;
                yield return Wait(1.5f);
                yield return Shot("05k_armero_visible");
                cam.FocusOverride = null; cam.Distance = 6f; cam.Pitch = 35f;
                var a = town.Armorers[0].transform.position;
                bool clear = !Physics.Linecast(a + Vector3.up * 1.4f + smithy.Front * 0.5f, a + Vector3.up * 1.4f + smithy.Front * 5f, Ragdoll.WorldMask & ~(1 << Ragdoll.FighterLayer), QueryTriggerInteraction.Ignore);
                Log($"ARMORER in the open: nothing within 5 m in front of him={clear} (expected True) dist from spot={(a - smithy.ArmorerSpot).magnitude:0.0}");
            }
            Teleport(pl, p0);
            yield return Wait(0.3f);
        }

        /// <summary>The arena's doors and its free gate, both carpenters, the rows of jars, a carried crate against a
        /// wall, torches at night.</summary>
        IEnumerator ExtrasTest(TownBootstrap town)
        {
            var pl = town.Player;
            var cam = town.Cam;
            var sim = TownSimulation.Instance;
            var L = town.Layout;
            Vector3 p0 = pl.transform.position;
            float hour0 = sim.Hour;
            void Frame(Vector3 focus, Vector3 lookDir, float dist, float pitch) { cam.FocusOverride = focus; cam.Distance = dist; cam.Pitch = pitch; cam.Yaw = Quaternion.LookRotation(lookDir).eulerAngles.y; }
            void Free() { cam.FocusOverride = null; cam.Distance = 6f; cam.Pitch = 35f; }

            // ---- the arena gate: doors, and nobody standing in the doorway
            sim.Hour = 10.5f;
            Teleport(pl, L.ArenaGate + Vector3.up * 0.1f, -L.ArenaFacing);
            yield return Wait(3f);
            int inGate = 0; float nearest = 99f;
            foreach (var f in Fighter.All)
            {
                if (f == null || f == pl) continue;
                float d = (f.transform.position - L.ArenaGate).magnitude;
                if (d < nearest) nearest = d;
                if (d < 3.2f) inGate++;
            }
            Frame(L.ArenaGate + Vector3.up * 2.2f, -L.ArenaFacing, 9f, 6f);
            yield return Wait(1.0f);
            yield return Shot("05l_arena_puerta");
            Free();
            Log($"ARENA GATE: people within 3.2 m of it={inGate} (expected 0) nearest {nearest:0.0} m");

            // ---- the carpenters
            string cs = "";
            foreach (var c in town.Carpenters) cs += $" {c.Name} {(c.transform.position - L.Fountain).magnitude:0} m from the fountain;";
            Log($"CARPENTERS {town.Carpenters.Count} (expected 2):{cs} armourers={town.Armorers.Count}");
            for (int i = 0; i < town.Carpenters.Count; i++)
            {
                var c = town.Carpenters[i];
                Vector3 fw = c.transform.forward;
                Teleport(pl, c.transform.position + fw * 2.6f + Vector3.up * 0.1f, -fw);
                Frame(c.transform.position + Vector3.up * 1.1f, -fw + Vector3.Cross(Vector3.up, fw) * 0.5f, 6.5f, 14f);
                yield return Wait(1.3f);
                yield return Shot("05m_carpintero" + (i + 1));
                Free();
            }

            // ---- the rows of jars are real jars
            int rowJars = 0; TownProp aJar = null;
            foreach (var o in TownProp.All) if (o.Id >= 5000 && o.Id < 6000) { rowJars++; aJar = o; }
            Log($"JAR ROWS: spots={(town.JarSpots != null ? town.JarSpots.Count : 0)} jars standing={rowJars}");
            if (aJar != null)
            {
                Vector3 jp = aJar.transform.position;
                var lot = L.Lots[0]; float bd = 1e9f;
                foreach (var l in L.Lots) { float d = (l.Center - jp).sqrMagnitude; if (d < bd) { bd = d; lot = l; } }
                Vector3 away = jp - lot.Center; away.y = 0f; away = Vector3.Project(away, lot.Right).normalized;
                Teleport(pl, jp + away * 2.2f - Vector3.up * jp.y + Vector3.up * 0.1f, -away);
                Frame(jp + Vector3.up * 0.6f, -away + lot.Front * -0.6f, 6f, 22f);
                yield return Wait(1.2f);
                yield return Shot("05n_anforas");
                Free();
                // carry one into the wall: it stays on this side
                Teleport(pl, jp + away * 0.9f - Vector3.up * jp.y + Vector3.up * 0.1f, -away);
                yield return Wait(0.4f);
                bool got = aJar.PickUp(pl);
                var pcx = pl.GetComponent<PlayerController>();
                pcx.enabled = false;
                for (float t = 0f; t < 1.6f; t += Time.deltaTime) { pl.SetFacing(-away); pl.Move(-away); yield return null; }
                pl.Move(Vector3.zero);
                yield return Wait(0.3f);
                float wallAt = lot.Width * 0.5f;
                float jarAt = aJar != null ? Mathf.Abs(Vector3.Dot(aJar.transform.position - lot.Center, lot.Right)) : -1f;
                Frame(pl.transform.position + Vector3.up * 1f, -away + lot.Front * -0.9f, 4f, 25f);
                yield return Wait(0.5f);
                yield return Shot("05o_jarron_contra_pared");
                Free();
                Log($"CARRY against a wall: picked={got} jar centre {jarAt:0.00} m from the house's middle, wall face at {wallAt:0.00} (expected the jar outside: over {wallAt + 0.15f:0.00})");
                if (pl.Carried != null) pl.Carried.Drop();
                pcx.enabled = true;
            }

            // ---- torches at night
            sim.Hour = 20.6f;
            Teleport(pl, L.Fountain + new Vector3(4f, 0.1f, -4f), Vector3.forward);
            Torch.Ensure(pl);
            if (!pl.TorchOn) pl.ToggleTorch();
            yield return Wait(4f);
            int guards = 0, guardsLit = 0, folk = 0, folkLit = 0;
            CitizenNPC gShow = null;
            foreach (var n in town.Spawner.GetComponentsInChildren<CitizenNPC>())
            {
                if (n.Citizen == null || n.transform.position.y < -10f) continue;
                bool lit = n.GetComponent<Fighter>().TorchShown;
                if (n.Citizen.Job == Job.Guardia) { guards++; if (lit) { guardsLit++; gShow = n; } }
                else { folk++; if (lit) folkLit++; }
            }
            Log($"TORCH at 20:36: player on={pl.TorchOn} shown={pl.TorchShown} (expected True True); guards with bodies {guards}, with a torch {guardsLit}; townsfolk {folk}, with a torch {folkLit}");
            Frame(pl.transform.position + Vector3.up * 1.1f, new Vector3(-0.6f, 0, 1f), 4.2f, 14f);
            yield return Wait(0.8f);
            yield return Shot("05p_antorcha_jugador");
            if (gShow != null)
            {
                Vector3 gf = gShow.transform.forward;
                Teleport(pl, gShow.transform.position + gf * 3f + Vector3.up * 0.1f, -gf);
                Frame(gShow.transform.position + Vector3.up * 1.1f, -gf + Vector3.Cross(Vector3.up, gf) * 0.5f, 5f, 12f);
                yield return Wait(1.0f);
                yield return Shot("05q_antorcha_guardia");
            }
            Free();
            pl.ToggleTorch();
            yield return Wait(0.4f);
            Log($"TORCH put out: on={pl.TorchOn} shown={pl.TorchShown} (expected False False)");
            sim.Hour = hour0;
            Teleport(pl, p0);
            yield return Wait(0.5f);
        }

        /// <summary>The townsfolk's day: the timetable, the people shown indoors (asleep, at the table, in the tavern),
        /// the knots of neighbours in the evening, and the craftsmen's opening hours.</summary>
        IEnumerator LifeTest(TownBootstrap town)
        {
            var pl = town.Player;
            var cam = town.Cam;
            var sim = TownSimulation.Instance;
            var L = town.Layout;
            var guards = town.Guards;
            Vector3 p0 = pl.transform.position;
            float hour0 = sim.Hour;
            bool susp0 = guards.Suspended;
            guards.Suspended = true;
            void Frame(Vector3 focus, Vector3 lookDir, float dist, float pitch) { cam.FocusOverride = focus; cam.Distance = dist; cam.Pitch = pitch; cam.Yaw = Quaternion.LookRotation(lookDir).eulerAngles.y; }
            void Free() { cam.FocusOverride = null; cam.Distance = 6f; cam.Pitch = 35f; }

            // ---- the timetable
            var worker = sim.Citizens.Find(c => c.Job == Job.Alfarero || c.Job == Job.Tendero || c.Job == Job.Herrero);
            if (worker != null) Log("LIFE day of a worker: " + sim.Describe(worker, sim.Day).Replace("\n", " | "));
            var keeperC = sim.Citizens.Find(c => c.Job == Job.Tabernero);
            if (keeperC != null) Log("LIFE day of a barkeeper: " + sim.Describe(keeperC, sim.Day).Replace("\n", " | "));
            foreach (float h in new[] { 3f, 6.5f, 10f, 12.5f, 20f, 22f, 23.6f })
            {
                var cs = sim.Census(h);
                string line = "";
                foreach (var kv in cs) line += kv.Key + "=" + kv.Value + " ";
                int tav = 0, meet = 0, eatHome = 0;
                foreach (var c in sim.Citizens)
                {
                    var l = sim.WhereIs(c, h);
                    if (l.Walking) continue;
                    if (l.Activity == Activity.Taberna) tav++;
                    if (l.Activity == Activity.Reunion) meet++;
                    if (l.Activity == Activity.Comer && l.PlaceId == c.HomeId) eatHome++;
                }
                Log($"LIFE census {TownSimulation.Clock(h)}: {line} tavern={tav} with neighbours={meet} eating at home={eatHome}");
            }

            // ---- a home with a family: asleep at 2:00, at the table at 6:20 and at 22:00
            HouseInterior home = null; int people = 0;
            foreach (var h in town.Houses)
            {
                if (h.Floor != 0 || h.Lot == null || !h.Lot.IsHome || h.Lot.ForSale || h.Door == null) continue;
                var res = HomeOwners.Residents(h);
                int adults = res.FindAll(c => c.Job != Job.Guardia && c.Job != Job.Panadero && c.Job != Job.Tabernero).Count;
                if (adults == res.Count && res.Count > people) { people = res.Count; home = h; }
            }
            if (home != null)
            {
                foreach (var step in new[] { (2f, "05r_casa_durmiendo", Fighter.ActLie, "asleep in bed"), (6.35f, "05s_casa_desayuno", Fighter.ActChair, "at the table"), (22.2f, "05t_casa_cena", Fighter.ActChair, "at the table") })
                {
                    sim.Hour = step.Item1;
                    Teleport(pl, L.Fountain + new Vector3(3f, 0.1f, 3f));
                    yield return Wait(0.8f);
                    int atHome = 0;
                    foreach (var c in HomeOwners.Residents(home)) if (HomeOwners.AtHome(c, sim.Hour)) atHome++;
                    Teleport(pl, HomeOwners.DoorSpot(home), home.transform.position - HomeOwners.DoorSpot(home));
                    yield return Wait(3.5f);
                    Frame(home.transform.position + Vector3.up * 0.8f, -home.transform.forward + home.transform.right * 0.4f, Mathf.Max(home.HalfX, home.HalfZ) + 4.5f, 55f);
                    yield return Wait(0.8f);
                    yield return Shot(step.Item2);
                    Free();
                    Log($"LIFE home lot {home.Lot.Id} at {TownSimulation.Clock(sim.Hour)}: residents {people}, at home {atHome}, shown {town.Indoor.Count} (expected {atHome}), {step.Item4}={town.Indoor.Doing(step.Item3)}, standing={town.Indoor.Doing(-1)}");
                }
                // walking away: they are data again
                Teleport(pl, L.Fountain + new Vector3(3f, 0.1f, 3f));
                yield return Wait(1.0f);
                Log($"LIFE left the house: shown {town.Indoor.Count} (expected 0) focus={(town.Indoor.Focus != null)}");
            }
            else Log("LIFE no family home found");

            // ---- the tavern in the evening
            if (town.Services.Taverns.Count > 0)
            {
                var tv = town.Services.Taverns[0];
                sim.Hour = 20.3f;
                int should = 0;
                foreach (var c in sim.Citizens) { var l = sim.WhereIs(c, sim.Hour); var plc = sim.Place(l.PlaceId); if (!l.Walking && l.Indoors && plc != null && plc.LotId == tv.Lot.Id && c.Job != Job.Tabernero) should++; }
                Teleport(pl, tv.CounterFront + tv.Lot.Front * 3.2f + Vector3.up * 0.1f, -tv.Lot.Front);
                yield return Wait(5f);
                Frame(tv.transform.position + Vector3.up * 0.8f, -tv.Lot.Front + tv.Lot.Right * 0.35f, Mathf.Max(tv.HalfX, tv.HalfZ) + 4.5f, 52f);
                yield return Wait(0.8f);
                yield return Shot("05u_taberna_noche");
                Free();
                Log($"LIFE tavern lot {tv.Lot.Id} at 20:18: customers inside by the timetable {should}, shown {town.Indoor.Count}, seated={town.Indoor.Doing(Fighter.ActChair)}, standing={town.Indoor.Doing(-1)}");
            }

            // ---- the knots of neighbours in the plaza
            sim.Hour = 20.3f;
            Vector3 plaza = sim.Place("plaza").Position;
            Teleport(pl, plaza + Vector3.up * 0.1f, Vector3.forward);
            yield return Wait(7f);
            int meetBodies = 0, facing = 0;
            foreach (var n in town.Spawner.Active)
            {
                if (n.Citizen == null) continue;
                var l = sim.WhereIs(n.Citizen, sim.Hour);
                if (l.Activity != Activity.Reunion || l.Walking) continue;
                Vector3 mid = sim.GatherPoint(n.Citizen) - n.transform.position; mid.y = 0f;
                if (mid.magnitude > 3f) continue;
                meetBodies++;
                if (Vector3.Angle(n.transform.forward, mid) < 35f) facing++;
            }
            Frame(plaza + Vector3.up * 1f, new Vector3(0.3f, 0, 1f), 15f, 38f);
            yield return Wait(0.8f);
            yield return Shot("05v_plaza_reunion");
            Free();
            Log($"LIFE plaza at 20:18: neighbours standing in their ring {meetBodies}, facing its middle {facing}");

            // ---- the craftsmen: closed at night, open by day, the armourer hammering
            var ks = town.Keepers;
            sim.Hour = 19.05f;
            var ak = ks.All.Find(k => k.Hammers);
            Teleport(pl, ak.Post + L.Lots.Find(l => l.Kind == BuildingKind.Smithy).Front * 5f + Vector3.up * 0.1f, -L.Lots.Find(l => l.Kind == BuildingKind.Smithy).Front);
            float tk = Time.time;
            yield return Wait(0.5f);
            bool walking = !ak.AtPost && !ak.Inside;
            while (Time.time - tk < 16f && !ks.All.TrueForAll(k => k.Inside)) yield return null;
            int inside = ks.All.FindAll(k => k.Inside).Count;
            Teleport(pl, ak.Post + (ak.Rot * Vector3.forward) * 1.6f + Vector3.up * 0.1f, -(ak.Rot * Vector3.forward));
            yield return Wait(0.8f);
            yield return Shot("05w_armero_cerrado");
            var near = ks.Near(pl.transform.position, 2.5f);
            Log($"LIFE keepers at 19:03: {ks.All.Count} (expected 4) armourer walking home={walking} all inside after {Time.time - tk:0.0} s: {inside}; at the post: keeper={(near != null ? near.Trade : "-")} selling={(near != null && near.AtPost)} (expected False)");
            sim.Hour = 7.02f;
            tk = Time.time;
            while (Time.time - tk < 16f && !ks.All.TrueForAll(k => k.AtPost)) yield return null;
            int open = ks.All.FindAll(k => k.AtPost).Count;
            Log($"LIFE keepers at 7:01: at their posts after {Time.time - tk:0.0} s: {open} of {ks.All.Count}");
            sim.Hour = 10f;
            Teleport(pl, ak.Post + (ak.Rot * Vector3.forward) * 5.5f + Vector3.up * 0.1f, -(ak.Rot * Vector3.forward));
            int blows = 0;
            System.Action<Fighter, AttackType> onBlow = (f, a) => { if (f == ak.F) blows++; };
            Fighter.AnyAttackStarted += onBlow;
            Frame(ak.Post + Vector3.up * 1.1f, -(ak.Rot * Vector3.forward) + (ak.Rot * Vector3.right) * 0.4f, 4.5f, 12f);
            float tb = Time.time; bool shot = false;
            while (Time.time - tb < 9f) { if (!shot && blows > 0) { shot = true; yield return Wait(0.2f); yield return Shot("05x_armero_martillando"); } yield return null; }
            Fighter.AnyAttackStarted -= onBlow;
            Free();
            Log($"LIFE armourer at 10:00 with nobody at his table: hammer blows in 9 s = {blows} (expected 2 to 4)");

            sim.Hour = hour0;
            guards.Suspended = susp0;
            Teleport(pl, p0);
            yield return Wait(0.5f);
        }

        IEnumerator ServicesTest(TownBootstrap town)
        {
            var pl = town.Player;
            var cam = town.Cam;
            var sim = TownSimulation.Instance;
            var sv = town.Services;
            var guards = town.Guards;
            var prof = GameManager.Instance.Profile;
            var pc = pl.GetComponent<PlayerController>();
            Vector3 p0 = pl.transform.position;
            float hour0 = sim.Hour;
            bool susp0 = guards.Suspended;
            int money0 = prof.Denarii;
            guards.Suspended = true;
            if (sv == null || sv.Baths == null || sv.Forum == null) { Log($"SERVICES missing: services={(sv != null)} baths={(sv != null && sv.Baths != null)} forum={(sv != null && sv.Forum != null)}"); yield break; }
            prof.AddDenarii(500);
            int money = prof.Denarii;
            Vector3 lookAlong = Vector3.zero;
            void Look(float h, float dist, float pitch) { cam.FocusOverride = pl.transform.position + Vector3.up * h; cam.Distance = dist; cam.Pitch = pitch; if (lookAlong.sqrMagnitude > 0.01f) cam.Yaw = Quaternion.LookRotation(lookAlong).eulerAngles.y; }
            IEnumerator Hold(float s, float h, float dist, float pitch) { for (float t = 0f; t < s; t += Time.unscaledDeltaTime) { Look(h, dist, pitch); yield return null; } }

            // ---- the baths
            var b = sv.Baths;
            Log($"SERVICES baths lot {b.Lot.Id} {b.Lot.Width:0.0} x {b.Lot.Depth:0.0} m public={b.Public} poolSeats={b.PoolSpots.Count} keeper={(sv.BathKeeper != null ? sv.BathKeeper.Name : "-")} " +
                $"keeperInside={(sv.BathKeeper != null && b.Contains(sv.BathKeeper.transform.position))} roofHidesInside={(b.Upper != null)}");
            sim.Hour = 15f;
            lookAlong = -b.transform.forward;
            Teleport(pl, b.PoolStand + Vector3.up * 0.1f, -b.transform.forward);
            yield return Hold(0.8f, 1.0f, 6f, 40f);
            yield return Shot("07a_termas_interior");
            pl.Health.TakeDamage(pl.Health.Current - pl.Health.Max * 0.4f);
            float hp0 = pl.Health.Normalized, h0 = sim.Hour;
            bool inBaths = b.Contains(pl.transform.position);
            bool started = sv.TryBath();
            yield return Hold(2.6f, 0.6f, 4.5f, 35f);
            bool sitting = pl.InActivity && pl.OnFurniture;
            float inWater = (pl.transform.position - b.PoolCenter).magnitude;
            yield return Shot("07b_en_la_piscina");
            float tB = Time.time;
            while (sv.Bathing && Time.time - tB < 6f) { Look(0.6f, 4.5f, 35f); yield return null; }
            yield return Hold(1.2f, 0.8f, 4.5f, 35f);
            Log($"BATH inBaths={inBaths} started={started} sitting in the pool={sitting} ({inWater:0.0} m from its middle) paid={money - prof.Denarii} (expected {Boons.BathPrice}) " +
                $"hp {hp0:0.00}->{pl.Health.Normalized:0.00} (expected about +{Boons.BathHeal:0.00}) hours {h0:0.0}->{sim.Hour:0.0} (expected about +1) clockRate={sim.ClockRate} " +
                $"bonus waiting={Boons.Bathed} (expected True) out of the water={!pl.InActivity} controller={(pc == null || pc.enabled)}");
            // the bonus in the arena: a dummy with the player's numbers
            {
                var d = Gladiator.Arena.CharacterFactory.Create("Prueba", new Gladiator.Arena.GladiatorLook { Tunic = Color.gray, Skin = new Color(0.8f, 0.6f, 0.45f) }, pl.transform.position + Vector3.up * 30f, Quaternion.identity, 7);
                d.Health.SetMax(100f, true); d.MoveSpeed = 4.6f;
                Boons.ApplyArena(d);
                Log($"BATH bonus in the arena: health 100->{d.Health.Max:0} (expected {100f * Boons.BathHealth:0}) speed 4,60->{d.MoveSpeed:0.00} (expected {4.6f * Boons.BathSpeed:0.00})");
                Destroy(d.gameObject);
            }
            money = prof.Denarii;

            // ---- the basilica
            var fo = sv.Forum;
            Log($"SERVICES basilica lot {fo.Lot.Id} {fo.Lot.Width:0.0} x {fo.Lot.Depth:0.0} m public={fo.Public} scribe={(sv.Scribe != null ? sv.Scribe.Name : "-")} " +
                $"scribeInside={(sv.Scribe != null && fo.Contains(sv.Scribe.transform.position))} counterInside={fo.Contains(fo.CounterFront)}");
            lookAlong = -fo.transform.forward;
            Teleport(pl, fo.CounterFront + Vector3.up * 0.1f, -fo.transform.forward);
            yield return Hold(1.0f, 1.3f, 2.8f, 38f);
            bool handled = sv.Handle(pl.transform.position, "[E] ");
            yield return Shot("07c_basilica_interior");
            sv.OpenScribe();
            yield return Wait(0.4f);
            bool menu = Gladiator.UI.ChoiceMenu.Instance != null && Gladiator.UI.ChoiceMenu.Instance.IsOpen;
            yield return Shot("07d_menu_del_escriba");
            if (menu) Gladiator.UI.ChoiceMenu.Instance.Close();
            yield return Wait(0.3f);
            Log($"SCRIBE at the counter: prompt={handled} menu opens={menu} offers={TownServices.Offers.Count}");
            // fines
            lookAlong = Vector3.zero;
            guards.Suspended = false;
            Teleport(pl, town.Layout.Fountain + new Vector3(3f, 0.1f, 3f));
            yield return Wait(1.0f);
            guards.Offence("PRUEBA");
            int warned = guards.Warnings;
            int mF = prof.Denarii;
            bool paidFine = sv.PayFine();
            Log($"FINE warnings={warned} paid={paidFine} cost={mF - prof.Denarii} (expected {warned * TownServices.FinePerWarning}) warnings now={guards.Warnings} (expected 0) nothing owed pays={sv.PayFine()} (expected False)");
            guards.Suspended = true;
            // wagers
            int mW = prof.Denarii;
            bool bet = sv.PlaceWager(50), twice = sv.PlaceWager(25);
            int afterBet = prof.Denarii;
            Boons.SettleArena(prof, true);
            int afterWin = prof.Denarii;
            sv.PlaceWager(100);
            Boons.SettleArena(prof, false);
            Log($"WAGER placed={bet} a second one={twice} (expected False) staked={mW - afterBet} (expected 50) won: +{afterWin - afterBet} (expected 100) lost 100: {prof.Denarii - afterWin} (expected -100) " +
                $"result kept for the town={Boons.LastWagerResult} (expected -100) bath bonus spent={!Boons.Bathed} (expected True)");
            Boons.LastWagerResult = 0;

            // ---- errands
            sim.Hour = 10f;
            sv.EnsureOffers();
            string list = "";
            foreach (var e in TownServices.Offers) list += " [" + e.Kind + " " + e.Reward + ": " + e.Title + "]";
            Log("ERRANDS today:" + list);
            foreach (var e in new System.Collections.Generic.List<Errand>(TownServices.Offers))
            {
                int mE = prof.Denarii;
                bool took = sv.Accept(e);
                yield return Wait(0.3f);
                var target = sv.ErrandTarget();
                Errand other = null;
                foreach (var x in TownServices.Offers) if (x != e && !x.Done) other = x;
                bool second = other != null && sv.Accept(other);
                if (!took || !target.HasValue) { Log($"ERRAND {e.Kind}: taken={took} target={target.HasValue}"); continue; }
                if (e.Kind == ErrandKind.Parcel)
                {
                    Teleport(pl, target.Value + Vector3.up * 0.1f + town.HouseOfLot(e.LotId).transform.forward * 6f);
                    yield return Hold(0.8f, 1.6f, 7f, 14f);
                    yield return Shot("07e_encargo_paquete_luz");
                    bool early = TownServices.Active == null;
                    Teleport(pl, target.Value + Vector3.up * 0.1f);
                    yield return Wait(0.6f);
                    Log($"ERRAND parcel: {e.Title} taken={took} another at once={second} (expected False) done before arriving={early} (expected False) at the door: outcome={sv.LastErrandOutcome} (expected done) paid={prof.Denarii - mE} (expected {e.Reward})");
                }
                else if (e.Kind == ErrandKind.Message)
                {
                    Teleport(pl, target.Value + Vector3.up * 0.1f + new Vector3(1.2f, 0f, 0.8f));
                    float tM = Time.time; bool body = false;
                    while (Time.time - tM < 8f && !body)
                    {
                        foreach (var n in town.Spawner.Active) if (n.Citizen != null && n.Citizen.Id == e.CitizenId) body = true;
                        yield return null;
                    }
                    var tgt = sv.ErrandTarget();
                    bool farTry = false;
                    string prompt = sv.IdlePrompt;
                    if (tgt.HasValue)
                    {
                        Teleport(pl, tgt.Value + Vector3.up * 0.1f + new Vector3(6f, 0f, 0f));
                        yield return Wait(0.2f);
                        farTry = sv.TryDeliver();
                        tgt = sv.ErrandTarget();
                        Teleport(pl, tgt.Value + Vector3.up * 0.1f + new Vector3(1.3f, 0f, 0.4f));
                        yield return Hold(0.5f, 1.2f, 5f, 16f);
                        yield return Shot("07f_encargo_recado");
                    }
                    bool gave = sv.TryDeliver();
                    Log($"ERRAND message: {e.Title} taken={took} recipient has a body={body} from 6 m={farTry} (expected False) next to them={gave} outcome={sv.LastErrandOutcome} (expected done) paid={prof.Denarii - mE} (expected {e.Reward}) prompt='{prompt}'");
                }
                else if (e.Kind == ErrandKind.Drunk)
                {
                    var dr = sv.Drunk;
                    if (dr == null) { Log("ERRAND drunk: no drunk"); continue; }
                    pl.Health.Heal(pl.Health.Max);
                    Teleport(pl, dr.transform.position + Vector3.up * 0.1f + new Vector3(2.4f, 0f, 0f));
                    float hpP = pl.Health.Current;
                    lookAlong = Vector3.zero;
                    yield return Hold(1.2f, 1.0f, 6f, 40f);
                    yield return Shot("07g2_borracho_llega");
                    yield return Hold(5.0f, 1.0f, 6f, 40f);
                    var ai = dr.GetComponent<EnemyAI>();
                    yield return Shot("07g_encargo_borracho");
                    Log($"ERRAND drunk: {e.Title} taken={took} at the tavern door, picks a fight={(ai != null && ai.enabled)} target is the player={(ai != null && ai.Target == pl)} player hp {hpP:0}->{pl.Health.Current:0} in 6 s");
                    Vector3 dir = dr.transform.position - pl.transform.position; dir.y = 0f;
                    dr.ReceiveHit(pl, 999f, AttackType.Heavy, dir.normalized);
                    yield return Hold(1.5f, 0.6f, 5.5f, 30f);
                    Log($"ERRAND drunk beaten: alive={dr.IsAlive} knockedOut={dr.KnockedOut} ragdolled={dr.Ragdolled} outcome={sv.LastErrandOutcome} (expected done) paid={prof.Denarii - mE} (expected {e.Reward}) guards' warnings={guards.Warnings} (expected 0)");
                    yield return Shot("07h_borracho_tumbado");
                }
            }
            int done = 0; foreach (var e in TownServices.Offers) if (e.Done) done++;
            Log($"ERRANDS done today={done}/{TownServices.Offers.Count} active={(TownServices.Active != null)} (expected False) taking a done one again={sv.Accept(TownServices.Offers[0])} (expected False)");

            prof.AddDenarii(money0 - prof.Denarii);
            guards.Suspended = susp0;
            sim.Hour = hour0;
            pl.Health.Heal(pl.Health.Max);
            Teleport(pl, p0);
            if (pc != null) pc.enabled = true;
            cam.FocusOverride = null;
            yield return Wait(0.3f);
        }

        /// <summary>R puts the weapon away and draws it; a blow draws it; where it hangs (belt, back); the house windows.</summary>
        IEnumerator SheathWindowsTest(TownBootstrap town)
        {
            var pl = town.Player;
            var cam = town.Cam;
            var pc = pl.GetComponent<PlayerController>();
            if (pc != null) pc.enabled = false;
            Vector3 p0 = pl.transform.position;
            Quaternion r0 = pl.transform.rotation;
            WeaponStats w0 = pl.CurrentWeapon != null ? pl.CurrentWeapon.Stats : WeaponLibrary.Rudis;
            // out in the ludus yard, where the camera has room
            Teleport(pl, town.Ludus.TransformPoint(new Vector3(0f, 0.05f, -5f)), town.Ludus.forward);
            yield return Wait(0.4f);
            void CleanGround() { foreach (var gw in new System.Collections.Generic.List<Weapon>(Weapon.GroundWeapons)) if (gw != null) Destroy(gw.gameObject); }
            Log($"SHEATH start in town: sheathed={pl.Sheathed} inHand={pl.WeaponInHand} weapon={(pl.CurrentWeapon != null ? pl.CurrentWeapon.Stats.Name : "-")} fights with={pl.ActiveStats.Name}");
            pl.Equip(WeaponFactory.Create(WeaponLibrary.Gladius));
            pl.SetSheathed(true, true);
            yield return HoldCam(cam, pl, 0.6f, 0.95f, 2.4f, 32f, -50f);
            Vector3 hip = pl.CurrentWeapon.transform.position - pl.transform.position;
            Log($"SHEATH gladius put away: sheathed={pl.Sheathed} inHand={pl.WeaponInHand} fights with={pl.ActiveStats.Name} (expected Puños) " +
                $"hangs at height {hip.y:0.00} m, {Vector3.Dot(hip, pl.transform.right):0.00} m to the right (expected about 1.0 and -0.2: the left hip) " +
                $"points down={Vector3.Dot(pl.CurrentWeapon.transform.forward, Vector3.down):0.00} (expected about 0.9)");
            yield return Shot("04a_enfundada_cintura");
            // R: draw
            bool drew = pl.ToggleSheath();
            yield return HoldCam(cam, pl, Fighter.SheathSeconds * 0.42f, 0.95f, 2.4f, 32f, -50f);
            float midPhase = pl.SheathPhase;
            float handToHip = (pl.GetComponentInChildren<Gladiator.Character.HumanoidRig>().Bones[Gladiator.Character.SomaSkeleton.RightHand].position - pl.CurrentWeapon.transform.position).magnitude;
            yield return Shot("04b_desenfundando");
            yield return HoldCam(cam, pl, Fighter.SheathSeconds * 0.75f, 0.95f, 2.4f, 32f, -50f);
            Log($"SHEATH R draws: started={drew} mid phase={midPhase:0.00} hand to the grip then={handToHip:0.00} m (expected under 0.2) -> sheathed={pl.Sheathed} inHand={pl.WeaponInHand} fights with={pl.ActiveStats.Name} (expected Gladius)");
            yield return Shot("04c_en_la_mano");
            // R: put away again, then a blow draws it by itself
            bool put = pl.ToggleSheath();
            yield return HoldCam(cam, pl, Fighter.SheathSeconds + 0.2f, 0.95f, 2.4f, 32f, -50f);
            bool away = pl.Sheathed && !pl.WeaponInHand;
            int swings = 0;
            System.Action<Fighter, AttackType> onSwing = (f, a) => { if (f == pl) swings++; };
            Fighter.AnyAttackStarted += onSwing;
            bool asked = pl.TryAttack(AttackType.Light);
            yield return HoldCam(cam, pl, 0.12f, 0.95f, 2.4f, 32f, -50f);
            bool drawing = pl.SheathPhase >= 0f && pl.State == FighterState.Idle;
            float tA = Time.time;
            while (swings == 0 && Time.time - tA < 2f) yield return null;
            float delay = Time.time - tA + 0.12f;
            Log($"SHEATH put away with R={put} away={away}; attack: accepted={asked} drawing={drawing} (expected False) swing started after {delay:0.00} s (expected about 0.12) " +
                $"with={pl.AttackStats.Name} (expected Puños) inHand={pl.WeaponInHand} sheathed={pl.Sheathed} (expected False, True) swings={swings}");
            Fighter.AnyAttackStarted -= onSwing;
            yield return Shot("06c_punos_con_arma_enfundada");
            yield return HoldCam(cam, pl, 1.2f, 0.95f, 2.4f, 32f, -50f);
            // two-handers, spears and the bow go across the back
            foreach (var ws in new[] { WeaponLibrary.Greatsword, WeaponLibrary.Spear, WeaponLibrary.Bow })
            {
                pl.Equip(WeaponFactory.Create(ws));
                CleanGround();
                yield return HoldCam(cam, pl, 0.4f, 1.1f, 3.2f, 10f, 150f);
                pl.ToggleSheath();
                yield return HoldCam(cam, pl, Fighter.SheathSeconds * 0.45f, 1.1f, 3.2f, 10f, 150f);
                if (ws == WeaponLibrary.Greatsword) yield return Shot("04d_guardando_mandoble");
                yield return HoldCam(cam, pl, Fighter.SheathSeconds * 0.8f, 1.1f, 3.2f, 10f, 150f);
                Vector3 off = pl.CurrentWeapon.transform.position - pl.transform.position;
                Log($"SHEATH {ws.Name}: sheathed={pl.Sheathed} inHand={pl.WeaponInHand} twoHanded={pl.TwoHanded} (expected False) grip at height {off.y:0.00} m, " +
                    $"{Vector3.Dot(off, pl.transform.forward):0.00} m forward (expected negative: on the back)");
                yield return Shot(ws == WeaponLibrary.Greatsword ? "04e_mandoble_espalda" : ws == WeaponLibrary.Spear ? "04f_lanza_espalda" : "04g_arco_espalda");
            }
            pl.Equip(WeaponFactory.Create(w0));
            CleanGround();
            pl.SetSheathed(true, true);

            // the townsfolk: guards and whoever carries a knife have it put away
            int armedNpc = 0, awayNpc = 0;
            CitizenNPC aGuard = null;
            foreach (var n in town.Spawner.Active)
            {
                if (n.Fighter.CurrentWeapon == null) continue;
                armedNpc++;
                if (n.Fighter.Sheathed && !n.Fighter.WeaponInHand) awayNpc++;
                if (n.Citizen != null && n.Citizen.Job == Job.Guardia) aGuard = n;
            }
            Log($"SHEATH townsfolk carrying a weapon={armedNpc} put away={awayNpc} (expected all)");
            if (aGuard != null)
            {
                yield return HoldCam(cam, aGuard.Fighter, 0.5f, 0.95f, 2.6f, 32f, -50f);
                yield return Shot("04h_guardia_enfundado");
            }

            // ---- windows: an opening with glass in the front wall of every home you can walk into
            int homes = 0, withWindows = 0, panes = 0;
            HouseInterior show = null;
            foreach (var h in town.Houses)
            {
                if (h.Rooms.Count == 0 || h.Lot == null) continue;
                homes++;
                if (h.Windows.Count > 0) { withWindows++; panes += h.Windows.Count; if (show == null && h.Floor == 0 && !h.Lot.ForSale) show = h; }
            }
            Log($"WINDOWS homes={homes} with windows={withWindows} panes={panes}");
            if (show != null)
            {
                Vector3 wc = show.Windows[0], fwd = show.transform.forward;
                // a ray through the middle of the window: nothing but the glass in the way, and the room behind it
                bool hitGlass = Physics.Raycast(wc + fwd * 1.5f, -fwd, out RaycastHit hg, 1.6f, Ragdoll.WorldMask, QueryTriggerInteraction.Ignore);
                bool wallBeside = Physics.Raycast(wc + fwd * 1.5f + show.transform.right * 0.6f, -fwd, out RaycastHit hw, 1.6f, Ragdoll.WorldMask, QueryTriggerInteraction.Ignore);
                bool seeThrough = false;
                foreach (var mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    if (mr.sharedMaterial != null && mr.sharedMaterial.shader != null && mr.sharedMaterial.shader.name == "Gladiator/PS2_Glass") { seeThrough = mr.sharedMaterial.color.a < 0.5f || mr.sharedMaterial.GetColor("_BaseColor").a < 0.5f; break; }
                Log($"WINDOWS house {show.Lot.Id}: pane at height {wc.y - show.transform.position.y:0.00} m, blocks the way={hitGlass} (expected True: nobody climbs in) " +
                    $"wall next to it={wallBeside} glass material translucent={seeThrough} (expected True)");
                // from the street
                Teleport(pl, wc + fwd * 3.2f - Vector3.up * (wc.y - show.transform.position.y) + Vector3.up * 0.1f, fwd);
                yield return HoldCam(cam, pl, 0.8f, 1.5f, 3.2f, 4f, 12f);
                yield return Shot("05a_ventana_desde_fuera");
                // from the kitchen, looking at the front wall
                Vector3 inside = show.RoomCenter("COCINA");
                Teleport(pl, inside, -fwd);
                yield return Wait(0.6f);
                yield return HoldCam(cam, pl, 0.8f, 1.5f, 2.6f, 2f, 0f);
                yield return Shot("05b_ventana_desde_dentro");
            }
            Teleport(pl, p0, r0 * Vector3.forward);
            cam.FocusOverride = null;
            if (pc != null) pc.enabled = true;
            yield return Wait(0.3f);
        }

        /// <summary>Asleep in somebody else's bed: the owner comes home, wakes you, gives you 15 seconds, then attacks and the
        /// watch comes in; knocked out you wake in the infirmary a day later. Leaving in time ends it. Arena hours.</summary>
        IEnumerator HomeOwnerTest(TownBootstrap town)
        {
            var pl = town.Player;
            var cam = town.Cam;
            var sim = TownSimulation.Instance;
            var tm = town.GetComponent<TownManager>();
            var owners = town.Owners;
            var guards = town.Guards;
            var gm = GameManager.Instance;
            var pc = pl.GetComponent<PlayerController>();
            Vector3 p0 = pl.transform.position;
            float hour0 = sim.Hour;
            bool susp0 = guards.Suspended;

            // a home that is empty at some hour and gets somebody back a little later
            HouseInterior home = null; float arrives = -1f; CitizenData who = null;
            foreach (var h in town.Houses)
            {
                if (h.Floor != 0 || !owners.Foreign(h) || !h.Furniture.Exists(f => f.Kind == Fighter.ActLie)) continue;
                var res = HomeOwners.Residents(h);
                if (res.Count == 0) continue;
                for (float t = 9f; t < 21f && home == null; t += 0.25f)
                {
                    bool emptyBefore = true; CitizenData back = null;
                    foreach (var c in res)
                    {
                        if (HomeOwners.AtHome(c, t - 0.25f) || HomeOwners.AtHome(c, t - 1.25f) || HomeOwners.AtHome(c, t - 0.75f)) emptyBefore = false;
                        else if (HomeOwners.AtHome(c, t)) back = c;
                    }
                    if (emptyBefore && back != null) { home = h; arrives = t; who = back; }
                }
                if (home != null) break;
            }
            if (home == null) { Log("OWNER no home found that empties and fills again"); yield break; }
            guards.Suspended = false;
            sim.Hour = arrives - 1.2f;
            var bed = home.Furniture.Find(f => f.Kind == Fighter.ActLie);
            Teleport(pl, bed.At.position + Vector3.up * 0.1f, home.transform.forward);
            yield return Wait(0.3f);
            bool lay = tm.UseFurniture(bed);
            yield return Wait(3.2f);
            float hSleep = sim.Hour;
            bool slept = tm.StartSleep(6);
            float t0 = Time.time;
            while (owners.Owner == null && Time.time - t0 < 8f)
            {
                cam.FocusOverride = pl.transform.position + Vector3.up * 0.6f; cam.Distance = 4.5f; cam.Pitch = 38f;
                yield return null;
            }
            yield return Wait(0.4f);
            var own = owners.Owner;
            Log($"OWNER house {home.Lot.Id} of {who.Name} (back at {TownSimulation.Clock(arrives)}): lay={lay} canSleep={tm.CanSleepIn(home)} sleepStarted={slept} at {TownSimulation.Clock(hSleep)}; " +
                $"woken at {TownSimulation.Clock(sim.Hour)} by={(own != null && own.Citizen != null ? own.Citizen.Name : "nobody")} stillSleeping={tm.Sleeping} (expected False) clockRate={sim.ClockRate} " +
                $"seconds to leave={owners.TimeLeft:0.0} (expected about {HomeOwners.Grace:0})" +
                (own != null ? $" owner {(own.transform.position - pl.transform.position).magnitude:0.0} m away, same room={(home.RoomAt(own.transform.position) == home.RoomAt(pl.transform.position))} weapon put away={(own.Fighter.CurrentWeapon == null || own.Fighter.Sheathed)}" : ""));
            for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime) { cam.FocusOverride = pl.transform.position + Vector3.up * 0.6f; cam.Distance = 4.5f; cam.Pitch = 38f; yield return null; }
            yield return Shot("06a_dueno_te_despierta");
            if (own == null) { guards.Suspended = susp0; sim.Hour = hour0; if (pl.InActivity) pl.StopActivity(); Teleport(pl, p0); cam.FocusOverride = null; yield break; }

            // stay: after the 15 seconds the owner attacks and the watch is in the house
            float tStay = Time.time;
            while (!owners.Attacking && Time.time - tStay < HomeOwners.Grace + 6f)
            {
                cam.FocusOverride = pl.transform.position + Vector3.up * 0.6f; cam.Distance = 4.5f; cam.Pitch = 38f;
                yield return null;
            }
            float waited = Time.time - tStay + 1.6f;
            for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime) { cam.FocusOverride = pl.transform.position + Vector3.up * 0.9f; cam.Distance = 5f; cam.Pitch = 42f; yield return null; }
            int inHouse = 0;
            foreach (var n in town.Spawner.Active)
                if (n.Citizen != null && n.Citizen.Job == Job.Guardia && n.Controlled && home.Contains(n.transform.position)) inHouse++;
            Log($"OWNER stayed: attacking={owners.Attacking} after {waited:0.0} s (expected about {HomeOwners.Grace:0}) outcome={owners.LastOutcome} owner's knife out={(own.Fighter.WeaponInHand)} harmless={own.Fighter.Harmless} " +
                $"guards called={owners.GuardsCalled} fighting={guards.Fighting} (expected 2) inside the house={inHouse} hostile={(guards.Hostile != null)} playerUp={!pl.InActivity}");
            yield return Shot("06b_dueno_ataca_y_guardias");
            // let them at it for a moment, then the knockout: a day goes by
            float hpA = pl.Health.Current;
            for (float t = 0f; t < 4f; t += Time.unscaledDeltaTime) { cam.FocusOverride = pl.transform.position + Vector3.up * 0.9f; cam.Distance = 5f; cam.Pitch = 42f; yield return null; }
            yield return Shot("06c_pelea_en_la_casa");
            float hpB = pl.Health.Current;
            int dayA = sim.Day; float hourA = sim.Hour;
            pl.Health.TakeDamage(Mathf.Max(0f, pl.Health.Current - pl.Health.Max * 0.2f));
            float tK = Time.time;
            while ((guards.LastOutcome != "knockout" || guards.OutCold || guards.Hostile != null) && Time.time - tK < 12f) yield return null;
            yield return Wait(1.0f);
            var inf = town.Infirmary;
            float passed = (sim.Day - dayA) * 24f + sim.Hour - hourA;
            Log($"OWNER fight: player hp {hpA:0}->{hpB:0} in 4 s; knocked out: outcome={guards.LastOutcome} day {dayA}->{sim.Day} hour {TownSimulation.Clock(hourA)}->{TownSimulation.Clock(sim.Hour)} " +
                $"hours passed={passed:0.0} (expected about {TownGuards.HoursOut:0}) inInfirmary={(inf != null && inf.Contains(pl.transform.position))} hp={pl.Health.Current:0}/{pl.Health.Max:0} guardsFighting={guards.Fighting} (expected 0)");
            cam.FocusOverride = null;
            yield return Shot("06d_enfermeria_un_dia_despues");
            float tD = Time.time;
            while (owners.Owner != null && Time.time - tD < 10f) yield return null;
            Log($"OWNER afterwards: owner's body gone={owners.Owner == null}");
            if (pl.InActivity) pl.StopActivity();
            yield return Wait(2.2f);

            // leaving in time: nothing happens
            Vector3 kitchen = home.RoomCenter("COCINA");
            Teleport(pl, kitchen, -home.transform.forward);
            yield return Wait(0.3f);
            bool again = owners.Confront(home, who);
            yield return Wait(2f);
            float left0 = owners.TimeLeft;
            Teleport(pl, home.Door.Center + home.transform.forward * 3.5f + Vector3.up * 0.1f, home.transform.forward);
            yield return Wait(1.5f);
            Log($"OWNER second time: confronted={again} time left after 2 s={left0:0.0} then out of the door: outcome={owners.LastOutcome} (expected left) attacking={owners.Attacking} (expected False) guardsFighting={guards.Fighting} (expected 0)");
            Teleport(pl, home.Door.Center + home.transform.forward * 14f + Vector3.up * 0.1f, home.transform.forward);
            yield return Wait(1.0f);
            Log($"OWNER walked away: owner's body gone={owners.Owner == null} (expected True)");

            // ---- the arena: 8:00 to 20:00
            bool Gate(float hour)
            {
                sim.Hour = hour;
                tm.OpenArenaGate();
                var m = Gladiator.UI.ArenaGateMenu.Instance;
                bool open = m != null && m.IsOpen;
                if (open) m.Close();
                return open;
            }
            bool fought = gm.DayCycle.FoughtToday;
            Log($"ARENA HOURS foughtToday={fought} gate opens at 07:30={Gate(7.5f)} (expected False) 08:00={Gate(8f)} (expected {!fought}) 13:00={Gate(13f)} (expected {!fought}) " +
                $"19:59={Gate(19.98f)} (expected {!fought}) 20:00={Gate(20f)} (expected False) 23:00={Gate(23f)} (expected False)");
            sim.Hour = 10f;
            sim.AdvanceTo(Gladiator.Progression.DayCycleManager.ArenaCloses);
            float afterFight = sim.Hour;
            sim.Hour = 21f; sim.AdvanceTo(Gladiator.Progression.DayCycleManager.ArenaCloses);
            Log($"ARENA HOURS a fight at 10:00 ends at {TownSimulation.Clock(afterFight)} (expected 20:00); the clock never goes back: 21:00 stays {TownSimulation.Clock(sim.Hour)}");

            guards.Suspended = susp0;
            sim.Hour = hour0;
            pl.Health.Heal(pl.Health.Max);
            Teleport(pl, p0);
            if (pc != null) pc.enabled = true;
            cam.FocusOverride = null;
            yield return Wait(0.3f);
        }

        /// <summary>Rag doll: a dummy is killed with a blow and falls with physics; the player is knocked out and comes round.</summary>
        IEnumerator RagdollTest(TownBootstrap town)
        {
            var pl = town.Player;
            var cam = town.Cam;
            Ragdoll.Measure = true;
            Ragdoll.KneeMin = Ragdoll.KneeMax = Ragdoll.ElbowMin = Ragdoll.ElbowMax = 0f;
            int before = Ragdoll.Activations;
            Vector3 fwd = pl.transform.forward;
            for (int n = 0; n < 2; n++)
            {
                Vector3 at = pl.transform.position + fwd * 2.4f + pl.transform.right * (n == 0 ? -0.8f : 0.9f);
                var look = new Gladiator.Arena.GladiatorLook { Tunic = n == 0 ? new Color(0.6f, 0.1f, 0.1f) : new Color(0.15f, 0.3f, 0.6f), Skin = new Color(0.8f, 0.6f, 0.45f), Helmet = n == 0, Crest = Color.red };
                var d = Gladiator.Arena.CharacterFactory.Create(n == 0 ? "Muneco A" : "Muneco B", look, at, Quaternion.LookRotation(-fwd), 7);
                d.Health.SetMax(40f);
                d.Equip(WeaponFactory.Create(WeaponLibrary.Dagger));
                if (n == 1) Shield.Attach(d, ShieldLibrary.Scutum, Color.white);
                yield return HoldCam(cam, d, 1.0f, 0.9f, 5.5f, 14f, 70f);
                Vector3 start = d.transform.position;
                // A: a heavy blow from the front; B: a kick from the side
                Vector3 dir = n == 0 ? fwd : (pl.transform.right + fwd * 0.3f).normalized;
                d.ReceiveHit(null, 999f, n == 0 ? AttackType.Heavy : AttackType.Kick, dir);
                Log($"RAGDOLL {d.DisplayName} dead={!d.IsAlive} ragdolled={d.Ragdolled} activations={Ragdoll.Activations - before}");
                yield return HoldCam(cam, d, 0.22f, 0.9f, 5.5f, 14f, 70f);
                yield return Shot(n == 0 ? "00a_ragdoll_golpe" : "00d_ragdoll_patada");
                yield return HoldCam(cam, d, 0.4f, 0.7f, 5.5f, 16f, 70f);
                yield return Shot(n == 0 ? "00b_ragdoll_cayendo" : "00e_ragdoll_cayendo");
                yield return HoldCam(cam, d, 3.6f, 0.4f, 5.5f, 22f, 70f);
                yield return Shot(n == 0 ? "00c_ragdoll_suelo" : "00f_ragdoll_suelo");
                var rg = d.Rag;
                float hipsUp = rg != null && rg.Hips != null ? rg.Hips.position.y - d.transform.position.y : -1f;
                float chestUp = rg != null && rg.Chest != null ? rg.Chest.position.y - d.transform.position.y : -1f;
                Vector3 moved = d.transform.position - start; moved.y = 0f;
                Log($"RAGDOLL {d.DisplayName} rest: hips {hipsUp:0.00} m and chest {chestUp:0.00} m above the ground (expected under 0.45), slid {moved.magnitude:0.00} m " +
                    $"along the blow {Vector3.Dot(moved, dir):0.00} m, settled={(rg != null && rg.Settled)} weaponDropped={d.CurrentWeapon == null}");
            }
            Log($"RAGDOLL joints: knees {Ragdoll.KneeMin:0}..{Ragdoll.KneeMax:0} deg (expected about -5..140: never forwards), right elbow {Ragdoll.ElbowMin:0}..{Ragdoll.ElbowMax:0} deg (expected about -145..5)");

            // the player knocked out, then coming round
            Vector3 p0 = pl.transform.position;
            bool ko = pl.KnockDown(-fwd, 2.2f);
            yield return HoldCam(cam, pl, 1.3f, 0.5f, 5f, 20f, 70f);
            Log($"RAGDOLL knockout: started={ko} state={pl.State} ragdolled={pl.Ragdolled} controller={pl.Controller.enabled} alive={pl.IsAlive}");
            yield return Shot("00g_inconsciente");
            yield return HoldCam(cam, pl, 1.15f, 0.6f, 5f, 18f, 70f);
            yield return Shot("00h_se_levanta");
            yield return HoldCam(cam, pl, 1.0f, 0.9f, 5f, 14f, 70f);
            Vector3 mv = pl.transform.position - p0; mv.y = 0f;
            Log($"RAGDOLL awake: state={pl.State} ragdolled={pl.Ragdolled} controller={pl.Controller.enabled} moved {mv.magnitude:0.00} m hp={pl.Health.Current:0}");
            yield return Shot("00i_de_pie");
            Ragdoll.Measure = false;
            cam.FocusOverride = null;
        }

        IEnumerator GuardsTest(TownBootstrap town)
        {
            var pl = town.Player;
            var guards = town.Guards;
            var gcam = town.Cam;
            // stand near a guard with a body
            CitizenNPC guard = null;
            for (int tries = 0; tries < 30 && guard == null; tries++)
            {
                foreach (var n in town.Spawner.Active)
                    if (n.Citizen != null && n.Citizen.Job == Job.Guardia && !n.GoingIn) { guard = n; break; }
                if (guard == null) yield return Wait(0.5f);
            }
            if (guard == null) { Log("GUARDS no guard with a body near"); yield break; }
            Vector3 gp = guard.transform.position;
            TownNavMesh.Snap(gp + (gp - town.Layout.Fountain).normalized * -8f, out Vector3 near, 6f);
            Teleport(pl, near + Vector3.up * 0.05f);
            yield return HoldCam(gcam, pl, 1.5f, 1.2f, 7f, 30f);
            // 1) hit a townsperson
            CitizenNPC victim = null;
            foreach (var n in town.Spawner.Active) if (n.Citizen != null && n.Citizen.Job != Job.Guardia) { victim = n; break; }
            if (victim != null) victim.Fighter.ReceiveHit(pl, 5f, AttackType.Light, (victim.transform.position - pl.transform.position).normalized);
            else guards.Offence("test");
            Log($"GUARDS after hit: warnings={guards.Warnings} warner={(guards.Warner != null ? guards.Warner.Citizen.Name : "none")} victimFled={(victim != null && victim.Fleeing)}");
            float t0 = Time.time;
            float startD = guards.Warner != null ? (guards.Warner.transform.position - pl.transform.position).magnitude : -1f;
            while (guards.Warner != null && Time.time - t0 < 10f && (guards.Warner.transform.position - pl.transform.position).magnitude > 2.6f) yield return HoldCam(gcam, pl, 0.1f, 1.2f, 7f, 30f);
            float endD = guards.Warner != null ? (guards.Warner.transform.position - pl.transform.position).magnitude : -1f;
            yield return HoldCam(gcam, pl, 1.0f, 1.2f, 6f, 25f);
            Log($"GUARDS warner came {startD:0.0}m -> {endD:0.0}m in {Time.time - t0:0.0}s state={(guards.Warner != null ? guards.Warner.Fighter.State.ToString() : "-")}");
            yield return Shot("02a_guardia_advierte");
            yield return Wait(3.2f);
            // 2) walk into a house that is not ours
            HouseInterior house = null;
            foreach (var h in town.Houses) if (h.Floor == 0 && !h.Public && !HouseDeeds.Owns(town.Layout.Seed, h.Lot.Id)) { house = h; break; }
            if (house != null)
            {
                Teleport(pl, house.RoomCenter("COCINA") + Vector3.up * 0.05f);
                yield return Wait(0.5f);
                Log($"GUARDS in a house: warnings={guards.Warnings} inHouse={(town.HouseAt(pl.transform.position) != null)}");
                yield return Wait(3.5f);
                Teleport(pl, near + Vector3.up * 0.05f);
                yield return Wait(1f);
            }
            // 3) third offence: the fight
            if (guards.Warner != null) yield return Wait(3f);
            yield return Wait(3.2f);
            guards.Offence("test 3");
            Log($"GUARDS third: warnings={guards.Warnings} hostile={(guards.Hostile != null ? guards.Hostile.Citizen.Name : "none")}");
            float hp0 = pl.Health.Current;
            for (float t = 0f; t < 8f && guards.Hostile != null; t += 0.1f) yield return HoldCam(gcam, pl, 0.1f, 1.1f, 6f, 25f);
            Log($"GUARDS fight 8s: playerHP {hp0:0}->{pl.Health.Current:0} hostile={(guards.Hostile != null)} guardHarmless={(guards.Hostile != null && guards.Hostile.Fighter.Harmless)}");
            yield return Shot("02b_guardia_pelea");
            // 4) knockout (take the player down to the threshold)
            if (guards.Hostile != null)
            {
                pl.Health.TakeDamage(pl.Health.Current - pl.Health.Max * 0.2f);
                yield return HoldCam(gcam, pl, 1.2f, 0.5f, 6f, 22f);
                Log($"GUARDS knocked out: outCold={guards.OutCold} ragdolled={pl.Ragdolled} state={pl.State}");
                yield return Shot("02b2_inconsciente");
                yield return Wait(TownGuards.OutSeconds);
                Log($"GUARDS came round: outCold={guards.OutCold} ragdolled={pl.Ragdolled} state={pl.State}");
            }
            Log($"GUARDS end: outcome={guards.LastOutcome} warnings={guards.Warnings} hp={pl.Health.Current:0}/{pl.Health.Max:0} alive={pl.IsAlive} " +
                $"inInfirmary={(town.Infirmary != null && town.Infirmary.Contains(pl.transform.position))}");
            yield return HoldCam(gcam, pl, 1f, 1.2f, 7f, 30f);
            yield return Shot("02c_despiertas");
            gcam.FocusOverride = null;
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "autotest_town_" + name + ".png");
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Log("SCREENSHOT " + path);
        }

        IEnumerator Start()
        {
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            GameSettings.CameraMode = 0;
            GameSettings.GraphicsPreset = 0;
            GameSettings.Apply(false);
            yield return Wait(1.5f);
            GameManager.Ensure();
            SceneManager.LoadScene(TownBootstrap.SceneName);
            float t0 = Time.realtimeSinceStartup;
            while (TownBootstrap.Instance == null && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            var town = TownBootstrap.Instance;
            if (town == null) { Log("TOWN NOT LOADED"); Quit(); yield break; }
            Log($"LOADED in {Time.realtimeSinceStartup - t0:0.0}s seed={town.Layout.Seed} rot={town.Layout.RotationDeg} mirrored={town.Layout.Mirrored}");
            var counts = new System.Collections.Generic.Dictionary<BuildingKind, int>();
            foreach (var lot in town.Layout.Lots) counts[lot.Kind] = counts.TryGetValue(lot.Kind, out int n) ? n + 1 : 1;
            Log("LOTS " + town.Layout.Lots.Count + " [" + string.Join(", ", System.Linq.Enumerable.Select(counts, kv => kv.Key + "=" + kv.Value)) + "]");
            Log($"PLACES {town.Layout.Places.Count} homes={town.Layout.Places.FindAll(p => p.Kind == PlaceKind.Home).Count} " +
                $"work={town.Layout.Places.FindAll(p => p.Kind == PlaceKind.Work).Count} stalls={town.Layout.Stalls.Count} trees={town.Layout.Trees.Count}");
            int renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length;
            int cols = FindObjectsByType<Collider>(FindObjectsSortMode.None).Length;
            Log($"SCENE renderers={renderers} colliders={cols} fps~{1f / Mathf.Max(0.001f, Time.smoothDeltaTime):0}");

            // ---- phase 2: the citizens as data
            var sim = TownSimulation.Instance;
            var jobs = new System.Collections.Generic.Dictionary<Job, int>();
            foreach (var c in sim.Citizens) jobs[c.Job] = jobs.TryGetValue(c.Job, out int jn) ? jn + 1 : 1;
            var fams = new System.Collections.Generic.HashSet<string>(); var homesUsed = new System.Collections.Generic.HashSet<string>();
            foreach (var c in sim.Citizens) { fams.Add(c.Family); homesUsed.Add(c.HomeId); }
            Log($"CITIZENS {sim.Citizens.Count} families={fams.Count} homesUsed={homesUsed.Count} [" + string.Join(", ", System.Linq.Enumerable.Select(jobs, kv => kv.Key + "=" + kv.Value)) + "]");
            int missing = 0;
            foreach (var c in sim.Citizens) foreach (var e in sim.ScheduleFor(c, sim.Day)) if (sim.Place(e.PlaceId) == null) missing++;
            Log("SCHEDULES missingPlaces=" + missing + " (expected 0)");
            string Sig() { var sb = new System.Text.StringBuilder(); foreach (var c in sim.Citizens) sb.Append(c).Append(string.Join("|", sim.ScheduleFor(c, sim.Day))); return sb.ToString(); }
            string sig1 = Sig(); sim.Generate(town.Layout.Seed); string sig2 = Sig();
            Log("DETERMINISTIC " + (sig1 == sig2) + " (same seed -> same people and timetables)");
            var smith = sim.FindJob(Job.Herrero);
            if (smith != null)
            {
                Log("SMITH " + smith);
                foreach (var line in sim.Describe(smith, sim.Day).Split('\n')) if (line.Length > 0) Log("  " + line);
                foreach (float hq in new[] { 2f, 8f, 10f, 14f, 16f, 20f })
                    Log($"  WhereIs({smith.Name}, {TownSimulation.Clock(hq)}) = {sim.WhereIs(smith, hq)}  -> {sim.PlaceName(sim.WhereIs(smith, hq).PlaceId)}");
            }
            var baker = sim.FindJob(Job.Panadero);
            if (baker != null) Log($"BAKER {baker.Name} at 04:00 = {sim.WhereIs(baker, 4f)} (expected Trabajar at {baker.WorkId}) | 12:00 = {sim.WhereIs(baker, 12f)}");
            var guard = sim.Citizens.Find(c => c.Job == Job.Guardia && c.Shift == Shift.Noche);
            if (guard != null) Log($"NIGHT GUARD {guard.Name} at 02:00 = {sim.WhereIs(guard, 2f)} | 10:00 = {sim.WhereIs(guard, 10f)}");
            foreach (float hq in new[] { 3f, 7f, 10f, 12.5f, 14f, 17f, 20f, 23f })
            {
                var cen = sim.Census(hq);
                Log($"CENSUS {TownSimulation.Clock(hq)} " + string.Join(" ", System.Linq.Enumerable.Select(cen, kv => kv.Key + "=" + kv.Value)) +
                    $" | plaza={sim.At("plaza", hq).Count} fuente={sim.At("fountain", hq).Count} outdoorsNearPlaza={sim.Near(town.Layout.Fountain, 20f, hq).Count}");
            }
            int smithsWorking10 = 0, smiths = 0;
            foreach (var c in sim.Citizens) if (c.Job == Job.Herrero) { smiths++; var l = sim.WhereIs(c, 10f); if (!l.Walking && l.Activity == Activity.Trabajar && l.PlaceId == c.WorkId) smithsWorking10++; }
            int asleep3 = sim.Census(3f)["durmiendo"];
            Log($"CHECK smithsAtWork@10={smithsWorking10}/{smiths} asleep@03={asleep3}/{sim.Citizens.Count}");

            // ---- women and men of the town: complexion ranges
            float fMin = 1f, fMax = 0f, mMin = 1f, mMax = 0f; int women = 0;
            foreach (var c in sim.Citizens)
            {
                float fe = CitizenSpawner.Look(c).Femininity;
                if (c.Female) { women++; fMin = Mathf.Min(fMin, fe); fMax = Mathf.Max(fMax, fe); }
                else { mMin = Mathf.Min(mMin, fe); mMax = Mathf.Max(mMax, fe); }
            }
            // households: most homes a couple; the guard: 15+ men
            int homesWithCouple = 0, homesUsed2 = 0;
            var byHome = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<CitizenData>>();
            foreach (var c in sim.Citizens) { if (!byHome.TryGetValue(c.HomeId, out var lst)) byHome[c.HomeId] = lst = new System.Collections.Generic.List<CitizenData>(); lst.Add(c); }
            foreach (var kv in byHome)
            {
                homesUsed2++;
                bool man = kv.Value.Exists(c => !c.Female && c.Age >= 18 && c.Age <= 60), woman = kv.Value.Exists(c => c.Female && c.Age >= 18 && c.Age <= 60);
                if (man && woman) homesWithCouple++;
            }
            var guards = sim.Citizens.FindAll(c => c.Job == Job.Guardia);
            var smithLots = town.Layout.Lots.FindAll(l => l.Kind == BuildingKind.Smithy);
            int smithies = smithLots.Count;
            if (smithies == 2)
                Log($"SMITHIES apart={(smithLots[0].Door - smithLots[1].Door).magnitude:0}m  toPlaza=[{(smithLots[0].Door - town.Layout.Fountain).magnitude:0}m, {(smithLots[1].Door - town.Layout.Fountain).magnitude:0}m]  " +
                    $"toLudusGate=[{(smithLots[0].Door - town.Layout.LudusGate).magnitude:0}m, {(smithLots[1].Door - town.Layout.LudusGate).magnitude:0}m]");
            Log($"POPULATION homes={homesUsed2} withCouple={homesWithCouple} ({100f * homesWithCouple / Mathf.Max(1, homesUsed2):0}%)  guards={guards.Count} (expected >=15) " +
                $"guardsAllMen={guards.TrueForAll(c => !c.Female)} smithies={smithies} (expected 2) smiths={sim.Citizens.FindAll(c => c.Job == Job.Herrero).Count}");
            Log($"COMPLEXION women={women} [{fMin:0.00}..{fMax:0.00}] (expected >=0.50)  men={sim.Citizens.Count - women} [{mMin:0.00}..{mMax:0.00}] (expected <=0.50)");

            yield return Wait(1.5f);
            yield return Shot("01_llegada");
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-aerial") >= 0)
            {
                // straight down over the whole town, then a closer look at a quarter
                var pcA = town.Player.GetComponent<PlayerController>();
                if (pcA != null) pcA.enabled = false;
                var camA = town.Cam;
                Vector3 mid = (town.Layout.ArenaCenter + town.Layout.LudusCenter) * 0.5f;
                for (float t = 0f; t < 2.5f; t += Time.unscaledDeltaTime) { camA.FocusOverride = mid; camA.Distance = 150f; camA.Pitch = 89f; yield return null; }
                yield return Shot("00_aerea_cenital");
                for (float t = 0f; t < 2f; t += Time.unscaledDeltaTime) { camA.FocusOverride = town.Layout.ToWorld(new Vector3(40f, 0, 40f)); camA.Distance = 60f; camA.Pitch = 70f; yield return null; }
                yield return Shot("00b_barrio");
                for (float t = 0f; t < 2f; t += Time.unscaledDeltaTime) { camA.FocusOverride = town.Layout.ToWorld(new Vector3(0f, 0, 30f)); camA.Distance = 45f; camA.Pitch = 55f; yield return null; }
                yield return Shot("00c_calle");
                // the time of day: dawn, day, dusk, night (same street view)
                var simA = TownSimulation.Instance;
                foreach (var (h, nm) in new[] { (6.5f, "amanecer"), (12f, "dia"), (18.5f, "atardecer"), (23f, "noche") })
                {
                    simA.Hour = h;
                    for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime)
                    {
                        camA.FocusOverride = town.Layout.ToWorld(new Vector3(0f, 0, 24f)) + Vector3.up * 6f; camA.Distance = 30f; camA.Pitch = 12f;
                        camA.Yaw = Quaternion.LookRotation(-town.Layout.ArenaFacing).eulerAngles.y;
                        yield return null;
                    }
                    var dn = Gladiator.PS2Style.DayNightCycle.Instance;
                    Log($"DAYNIGHT {h:0.0}h phase={(dn != null ? dn.Current.ToString() : "none")} clouds={(dn != null ? dn.CloudCount : 0)} sun={RenderSettings.sun.intensity:0.00} fog={RenderSettings.fogColor}");
                    yield return Shot("00d_hora_" + nm);
                    if (h == 12f)
                    {
                        // from the street, looking up at the clouds
                        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
                        {
                            camA.FocusOverride = town.Layout.ToWorld(new Vector3(0f, 0, 24f)) + Vector3.up * 40f; camA.Distance = 12f; camA.Pitch = -25f;
                            yield return null;
                        }
                        yield return Shot("00e_nubes");
                    }
                }
                Quit(); yield break;
            }

            // ---- taunt wheel (T held) and the four selectable gestures, seen from the front
            {
                var pl = town.Player;
                var gcam = town.Cam;
                Vector3 face = pl.transform.forward;
                gcam.FocusOverride = pl.transform.position + Vector3.up * 0.9f; gcam.Distance = 4.2f; gcam.Pitch = 8f;
                gcam.Yaw = Quaternion.LookRotation(-face).eulerAngles.y;
                var wheel = Gladiator.UI.TauntWheel.Create();
                wheel.Open(); wheel.SetSelected(1);
                yield return HoldCam(gcam, pl, 0.6f);
                yield return Shot("01b_menu_burlas");
                wheel.Close(); Destroy(wheel.gameObject);
                string[] gnames = { "brazos", "saludo", "reverencia", "negar" };
                for (int g = 0; g < 4; g++)
                {
                    yield return HoldCam(gcam, pl, 0.4f);
                    bool ok = pl.TryTaunt(Gladiator.UI.TauntWheel.Kinds[g]);
                    float tShot = g == 2 ? 1.3f : 0.9f;
                    yield return HoldCam(gcam, pl, tShot);
                    Log($"GESTURE {gnames[g]} ok={ok} state={pl.State} kind={pl.TauntKind}");
                    yield return Shot("01c_gesto_" + gnames[g]);
                    while (pl.State == FighterState.Taunt) yield return null;
                }
                gcam.FocusOverride = null;
            }

            // ---- activities (hold X): lie down, sit, eat, cook; then walk vs run
            {
                var pl = town.Player;
                var gcam = town.Cam;
                {
                    // an open spot next to the fountain
                    var cc = pl.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    pl.transform.position = town.Layout.Fountain + new Vector3(4.5f, 0.05f, 0f);
                    if (cc != null) cc.enabled = true;
                    yield return HoldCam(gcam, pl, 0.6f);
                }
                var aw = Gladiator.UI.TauntWheel.Create("ACCION", Gladiator.UI.TauntWheel.ActivityLabels);
                aw.Open(); aw.SetSelected(0);
                yield return HoldCam(gcam, pl, 0.5f);
                yield return Shot("01d_menu_acciones");
                aw.Close(); Destroy(aw.gameObject);
                string[] an = { "acostarse", "sentarse", "comer", "cocinar" };
                for (int k = 0; k < 4; k++)
                {
                    bool ok = pl.TryActivity(k);
                    yield return HoldCam(gcam, pl, pl.ActivityInTime + 1.0f, 0.3f, 3.6f, 18f, k == 0 ? 80f : 35f);
                    Log($"ACTIVITY {an[k]} ok={ok} state={pl.State} kind={pl.ActivityKind} props={(pl.GetComponent<ActivityProps>() != null)}");
                    yield return Shot("01e_accion_" + an[k]);
                    Vector3 before = pl.transform.position;
                    pl.Move(pl.transform.forward);
                    yield return HoldCam(gcam, pl, 0.3f);
                    Log($"ACTIVITY {an[k]} moved while busy={(pl.transform.position - before).magnitude:0.00}m");
                    pl.StopActivity();
                    float t0a = Time.time;
                    while (pl.State == FighterState.Activity && Time.time - t0a < 5f) { pl.Move(Vector3.zero); yield return null; }
                    Log($"ACTIVITY {an[k]} got up in {Time.time - t0a:0.00}s state={pl.State}");
                }
                var pcw = pl.GetComponent<PlayerController>();
                if (pcw != null) pcw.enabled = false;
                foreach (bool walk in new[] { false, true })
                {
                    pl.WalkMode = walk;
                    Vector3 p0 = pl.transform.position;
                    for (float t = 0f; t < 1f; t += Time.deltaTime) { pl.Move(pl.transform.forward); yield return null; }
                    Log($"WALKMODE {walk} speed={(pl.transform.position - p0).magnitude:0.00} m/s");
                    if (walk) yield return Shot("01f_caminando");
                }
                pl.WalkMode = false;
                if (pcw != null) pcw.enabled = true;
                gcam.FocusOverride = null;
                yield return RagdollTest(town);
                yield return GuardsTest(town);
                yield return InfirmaryTest(town);
                yield return SheathWindowsTest(town);
                yield return HomeOwnerTest(town);
                yield return PropsTest(town);
                yield return TavernTest(town);
                yield return ExtrasTest(town);
                yield return LifeTest(town);
                yield return ServicesTest(town);
                yield return LudusTest(town);
                town.Guards.Suspended = true;
                yield return ThreeStoreyTest(town);
                yield return ShopsTest(town);
                yield return TempleDomusTest(town);
                yield return FurnitureTest(town);
                yield return JailTest(town);
                yield return RulesTest(town); // the older tests walk into houses: no warnings there
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-quick") >= 0)
                {
                    // the real path: losing in the arena -> next day, the town, in an infirmary bed
                    var gm = GameManager.Instance;
                    int day0 = gm.DayCycle.Day;
                    gm.DayCycle.CompleteArena(false);
                    float tl = Time.realtimeSinceStartup;
                    yield return null;
                    while ((TownBootstrap.Instance == null || TownBootstrap.Instance == town) && Time.realtimeSinceStartup - tl < 30f) yield return null;
                    var t2 = TownBootstrap.Instance;
                    yield return Wait(2.5f);
                    Log($"ARENA END the clock after the fight={TownSimulation.Clock(TownSimulation.Instance.Hour)} (expected 20:00 and a little)");
                    if (t2 != null)
                    {
                        var inf2 = t2.Infirmary;
                        Log($"LOST FIGHT day {day0}->{gm.DayCycle.Day} scene={SceneManager.GetActiveScene().name} woke={t2.WokeInInfirmary} bed={t2.LastBed} " +
                            $"state={t2.Player.State} lying={t2.Player.InActivity && t2.Player.ActivityKind == 0} inInfirmary={(inf2 != null && inf2.Contains(t2.Player.transform.position))} hp={t2.Player.Health.Current:0}/{t2.Player.Health.Max:0}");
                        t2.Cam.FocusOverride = null; // what the player sees: the normal town camera
                        yield return Wait(0.5f);
                        yield return Shot("03b_derrota_enfermeria");
                    }
                    else Log("LOST FIGHT town not reloaded");
                    Quit(); yield break;
                }
            }

            // ---- the complexion slider: bust grows from masculine to feminine (same body otherwise), bare and in a tunic
            {
                var row = new System.Collections.Generic.List<Fighter>();
                Vector3 baseP = town.Player.transform.position + town.Layout.LudusFacing * 4f;
                Vector3 right = Vector3.Cross(Vector3.up, town.Layout.LudusFacing);
                float[] fems = { 0f, 0.5f, 0.75f, 1f };
                for (int k = 0; k < 2; k++)
                    for (int i = 0; i < fems.Length; i++)
                    {
                        var look = Gladiator.Character.CharacterAppearance.Random(new System.Random(77), false);
                        look.Femininity = fems[i]; look.Armor = 0; look.Helmet = false; look.Beard = 0; look.HairStyle = 2;
                        look.Outfit = k == 0 ? 0 : 1; look.ClothesVersion = 1; look.Shirt = k == 0 ? 0 : 1; look.ClothColor = 2;
                        var f = Gladiator.Arena.CharacterFactory.Create("Busto" + i, look,
                            baseP + right * (-3.3f + i * 2.2f) + town.Layout.LudusFacing * (k * 3f), Quaternion.LookRotation(-town.Layout.LudusFacing), 9);
                        f.Controller.enabled = false;
                        row.Add(f);
                    }
                town.Cam.FocusOverride = baseP + town.Layout.LudusFacing * 1.5f;
                town.Cam.Distance = 9f; town.Cam.Pitch = 12f;
                town.Cam.Yaw = Quaternion.LookRotation(town.Layout.LudusFacing).eulerAngles.y; // look at their faces
                yield return Wait(2.5f);
                yield return Shot("09_complexion_busto");
                // shoulders: forward / normal / back, seen from the side
                foreach (var f in row) Destroy(f.gameObject);
                row.Clear();
                float[] shs = { 0f, 0.35f, 0.65f, 1f };
                for (int i = 0; i < shs.Length; i++)
                {
                    var look = Gladiator.Character.CharacterAppearance.Random(new System.Random(77), false);
                    look.Femininity = 0f; look.Armor = 0; look.Helmet = false; look.Outfit = 0; look.ClothesVersion = 1; look.Shirt = 0;
                    look.ShoulderBack = shs[i];
                    var f = Gladiator.Arena.CharacterFactory.Create("Hombros" + i, look, baseP + right * (-3.3f + i * 2.2f), Quaternion.LookRotation(right), 9);
                    f.Controller.enabled = false;
                    row.Add(f);
                }
                town.Cam.Yaw = Quaternion.LookRotation(town.Layout.LudusFacing).eulerAngles.y;
                town.Cam.FocusOverride = baseP;
                yield return Wait(2f);
                yield return Shot("09b_hombros");
                foreach (var f in row) Destroy(f.gameObject);
                town.Cam.FocusOverride = null; town.Cam.Distance = 11f; town.Cam.Pitch = 40f;
                town.Cam.Yaw = Quaternion.LookRotation(-town.Layout.ArenaFacing).eulerAngles.y;
            }

            // the simulation made visible: F3 panel + F4 markers at 10:00 and at 21:00, from the air
            sim.Hour = 10f;
            town.Info.SetMarkers(true);
            town.Cam.FocusOverride = town.Layout.Fountain; town.Cam.Distance = 70f; town.Cam.Pitch = 60f;
            yield return Wait(2.5f);
            Log($"MARKERS 10:00 visible={town.Info.VisibleMarkers}");
            yield return Shot("10_marcadores_10h");
            sim.Hour = 21f;
            yield return Wait(1f);
            Log($"MARKERS 21:00 visible={town.Info.VisibleMarkers}");
            yield return Shot("11_marcadores_21h");
            town.Cam.FocusOverride = null; town.Cam.Distance = 11f; town.Cam.Pitch = 40f;
            sim.Hour = 10.5f;
            Teleport(town.Player, town.Layout.Find("plaza").Position, -town.Layout.ArenaFacing);
            town.Info.SetPanel(true);
            yield return Wait(2f);
            yield return Shot("12_panel_plaza");
            town.Info.SetPanel(false); town.Info.SetMarkers(false);

            // ---- houses you can enter: door with E, kitchen / bedroom / bathroom, roof hidden inside
            {
                var houses = town.Houses;
                int withDoor = 0, threeRooms = 0;
                foreach (var hh in houses) { if (hh.Door != null) withDoor++; if (hh.Rooms.Count == 3) threeRooms++; }
                Log($"HOUSES homes={houses.Count} withDoor={withDoor} threeRooms={threeRooms}");
                // the home nearest the plaza
                HouseInterior hs = null; float bd = float.MaxValue;
                foreach (var hh in houses) { if (hh.Floor != 0 || hh.Lot.ForSale) continue; float dd = (hh.transform.position - town.Layout.Fountain).sqrMagnitude; if (dd < bd) { bd = dd; hs = hh; } }
                var tm1 = town.GetComponent<TownManager>();
                Vector3 front = hs.Door.Center + hs.transform.forward * 1.2f;
                Teleport(town.Player, front, -hs.transform.forward);
                yield return Wait(0.5f);
                bool blockedClosed = true;
                for (float t = 0f; t < 1.2f; t += Time.deltaTime) { town.Player.Move(-hs.transform.forward); yield return null; }
                town.Player.Move(Vector3.zero);
                blockedClosed = !hs.Contains(town.Player.transform.position);
                Teleport(town.Player, front, -hs.transform.forward);
                yield return Wait(0.3f);
                var nd = town.NearestDoor(town.Player.transform.position, 1.7f);
                if (nd != null) nd.Toggle();
                yield return Wait(0.6f);
                Log($"DOOR closedBlocks={blockedClosed} (expected True) opened={hs.Door.IsOpen} angle={hs.Door.Angle:0}");
                yield return Shot("16_puerta_abierta");
                {
                    var ct = town.Cam.transform;
                    var inside = Physics.OverlapSphere(ct.position, 0.4f, ~0, QueryTriggerInteraction.Ignore);
                    var camHits = new System.Collections.Generic.List<string>();
                    foreach (var c in inside) camHits.Add(c.transform.parent != null ? c.transform.parent.name + "/" + c.name : c.name);
                    Log($"CAMDEBUG cam={ct.position} pitch={ct.eulerAngles.x:0} yaw={ct.eulerAngles.y:0} player={town.Player.transform.position} " +
                        $"houseFwd={hs.transform.forward} insideColliders=[{string.Join(",", camHits)}]");
                }
                // walk in through the doorway, then visit every room
                for (float t = 0f; t < 2.2f && !hs.Contains(town.Player.transform.position + (-hs.transform.forward) * 1.2f); t += Time.deltaTime)
                { town.Player.Move(-hs.transform.forward * 0.6f); yield return null; }
                for (float t = 0f; t < 1.2f; t += Time.deltaTime) { town.Player.Move(-hs.transform.forward * 0.6f); yield return null; }
                town.Player.Move(Vector3.zero);
                yield return Wait(0.5f);
                Log($"ENTER inside={hs.Contains(town.Player.transform.position)} room={tm1.CurrentRoom} roofHidden={!hs.Upper.activeSelf}");
                yield return Shot("17_dentro_cocina");
                var seen = new System.Collections.Generic.List<string>();
                // kitchen -> bedroom -> (back through the kitchen) -> bathroom -> kitchen, always through the openings
                var legs = new (string room, Vector3[] pts)[]
                {
                    ("HABITACION", new[] { hs.GapPoint("HABITACION", 1), hs.GapPoint("HABITACION", -1), hs.RoomCenter("HABITACION") }),
                    ("BANO", new[] { hs.GapPoint("HABITACION", -1), hs.GapPoint("HABITACION", 1), hs.GapPoint("BANO", 1), hs.GapPoint("BANO", -1), hs.RoomCenter("BANO") }),
                    ("COCINA", new[] { hs.GapPoint("BANO", -1), hs.GapPoint("BANO", 1), hs.RoomCenter("COCINA") }),
                };
                foreach (var leg in legs)
                {
                    foreach (var target in leg.pts)
                        for (float t = 0f; t < 4f; t += Time.deltaTime)
                        {
                            Vector3 to = target - town.Player.transform.position; to.y = 0f;
                            if (to.magnitude < 0.3f) break;
                            town.Player.Move(to.normalized * 0.5f); town.Player.SetFacing(to);
                            yield return null;
                        }
                    town.Player.Move(Vector3.zero);
                    yield return Wait(0.4f);
                    seen.Add(leg.room + "->" + (hs.RoomAt(town.Player.transform.position) ?? "fuera") + "(msg " + tm1.CurrentRoom + ")");
                    if (leg.room == "HABITACION") yield return Shot("18_dentro_habitacion");
                }
                Log("ROOMS walked [" + string.Join(", ", seen) + "]");
                {
                    int furnished = 0, emptySale = 0, saleTotal = 0;
                    foreach (var hh in houses)
                    {
                        int lightsHere = hh.Lights.Count;
                        if (hh.Lot.ForSale && hh.Floor == 0) { saleTotal++; if (lightsHere == 0) emptySale++; }
                        else if (lightsHere > 0) furnished++;
                    }
                    Log($"FURNITURE furnishedStoreys={furnished}/{houses.Count - saleTotal} saleHousesEmpty={emptySale}/{saleTotal} lightOnHere={(hs.Lights.Count > 0 && hs.Lights[0].enabled)}");
                    var camF = town.Cam;
                    float d0 = camF.Distance, p0 = camF.Pitch;
                    foreach (var rn in new[] { "COCINA", "HABITACION", "BANO" })
                    {
                        camF.FocusOverride = hs.RoomCenter(rn); camF.Distance = 6.5f; camF.Pitch = 58f;
                        yield return Wait(1.2f);
                        yield return Shot("30_muebles_" + rn.ToLowerInvariant());
                    }
                    camF.FocusOverride = null; camF.Distance = d0; camF.Pitch = p0;
                }
                // the back wall holds
                Vector3 before = town.Player.transform.position;
                for (float t = 0f; t < 2f; t += Time.deltaTime) { town.Player.Move(-hs.transform.forward); yield return null; }
                town.Player.Move(Vector3.zero);
                Log($"WALLS stillInside={hs.Contains(town.Player.transform.position)} (expected True)");
                GameSettings.CameraMode = 1; GameSettings.Apply(false);
                yield return Wait(1.5f);
                yield return Shot("19_dentro_hombro");
                GameSettings.CameraMode = 0; GameSettings.Apply(false);
                Teleport(town.Player, front + hs.transform.forward * 2f, -hs.transform.forward);
                yield return Wait(0.5f);
                Log($"LEAVE roofShown={hs.Upper.activeSelf} (expected True)");
            }

            // ---- large homes: 2 bedrooms, 2 bathrooms, a 2 x 2 storeroom; never more than one bed in a bedroom
            {
                var larges = town.Houses.FindAll(hh => hh.Lot.Large && hh.Floor == 0);
                int okRooms = 0, overBeds = 0;
                foreach (var hh in larges)
                {
                    int beds = hh.Rooms.FindAll(r => r.Name == "HABITACION").Count, baths = hh.Rooms.FindAll(r => r.Name == "BANO").Count;
                    var store = hh.Rooms.Find(r => r.Name == "TRASTERO");
                    if (beds == 2 && baths == 2 && Mathf.Abs(store.X1 - store.X0 - 2f) < 0.01f && Mathf.Abs(store.Z1 - store.Z0 - 2f) < 0.01f) okRooms++;
                }
                foreach (var hh in town.Houses)
                {
                    int bedrooms = hh.Rooms.FindAll(r => r.Name == "HABITACION").Count;
                    if (hh.Beds.Count > bedrooms) overBeds++;
                }
                int maxHousehold = 0;
                var perHome = new System.Collections.Generic.Dictionary<string, int>();
                foreach (var c in sim.Citizens) perHome[c.HomeId] = perHome.TryGetValue(c.HomeId, out int n0) ? n0 + 1 : 1;
                foreach (var kv in perHome) maxHousehold = Mathf.Max(maxHousehold, kv.Value);
                Log($"LARGE homes={larges.Count} rightRooms={okRooms} housesWithMoreBedsThanBedrooms={overBeds} (expected 0) maxHousehold={maxHousehold} citizens={sim.Citizens.Count}");
                var lh = larges.Find(hh => !hh.Lot.ForSale);
                if (lh != null)
                {
                    var camL = town.Cam;
                    float d0 = camL.Distance, p0 = camL.Pitch, yaw0 = camL.Yaw;
                    Teleport(town.Player, lh.RoomCenter("PASILLO"), -lh.transform.forward);
                    yield return Wait(0.6f);
                    camL.FocusOverride = lh.transform.position; camL.Distance = 13f; camL.Pitch = 72f;
                    camL.Yaw = Quaternion.LookRotation(-lh.transform.forward).eulerAngles.y;
                    yield return Wait(1.5f);
                    var tmL = town.GetComponent<TownManager>();
                    Log($"LARGE inside room={lh.RoomAt(town.Player.transform.position)} roofHidden={!lh.Upper.activeSelf}");
                    // walk from the hallway into both bathrooms and both bedrooms through their doorways
                    var walkedIn = new System.Collections.Generic.List<string>();
                    foreach (var rm in lh.Rooms)
                    {
                        if (rm.Name != "BANO" && rm.Name != "HABITACION") continue;
                        Teleport(town.Player, lh.RoomCenter("PASILLO"), -lh.transform.forward);
                        yield return Wait(0.2f);
                        float side = rm.X0 < 0f ? -1f : 1f;
                        float zc = rm.Name == "BANO" ? (rm.Z0 + rm.Z1) * 0.5f : rm.Z0 + 2.75f - 0.0f;
                        if (rm.Name == "HABITACION") zc = lh.Rooms.Find(r => r.Name == "TRASTERO").Z0 + 2.75f;
                        Vector3[] pts = { lh.transform.TransformPoint(new Vector3(0f, 0.05f, zc)), lh.transform.TransformPoint(new Vector3(side * 1.9f, 0.05f, zc)) };
                        foreach (var target in pts)
                            for (float t = 0f; t < 3f; t += Time.deltaTime)
                            {
                                Vector3 to = target - town.Player.transform.position; to.y = 0f;
                                if (to.magnitude < 0.25f) break;
                                town.Player.Move(to.normalized * 0.5f); town.Player.SetFacing(to);
                                yield return null;
                            }
                        town.Player.Move(Vector3.zero);
                        yield return Wait(0.2f);
                        Vector3 lp = lh.transform.InverseTransformPoint(town.Player.transform.position);
                        bool inRoom = lp.x >= rm.X0 && lp.x <= rm.X1 && lp.z >= rm.Z0 && lp.z <= rm.Z1;
                        walkedIn.Add(rm.Name + (side < 0 ? "-izq" : "-der") + "=" + inRoom);
                    }
                    Log("LARGE walkIn [" + string.Join(", ", walkedIn) + "]");
                    yield return Shot("31_casa_grande");
                    camL.FocusOverride = null; camL.Distance = d0; camL.Pitch = p0; camL.Yaw = yaw0;
                }
            }

            // ---- houses for sale: four, spread over the town; buy one
            {
                var sale = town.Houses.FindAll(hh => hh.Floor == 0 && hh.Lot.ForSale);
                float minApart = float.MaxValue;
                for (int i = 0; i < sale.Count; i++) for (int j = i + 1; j < sale.Count; j++)
                    minApart = Mathf.Min(minApart, (sale[i].transform.position - sale[j].transform.position).magnitude);
                int livedIn = 0;
                foreach (var c in sim.Citizens) foreach (var hh in sale) if (c.HomeId == "home" + hh.Lot.Id) livedIn++;
                Log($"FORSALE count={sale.Count} (expected 4) minApart={minApart:0}m nobodyLivesThere={livedIn == 0} prices=[{string.Join(",", sale.ConvertAll(hh => hh.Lot.Price.ToString()))}]");
                if (sale.Count > 0)
                {
                    var tmB = town.GetComponent<TownManager>();
                    var hb = sale[0];
                    HouseDeeds.Remove(town.Layout.Seed, hb.Lot.Id);
                    if (hb.SaleSign != null) hb.SaleSign.SetActive(true);
                    Teleport(town.Player, hb.Door.Center + hb.transform.forward * 1.1f, -hb.transform.forward);
                    yield return Wait(0.6f);
                    yield return Shot("25_casa_en_venta");
                    var prof = GameManager.Instance.Profile;
                    int before = prof.Denarii;
                    bool poor = before < hb.Lot.Price && !tmB.TryBuy(hb);
                    prof.AddDenarii(hb.Lot.Price);
                    int money0 = prof.Denarii;
                    bool bought = tmB.TryBuy(hb);
                    yield return Wait(0.8f);
                    Log($"BUY refusedWhenPoor={poor || before >= hb.Lot.Price} bought={bought} paid={money0 - prof.Denarii} owned={HouseDeeds.Owns(town.Layout.Seed, hb.Lot.Id)} doorOpen={hb.Door.IsOpen} signHidden={hb.SaleSign == null || !hb.SaleSign.activeSelf}");
                    yield return Shot("26_casa_comprada");
                    HouseDeeds.Remove(town.Layout.Seed, hb.Lot.Id); // tests leave no trace
                    prof.Denarii = before;
                }
            }

            // ---- two-storey houses: outside stairs up to a door into the first floor
            {
                var ups = town.Houses.FindAll(hh => hh.Floor == 1);
                int twoStorey = town.Layout.Lots.FindAll(l => l.IsHome && l.Floors >= 2).Count;
                Log($"STAIRS twoStoreyHomes={twoStorey} upperFloors={ups.Count} (expected equal)");
                HouseInterior up = null; float bu = float.MaxValue;
                foreach (var hh in ups) { float dd = (hh.transform.position - town.Layout.Fountain).sqrMagnitude; if (dd < bu) { bu = dd; up = hh; } }
                if (up != null)
                {
                    Teleport(town.Player, up.StairBottom, up.StairDir);
                    yield return Wait(0.5f);
                    yield return Shot("27_escalera_abajo");
                    float maxY = 0f;
                    for (float t = 0f; t < 6f; t += Time.deltaTime)
                    {
                        Vector3 to = up.StairTop - town.Player.transform.position; to.y = 0f;
                        if (to.magnitude < 0.3f) break;
                        town.Player.Move(to.normalized * 0.7f); town.Player.SetFacing(to);
                        maxY = Mathf.Max(maxY, town.Player.transform.position.y);
                        yield return null;
                    }
                    town.Player.Move(Vector3.zero);
                    yield return Wait(0.4f);
                    float y = town.Player.transform.position.y;
                    var upDoor = town.NearestDoor(town.Player.transform.position, 1.7f);
                    Log($"CLIMB reachedY={y:0.00} (expected ~3.1) doorFound={(upDoor != null && upDoor == up.Door)}");
                    yield return Shot("28_escalera_arriba");
                    if (upDoor != null) upDoor.Toggle();
                    yield return Wait(0.7f);
                    // step in through the side door into the first-floor kitchen
                    Vector3 inside = up.transform.TransformPoint(new Vector3(0f, 0.05f, up.HalfZ - 1.3f));
                    for (float t = 0f; t < 4f; t += Time.deltaTime)
                    {
                        Vector3 to = inside - town.Player.transform.position; to.y = 0f;
                        if (to.magnitude < 0.3f) break;
                        town.Player.Move(to.normalized * 0.6f); town.Player.SetFacing(to);
                        yield return null;
                    }
                    town.Player.Move(Vector3.zero);
                    yield return Wait(0.6f);
                    var tmU = town.GetComponent<TownManager>();
                    Log($"UPSTAIRS doorOpen={up.Door.IsOpen} inside={up.Contains(town.Player.transform.position)} room={tmU.CurrentRoom} roofHidden={!up.Upper.activeSelf} y={town.Player.transform.position.y:0.00}");
                    yield return Shot("29_primer_piso");
                    Teleport(town.Player, town.Layout.Find("plaza").Position, -town.Layout.ArenaFacing);
                    yield return Wait(0.5f);
                }
            }

            // ---- the smithy sells (the ludus no longer does)
            {
                var tmS = town.GetComponent<TownManager>();
                var smithy = town.Layout.Lots.Find(l => l.Kind == BuildingKind.Smithy);
                Teleport(town.Player, smithy.Door + smithy.Front * 4f, -smithy.Front);
                yield return Wait(1f);
                yield return Shot("24_herreria_armero");
                Teleport(town.Player, smithy.ArmorerSpot + smithy.Front * 1.9f + smithy.Right * 1.1f, -smithy.Front);
                yield return Wait(1.2f);
                yield return Shot("24b_armero_cerca");
                bool nearSmithy = tmS.NearestSmithy(town.Player.transform.position, 2.5f) != null;
                var arm = town.Armorers.Count > 0 ? town.Armorers[0] : null;
                Log($"ARMORER count={town.Armorers.Count} hammer={(arm != null && arm.Fighter.WeaponSocket.Find("Hammer") != null)}");
                tmS.OpenShop();
                yield return Wait(0.6f);
                Log($"SMITHY near={nearSmithy} shopOpen={town.Hud.Shop.IsOpen} (expected True)");
                yield return Shot("20_herreria_tienda");
                town.Hud.Shop.Close();
                yield return Wait(0.3f);
            }

            // ---- phase 3: bodies near the player
            Log($"NAVMESH ready={TownNavMesh.Ready} tris={TownNavMesh.Triangles} built in {TownNavMesh.BuildSeconds:0.00}s");
            var sp = town.Spawner;
            sim.Hour = 10.5f;
            Teleport(town.Player, town.Layout.Find("plaza").Position, -town.Layout.ArenaFacing);
            yield return Wait(4f);
            float maxD = 0f;
            foreach (var n in sp.Active) maxD = Mathf.Max(maxD, Flat(n.transform.position - town.Player.transform.position));
            Log($"SPAWN 10:30 plaza active={sp.ActiveCount}/{CitizenSpawner.PoolSize} poolCreated={sp.PoolCreated} rebuilds={sp.Rebuilds} farthest={maxD:0.0}m " +
                $"logicalNear={sim.Near(town.Player.transform.position, 40f, sim.Hour).Count}");
            var names = new System.Collections.Generic.List<string>();
            foreach (var n in sp.Active) if (names.Count < 6) names.Add(n.Citizen.Name + "(" + n.Citizen.JobName + (n.Walking ? ",camina" : "") + ")");
            Log("BODIES " + string.Join(", ", names));
            // a guard at a plaza corner: red, armoured, gladius + scutum
            {
                CitizenNPC g = null;
                foreach (var n in sp.Active) if (n.Citizen != null && n.Citizen.Job == Job.Guardia) { g = n; break; }
                if (g != null)
                {
                    Log($"GUARD {g.Citizen.Name} weapon={(g.Fighter.CurrentWeapon != null ? g.Fighter.CurrentWeapon.Stats.Id : "-")} " +
                        $"shield={(g.Fighter.EquippedShield != null ? g.Fighter.EquippedShield.Stats.Id : "-")} armorLoad={g.Fighter.ArmorLoad:0.00}");
                    var camG = town.Cam;
                    camG.FocusOverride = g.transform.position; camG.Distance = 4.5f; camG.Pitch = 12f;
                    camG.Yaw = Quaternion.LookRotation(-g.transform.forward).eulerAngles.y;
                    yield return Wait(1.5f);
                    yield return Shot("21_guardia");
                    camG.FocusOverride = null; camG.Distance = 11f; camG.Pitch = 40f;
                    camG.Yaw = Quaternion.LookRotation(-town.Layout.ArenaFacing).eulerAngles.y;
                }
                else Log("GUARD none near the plaza");
            }
            // skirts while walking: a woman in a long tunic, seen from the side mid-stride
            {
                var look = CitizenSpawner.Look(sim.Citizens.Find(c => c.Female && c.Age >= 18));
                look.Outfit = 2; look.Shirt = 3; look.ClothesVersion = 1;
                Vector3 at = town.Player.transform.position + Vector3.Cross(Vector3.up, town.Layout.ArenaFacing) * 4f;
                var wlk = Gladiator.Arena.CharacterFactory.Create("Falda", look, at, Quaternion.LookRotation(-town.Layout.ArenaFacing), 9);
                var camW = town.Cam;
                for (float t = 0f; t < 1.3f; t += Time.deltaTime) { wlk.Move(-town.Layout.ArenaFacing * 0.9f); camW.FocusOverride = wlk.transform.position; camW.Distance = 4f; camW.Pitch = 8f; camW.Yaw = Quaternion.LookRotation(Vector3.Cross(Vector3.up, -town.Layout.ArenaFacing)).eulerAngles.y + 180f; yield return null; }
                yield return Shot("22_falda_andando");
                for (float t = 0f; t < 0.25f; t += Time.deltaTime) { wlk.Move(-town.Layout.ArenaFacing * 0.9f); yield return null; }
                yield return Shot("23_falda_andando_b");
                // from the front, mid-stride
                for (float t = 0f; t < 0.6f; t += Time.deltaTime)
                {
                    wlk.Move(-town.Layout.ArenaFacing * 0.9f);
                    camW.FocusOverride = wlk.transform.position + Vector3.up * 0.6f; camW.Distance = 3.4f; camW.Pitch = 6f;
                    camW.Yaw = Quaternion.LookRotation(town.Layout.ArenaFacing).eulerAngles.y;
                    yield return null;
                }
                yield return Shot("23c_falda_frente");
                {
                    var lk = Gladiator.Character.CharacterAppearance.Random(new System.Random(5), false);
                    lk.Femininity = 0f; lk.Armor = 0; lk.Helmet = false; lk.ClothesVersion = 1;
                    lk.Outfit = 1; lk.Shirt = 1; lk.Pants = 1; lk.ClothColor = 1; lk.PantsColor = 4;
                    Vector3 atT = wlk.transform.position + Vector3.Cross(Vector3.up, town.Layout.ArenaFacing) * 3f;
                    var tun = Gladiator.Arena.CharacterFactory.Create("Tunica", lk, atT, Quaternion.LookRotation(town.Layout.ArenaFacing), 9);
                    wlk.gameObject.SetActive(false);
                    camW.FocusOverride = atT + Vector3.down * 0.45f; camW.Distance = 3.6f; camW.Pitch = 2f;
                    camW.Yaw = Quaternion.LookRotation(-town.Layout.ArenaFacing).eulerAngles.y;
                    yield return Wait(1.2f);
                    yield return Shot("23f_tunica_quieto");
                    camW.Yaw = Quaternion.LookRotation(Vector3.Cross(Vector3.up, town.Layout.ArenaFacing)).eulerAngles.y;
                    yield return Wait(0.5f);
                    yield return Shot("23h_tunica_lado");
                    camW.Yaw = Quaternion.LookRotation(-town.Layout.ArenaFacing).eulerAngles.y;
                    for (float t = 0f; t < 0.7f; t += Time.deltaTime)
                    {
                        tun.Move(town.Layout.ArenaFacing * 0.5f);
                        camW.FocusOverride = tun.transform.position + Vector3.down * 0.45f;
                        yield return null;
                    }
                    yield return Shot("23g_tunica_andando");
                    Destroy(tun.gameObject);
                }
                Destroy(wlk.gameObject);
                // guards: a gladius alone, gladius + scutum blocking, and the scutum carried
                var poses = new System.Collections.Generic.List<Fighter>();
                Vector3 baseB = town.Player.transform.position + Vector3.Cross(Vector3.up, town.Layout.ArenaFacing) * 3f;
                Vector3 sideB = Vector3.Cross(Vector3.up, town.Layout.ArenaFacing);
                for (int i = 0; i < 3; i++)
                {
                    var lk = Gladiator.Character.CharacterAppearance.Random(new System.Random(31 + i), false);
                    lk.Armor = 0; lk.Helmet = false;
                    var g = Gladiator.Arena.CharacterFactory.Create("Guardia" + i, lk, baseB + sideB * (i * 1.6f), Quaternion.LookRotation(town.Layout.ArenaFacing), 9);
                    g.Equip(WeaponFactory.Create(WeaponLibrary.Gladius));
                    if (i > 0) Shield.Attach(g, ShieldLibrary.ById("scutum"), CitizenSpawner.GuardRed);
                    if (i < 2) g.SetBlock(true);
                    poses.Add(g);
                }
                camW.FocusOverride = baseB + sideB * 1.6f + Vector3.up * 1f; camW.Distance = 5.5f; camW.Pitch = 10f;
                camW.Yaw = Quaternion.LookRotation(-town.Layout.ArenaFacing).eulerAngles.y;
                yield return Wait(1.5f);
                Log($"GUARDPOSE states=[{string.Join(",", poses.ConvertAll(x => x.State.ToString()))}]");
                yield return Shot("23d_bloqueo_frente");
                camW.Yaw = Quaternion.LookRotation(sideB).eulerAngles.y;
                yield return Wait(1f);
                yield return Shot("23e_bloqueo_lado");
                foreach (var g in poses) Destroy(g.gameObject);
                camW.FocusOverride = null; camW.Distance = 11f; camW.Pitch = 40f;
                camW.Yaw = Quaternion.LookRotation(-town.Layout.ArenaFacing).eulerAngles.y;
            }
            var travelled0 = new System.Collections.Generic.Dictionary<CitizenNPC, float>();
            foreach (var n in sp.Active) travelled0[n] = n.Travelled;
            yield return Shot("13_ciudadanos_plaza");
            yield return Wait(6f);
            int movers = 0; float total = 0f;
            foreach (var kv in travelled0) if (kv.Key.Citizen != null) { float d = kv.Key.Travelled - kv.Value; total += d; if (d > 1f) movers++; }
            Log($"MOVE in 6s: {movers}/{travelled0.Count} walked >1m, total={total:0.0}m");
            GameSettings.CameraMode = 1; GameSettings.Apply(false);
            yield return Wait(1.5f);
            yield return Shot("14_hombro_plaza");
            GameSettings.CameraMode = 0; GameSettings.Apply(false);

            // talk to the nearest, then hit someone: they run and like you less
            var tm0 = town.GetComponent<TownManager>();
            var near = sp.Nearest(town.Player.transform.position, 40f);
            if (near != null)
            {
                Vector3 side = near.transform.position - near.transform.forward * 1.4f;
                Teleport(town.Player, side, near.transform.position - side);
                yield return Wait(0.3f);
                Log("TALK " + tm0.TalkTo(near));
                yield return Shot("15_hablar");
                int rel0 = near.Citizen.Relation;
                var target = near;
                Vector3 toT = target.transform.position - town.Player.transform.position; toT.y = 0f;
                Teleport(town.Player, target.transform.position - toT.normalized * 1.3f, toT);
                yield return Wait(0.2f);
                town.Player.TryAttack(AttackType.Light);
                yield return Wait(0.8f);
                Log($"HIT {target.Citizen?.Name} fleeing={target.Fleeing} relation {rel0}->{target.Citizen?.Relation} health={target.Fighter.Health.Current:0}");
                yield return Wait(2f);
            }

            // walk away: far bodies are released, near ones appear
            Teleport(town.Player, town.Layout.ArenaGate + town.Layout.ArenaFacing * 2f, -town.Layout.ArenaFacing);
            yield return Wait(4f);
            maxD = 0f;
            foreach (var n in sp.Active) maxD = Mathf.Max(maxD, Flat(n.transform.position - town.Player.transform.position));
            Log($"SPAWN arena gate active={sp.ActiveCount} farthest={maxD:0.0}m rebuilds={sp.Rebuilds} logicalNear={sim.Near(town.Player.transform.position, 40f, sim.Hour).Count}");
            // night: everybody goes home through their doors
            Teleport(town.Player, town.Layout.Find("plaza").Position, -town.Layout.ArenaFacing);
            yield return Wait(3f);
            int dayCount = sp.ActiveCount;
            sim.Hour = 23.5f;
            yield return Wait(20f);
            Log($"NIGHT 23:30 active before={dayCount} after 20s={sp.ActiveCount} logicalOutdoorsNear={sim.Near(town.Player.transform.position, 40f, sim.Hour).Count}");
            sim.Hour = 3f;

            var cam = town.Cam;
            var player = town.Player;
            var pc = player.GetComponent<PlayerController>();
            pc.enabled = false;

            // bird's-eye view of the whole town
            cam.FocusOverride = (town.Layout.ArenaCenter + town.Layout.LudusCenter) * 0.5f;
            cam.Distance = 120f; cam.Pitch = 62f;
            yield return Wait(2.5f);
            yield return Shot("02_vista_aerea");
            cam.FocusOverride = null; cam.Distance = 11f; cam.Pitch = 40f;

            // plaza
            Teleport(player, town.Layout.Find("plaza").Position, -town.Layout.ArenaFacing);
            yield return Wait(2f);
            yield return Shot("03_plaza");

            // a street from the shoulder, looking at the arena
            Vector3 street = town.Layout.ToWorld(new Vector3(0, 0, 36f));
            Teleport(player, street, -town.Layout.ArenaFacing);
            GameSettings.CameraMode = 1; GameSettings.Apply(false);
            yield return Wait(2f);
            yield return Shot("04_calle_hombro");
            GameSettings.GraphicsPreset = 1; GameSettings.Apply(false);
            yield return Wait(1.5f);
            yield return Shot("05_calle_ps3");
            GameSettings.CameraMode = 0; GameSettings.Apply(false);
            Teleport(player, town.Layout.ToWorld(new Vector3(-6f, 0, -8f)), -town.Layout.ArenaFacing);
            yield return Wait(2f);
            yield return Shot("06_plaza_ps3");
            GameSettings.GraphicsPreset = 0; GameSettings.Apply(false);

            // walk: ludus gate -> south street -> around the fountain -> main street -> arena gate
            Vector3[] route =
            {
                town.Layout.LudusGate + town.Layout.LudusFacing * 1.5f,
                town.Layout.ToWorld(new Vector3(0, 0, -30f)), town.Layout.ToWorld(new Vector3(0, 0, -14f)),
                town.Layout.ToWorld(new Vector3(5.5f, 0, -5f)), town.Layout.ToWorld(new Vector3(5.5f, 0, 7f)),
                town.Layout.ToWorld(new Vector3(0, 0, 16f)), town.Layout.ToWorld(new Vector3(0, 0, 45f)), town.Layout.ArenaGate,
            };
            Teleport(player, route[0], town.Layout.LudusFacing);
            yield return Wait(0.5f);
            int reached = 0; float walkTime = 0f;
            for (int i = 1; i < route.Length; i++)
            {
                float t = 0f;
                while (t < 14f)
                {
                    Vector3 to = route[i] - player.transform.position; to.y = 0f;
                    if (to.magnitude < 1.3f) break;
                    player.Move(to.normalized);
                    player.SetFacing(to);
                    t += Time.deltaTime;
                    yield return null;
                }
                player.Move(Vector3.zero);
                walkTime += t;
                Vector3 left = route[i] - player.transform.position; left.y = 0f;
                if (left.magnitude < 1.3f) reached++;
                else { Log($"WALK stuck before waypoint {i} at {player.transform.position} (missing {left.magnitude:0.0} m)"); break; }
            }
            Log($"WALK reached={reached}/{route.Length - 1} time={walkTime:0.0}s atArenaGate={(Flat(town.Layout.ArenaGate - player.transform.position) < 3.2f)}");
            yield return Shot("07_puerta_arena");

            // the ludus is part of the town: in and out through its gate, no loading
            yield return LudusTest(town);
            yield return Wait(1f);
            Log("DONE");
            Quit();
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        static void Teleport(Fighter f, Vector3 pos, Vector3 face)
        {
            f.Controller.enabled = false;
            f.transform.SetPositionAndRotation(pos + Vector3.up * 0.05f, Quaternion.LookRotation(face));
            f.Controller.enabled = true;
            f.SetFacing(face);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
