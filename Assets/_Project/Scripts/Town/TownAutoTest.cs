using System.Collections;
using System.IO;
using Gladiator.Combat;
using Gladiator.Core;
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
                yield return Shot("24_herreria_circulo");
                Teleport(town.Player, smithy.Door, -smithy.Front);
                yield return Wait(0.5f);
                bool nearSmithy = tmS.NearestSmithy(town.Player.transform.position, 2.6f) != null;
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
                Destroy(wlk.gameObject);
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

            // back to the ludus, then out again through the east door
            var tm = town.GetComponent<TownManager>();
            tm.GoToLudus();
            t0 = Time.realtimeSinceStartup;
            TrainingBootstrap tb = null;
            while ((tb = FindAnyObjectByType<TrainingBootstrap>()) == null && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            yield return Wait(1.5f);
            if (tb != null)
            {
                float d = Flat(tb.Player.transform.position - TrainingBootstrap.TownDoorPoint);
                bool merchant = false;
                foreach (var f in Fighter.All) if (f.DisplayName == "Mercator") merchant = true;
                Log($"LUDUS arrived nearEastDoor={d < 2f} dist={d:0.0} merchantStall={merchant} (expected False)");
                yield return Shot("08_ludus_puerta_este");
                tb.Player.GetComponent<PlayerController>().enabled = false;
                tb.Manager.GoToTown();
                t0 = Time.realtimeSinceStartup;
                while (TownBootstrap.Instance == null && Time.realtimeSinceStartup - t0 < 30f) yield return null;
                Log($"BACK IN TOWN={(TownBootstrap.Instance != null)} sameSeed={(TownBootstrap.Instance != null && TownBootstrap.Instance.Layout.Seed == town.Layout.Seed)}");
            }
            else Log("LUDUS NOT LOADED");
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
