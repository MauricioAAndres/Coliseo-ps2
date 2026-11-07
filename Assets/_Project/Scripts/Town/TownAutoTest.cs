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

            yield return Wait(1.5f);
            yield return Shot("01_llegada");

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
