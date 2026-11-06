using System.Collections;
using System.IO;
using Gladiator.Combat;
using Gladiator.Core;
using Gladiator.Progression;
using UnityEngine;

namespace Gladiator.Training
{
    /// <summary>
    /// "-autotest" for the ludus. Day 1: plays the three stations with bot input, checks the stats went up,
    /// then walks into the arena (where AutoTestRunner takes over and comes back). Day 2+: checks the loop
    /// came back with stats intact and the stations unlocked again, then quits.
    /// </summary>
    public class TrainingAutoTest : MonoBehaviour
    {
        TrainingBootstrap boot;

        public void Init(TrainingBootstrap b) { boot = b; }

        static void Log(string s) { Debug.Log("[AUTOTEST] " + s); }

        static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
        }

        static IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "autotest_" + name + ".png");
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Log("SCREENSHOT " + path);
        }

        IEnumerator Start()
        {
            var gm = GameManager.Instance;
            var dc = gm.DayCycle;
            var m = boot.Manager;
            var p = gm.Profile;
            Log($"TRAINING day={dc.Day} phase={dc.Phase} STR={p.Strength} AGI={p.Agility} RES={p.Resistance} W={p.Wins} L={p.Losses} denarii={p.Denarii} weapon={p.ArenaWeapon} shield={p.HasShield}");
            yield return Wait(1.5f);

            if (dc.Day >= 4)
            {
                yield return Shot("t9_day" + dc.Day);
                Log($"LOOP OK day={dc.Day} stationsUnlocked={dc.StationsUsedCount == 0} " + StatEffects.Describe(p));
                Log("DONE");
                Application.Quit();
                yield break;
            }
            if (dc.Day > 1) Log($"LOOP OK day={dc.Day} stationsUnlocked={dc.StationsUsedCount == 0} lastFightWon={dc.LastFightWon} lastReward={dc.LastReward}");

            yield return Shot("t0_day" + dc.Day);
            TrainingMinigame.AutoPlay = true;
            int sum0 = p.Strength + p.Agility + p.Resistance;
            int idx = 1;
            foreach (var s in m.Stations)
            {
                m.StartStation(s);
                if (dc.Day == 1) { yield return Wait(5.5f); yield return Shot("t" + idx + "_" + s.Id); }
                while (m.Busy) yield return null;
                Log($"STATION day={dc.Day} {s.Id} score={s.LastScore:0.00} points={TrainingMinigame.PointsFor(s.LastScore)}");
                yield return Wait(0.5f);
                idx++;
            }
            TrainingMinigame.AutoPlay = false;
            int sum1 = p.Strength + p.Agility + p.Resistance;
            Log($"STATS STR={p.Strength} AGI={p.Agility} RES={p.Resistance} gained={sum1 - sum0} -> {StatEffects.Describe(p)}");
            if (dc.Day == 1)
            {
                yield return Shot("t4_results");
                bool again = m.StartStation(m.Stations[0]);
                Log($"REUSE same day refused={!again}");

                // Free practice on the agility station: runs until "Esc" (StopRequested), no points.
                var dodge = m.Stations.Find(st => st is DodgeMinigame) as DodgeMinigame;
                int agi0 = p.Agility;
                bool freeOk = m.StartStation(dodge, true);
                yield return Wait(9f);
                yield return Shot("t8_libre_agilidad");
                dodge.StopRequested = true;
                while (m.Busy) yield return null;
                Log($"FREE practice ok={freeOk} bestStreak={dodge.BestStreak} speedAtStreak10=x{DodgeMinigame.SpeedForStreak(10):0.00} " +
                    $"x{DodgeMinigame.SpeedForStreak(13):0.00}@13 agilityUnchanged={p.Agility == agi0}");

                // Training partners: every extra station busy; hitting one does no damage; talking.
                var boot = FindAnyObjectByType<TrainingBootstrap>();
                int busy = 0;
                foreach (var st in boot.Partners) if (st.Occupied) busy++;
                Log($"PARTNERS npcs={TrainingNPC.All.Count} stationsOccupied={busy}/{boot.Partners.Count}");
                var npc = TrainingNPC.All[0];
                float hp0 = npc.Fighter.Health.Current;
                var pl = m.Player;
                Vector3 to = npc.Fighter.transform.position - npc.Fighter.transform.forward * 1.2f;
                m.Controller.enabled = false;
                pl.Controller.enabled = false; pl.transform.position = to; pl.Controller.enabled = true;
                pl.SetFacing(npc.Fighter.transform.position - to);
                yield return Wait(0.4f);
                for (int k = 0; k < 3; k++)
                {
                    pl.SetFacing(npc.Fighter.transform.position - pl.transform.position);
                    pl.TryAttack(AttackType.Light);
                    yield return Wait(0.7f);
                }
                yield return Shot("t9_companero_golpeado");
                npc.Talk();
                yield return Wait(0.3f);
                Log($"PARTNER hit: hp {hp0:0}->{npc.Fighter.Health.Current:0} complaints={npc.Complaints} alive={npc.Fighter.IsAlive}");
                m.Controller.enabled = true;
                var cam = m.Cam;
                float d0 = cam.Distance, p0 = cam.Pitch;
                cam.FocusOverride = new Vector3(0, 0, 0);
                cam.Distance = 26f; cam.Pitch = 58f;
                yield return Wait(1.2f);
                yield return Shot("t10_ludus_completo");
                cam.FocusOverride = null; cam.Distance = d0; cam.Pitch = p0;
                yield return Wait(0.5f);
            }

            // ---- shop: day 1 can't afford anything, day 2 buys the shield, day 3 the sword
            m.OpenShop();
            var shop = m.Hud.Shop;
            yield return Wait(0.5f);
            if (dc.Day == 1)
            {
                foreach (int tab in new[] { 1, 2 })
                {
                    shop.SetTab(tab);
                    yield return Wait(0.3f);
                    yield return Shot("t5_tienda_" + Gladiator.UI.ShopMenu.TabNames[tab].ToLowerInvariant());
                }
                shop.SetTab(0);
                shop.Select(0);
                var r = shop.BuySelected();
                Log($"SHOP day1 buy shield with {p.Denarii} -> {r} (expected NotEnoughMoney)");
                yield return Wait(0.6f);
                yield return Shot("t5_shop_day1");
            }
            else
            {
                int index = dc.Day == 2 ? 0 : 1;
                var item = ShopCatalog.Items[index];
                if (p.Denarii < item.Price)
                {
                    Log($"GRANT {item.Price - p.Denarii} denarii (bot lost money in the arena; testing the purchase path anyway)");
                    p.AddDenarii(item.Price - p.Denarii);
                }
                shop.SelectItem(item.Id);
                int before = p.Denarii;
                var r = shop.BuySelected();
                Log($"SHOP day{dc.Day} buy {item.Id} -> {r} denarii {before}->{p.Denarii} weapon={p.ArenaWeapon} shield={p.HasShield}");
                var r2 = shop.BuySelected();
                Log($"SHOP rebuy {item.Id} -> {r2} (expected AlreadyOwned)");
                yield return Wait(0.6f);
                yield return Shot("t5_shop_day" + dc.Day);
            }
            if (dc.Day >= 2)
            {
                // Armour: a helmet and a chest piece (money granted if the bot is short), then the EQUIPO tab.
                foreach (var id in dc.Day == 2 ? new[] { "galea_bronce", "pectoral_cuero" } : new[] { "musleras_hierro", "grebas" })
                {
                    int idx2 = ShopCatalog.Items.FindIndex(it => it.Id == id);
                    var it2 = ShopCatalog.Items[idx2];
                    if (p.Denarii < it2.Price) p.AddDenarii(it2.Price - p.Denarii);
                    shop.SelectItem(id);
                    var r3 = shop.BuySelected();
                    Log($"SHOP armour {id} -> {r3} weight={p.ArmorSet.Weight:0.#}kg");
                }
                shop.SetTab(3);
                shop.Select(8);
                yield return Wait(0.6f);
                Log($"EQUIPO {Loadout.WeightLine(p)}");
                yield return Shot("t6_equipo_day" + dc.Day);
                yield return Wait(0.3f);
            }
            shop.Close();
            Log($"SHOP closed busy={m.Busy} controller={m.Controller.enabled}");
            yield return Wait(0.5f);

            // The gate menu: day 1 duel, day 2 waves, day 3 the champion.
            var target = dc.Day == 1 ? EncounterType.Duel : dc.Day == 2 ? EncounterType.Waves : EncounterType.Champion;
            m.OpenGate();
            yield return Wait(0.5f);
            var gate = Gladiator.UI.ArenaGateMenu.Instance;
            gate.Select(System.Array.IndexOf(Encounters.MenuOrder, target));
            yield return Wait(0.3f);
            if (dc.Day == 1) yield return Shot("t7_puerta");
            Log($"GATE open={gate.IsOpen} choose={target}");
            gate.Choose();
        }
    }
}
