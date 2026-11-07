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

            yield return Wait(1.5f);
            yield return Shot("01_llegada");

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
                Log($"LUDUS arrived nearEastDoor={d < 2f} dist={d:0.0}");
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
