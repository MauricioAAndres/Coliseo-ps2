using System.Collections.Generic;
using Gladiator.Combat;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>
    /// The little brain of a materialised townsperson. The simulation says where they should be; this walks the body
    /// there along the streets (navmesh corners), waits facing the place (the stall, the fountain, the town from a gate),
    /// looks at the player (the poser's head layer does it), greets or provokes when you come close depending on what
    /// they think of you, runs from you if you hit them, and walks to their door when they go indoors.
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class CitizenNPC : MonoBehaviour
    {
        public Fighter Fighter { get; private set; }
        public CitizenData Citizen { get; private set; }
        public int LastCitizenId = -1;
        /// <summary>Went indoors (reached the door): the spawner takes the body back.</summary>
        public bool Finished { get; private set; }
        public bool Fleeing => fleeTimer > 0f;
        public bool Walking => corners.Count > 0 && !arrived;
        /// <summary>Heading for their door (home, or work inside) and for how long.</summary>
        public bool GoingIn => goingIn && !arrived;
        public float GoingInSeconds { get; private set; }
        /// <summary>Metres walked since materialising (tests).</summary>
        public float Travelled { get; private set; }

        TownSimulation sim;
        readonly List<Vector3> corners = new List<Vector3>();
        Vector3 goal, anchor, lastPos;
        float wanderTimer;
        string goalPlace;
        bool arrived, goingIn;
        float repath, fleeTimer, greetCooldown, stuckTimer, idleTurn;
        Vector3 fleeFrom;
        Vector3 faceDir;

        void Awake() { Fighter = GetComponent<Fighter>(); }

        public void Assign(CitizenData c, Vector3 at, TownSimulation simulation)
        {
            sim = simulation;
            Citizen = c;
            LastCitizenId = c.Id;
            Finished = false;
            goingIn = false;
            arrived = false;
            fleeTimer = 0f;
            Travelled = 0f;
            GoingInSeconds = 0f;
            goalPlace = null;
            corners.Clear();
            transform.localScale = Vector3.one * (c.Age < 16 ? 0.72f : c.Age > 60 ? 0.96f : 1f);
            Fighter.Controller.enabled = false;
            transform.SetPositionAndRotation(at + Vector3.up * 0.05f, Quaternion.Euler(0, (c.Id * 47) % 360, 0));
            Fighter.Controller.enabled = true;
            lastPos = transform.position;
            repath = 0f;
            gameObject.name = "Citizen_" + c.Name;
        }

        public void Clear() { Citizen = null; corners.Clear(); Fighter.Move(Vector3.zero); }

        public void Flee(Vector3 from, float seconds)
        {
            fleeFrom = from;
            fleeTimer = Mathf.Max(fleeTimer, seconds);
            corners.Clear();
        }

        float WalkSpeed => Citizen == null ? 0.3f : Citizen.Age < 16 ? 0.36f : Citizen.Age > 60 ? 0.24f : 0.31f;

        void Update()
        {
            if (Citizen == null || sim == null || !Fighter.IsAlive) return;
            float dt = Time.deltaTime;
            float moved = Flat(transform.position - lastPos); // since the last frame
            Travelled += moved;
            lastPos = transform.position;
            greetCooldown -= dt;
            GoingInSeconds = goingIn ? GoingInSeconds + dt : 0f;

            if (fleeTimer > 0f)
            {
                fleeTimer -= dt;
                Vector3 away = transform.position - fleeFrom; away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = transform.forward;
                Fighter.Move(away.normalized);           // full run
                Fighter.SetFacing(away);
                if (fleeTimer <= 0f) repath = 0f;
                return;
            }

            // where the simulation wants them now
            var loc = sim.WhereIs(Citizen, sim.Hour);
            string place = loc.PlaceId;
            if (loc.Indoors || (loc.Walking && IsIndoorPlace(place)))
            {
                // going home / to work inside: walk to the door and disappear through it
                var pl = sim.Place(place);
                SetGoal(pl != null ? pl.Position : loc.Position, place + "#door", true);
            }
            else
            {
                Vector3 target = loc.Walking ? SpotAtEnd(loc) : loc.Position;
                SetGoal(target, place, false);
            }

            if ((repath -= dt) <= 0f && !arrived) { TownNavMesh.Path(transform.position, goal, corners); repath = 3f; }

            if (!arrived && corners.Count > 0)
            {
                Vector3 to = corners[0] - transform.position; to.y = 0f;
                if (to.magnitude < 0.45f) { corners.RemoveAt(0); if (corners.Count == 0) Arrive(); }
                else
                {
                    Fighter.Move(to.normalized * WalkSpeed);
                    Fighter.SetFacing(to);
                    // stuck against something (another body, a corner): ask for a new path, then hop the corner
                    if (Fighter.State == FighterState.Idle && moved < 0.2f * dt) stuckTimer += dt; else stuckTimer = 0f;
                    if (stuckTimer > 1.5f) { stuckTimer = 0f; repath = 0f; Fighter.Move(Quaternion.Euler(0, 70f, 0) * to.normalized * 0.5f); }
                }
            }
            else Idle(dt);

            React();
        }

        bool IsIndoorPlace(string id)
        {
            var p = sim.Place(id);
            return p != null && (p.Kind == PlaceKind.Home || (p.Kind == PlaceKind.Work && !id.StartsWith("garden")));
        }

        // walking in the simulation: the body heads straight for the end of the trip
        Vector3 SpotAtEnd(CitizenLocation loc)
        {
            var later = sim.WhereIs(Citizen, sim.Hour + 2f);
            return later.PlaceId == loc.PlaceId && !later.Walking ? later.Position : (sim.Place(loc.PlaceId)?.Position ?? loc.Position);
        }

        void SetGoal(Vector3 target, string placeKey, bool door)
        {
            if (placeKey == goalPlace && (target - anchor).sqrMagnitude < 1f) return;
            goalPlace = placeKey;
            goal = anchor = target;
            wanderTimer = Random.Range(4f, 10f);
            goingIn = door;
            arrived = false;
            repath = 0f;
            faceDir = FaceFor(placeKey, target);
        }

        void Arrive()
        {
            arrived = true;
            Fighter.Move(Vector3.zero);
            if (goingIn) Finished = true; // through the door: back to data
        }

        Vector3 FaceFor(string placeKey, Vector3 target)
        {
            var p = sim.Place(placeKey);
            if (p == null) return transform.forward;
            switch (p.Kind)
            {
                case PlaceKind.Market:
                    foreach (var s in sim.Layout.Stalls) if ((s.Key + s.Value * 1.4f - p.Position).sqrMagnitude < 0.01f) return -s.Value;
                    return transform.forward;
                case PlaceKind.Fountain: return sim.Layout.Fountain - target;
                case PlaceKind.ArenaGate: return sim.Layout.ArenaFacing;
                case PlaceKind.LudusGate: return sim.Layout.LudusFacing;
                case PlaceKind.GuardPost: return p.Facing;
                default: return transform.forward;
            }
        }

        void Idle(float dt)
        {
            Fighter.Move(Vector3.zero);
            if (faceDir.sqrMagnitude > 0.01f && goalPlace != null && !goalPlace.StartsWith("plaza")) Fighter.SetFacing(faceDir);
            else if ((idleTurn -= dt) <= 0f)
            {
                // strolling in the plaza: turn to look around now and then
                idleTurn = Random.Range(3f, 7f);
                Fighter.SetFacing(Quaternion.Euler(0, Random.Range(-120f, 120f), 0) * transform.forward);
            }
            // strolling (plaza, fountain, children playing): wander between points around the spot now and then
            bool strolls = goalPlace == "plaza" || goalPlace == "fountain" || (Citizen.Job == Job.Nino && !goingIn);
            if (strolls && (wanderTimer -= dt) <= 0f)
            {
                wanderTimer = Citizen.Job == Job.Nino ? Random.Range(2f, 5f) : Random.Range(7f, 16f);
                Vector2 r = Random.insideUnitCircle * (Citizen.Job == Job.Nino ? 6f : 4.5f);
                if (TownNavMesh.Snap(anchor + new Vector3(r.x, 0, r.y), out Vector3 w, 2f)) { goal = w; arrived = false; repath = 0f; return; }
            }
            // drift back if pushed off the spot
            if (Flat(goal - transform.position) > 1.5f) { arrived = false; repath = 0f; }
        }

        /// <summary>The player comes close: a friend cheers, an enemy beats his chest at you; the rest just look.</summary>
        void React()
        {
            var player = TownBootstrap.Instance != null ? TownBootstrap.Instance.Player : null;
            if (player == null || greetCooldown > 0f) return;
            if (Flat(player.transform.position - transform.position) > 3.5f) return;
            greetCooldown = 40f;
            Vector3 to = player.transform.position - transform.position; to.y = 0f;
            Fighter.SetFacing(to);
            int rel = Citizen.Relation;
            if (rel >= 25) Fighter.TryTaunt(2);       // cheer
            else if (rel <= -25) Fighter.TryTaunt(0); // provocation
        }

        /// <summary>What they say when you talk to them (E): depends on what they are doing and what they think of you.</summary>
        public string Say()
        {
            if (Citizen == null) return "";
            var loc = sim.WhereIs(Citizen, sim.Hour);
            int rel = Citizen.Relation;
            string doing;
            switch (loc.Activity)
            {
                case Activity.Trabajar: doing = Citizen.Job == Job.Guardia ? "Estoy de guardia. Circula." : Citizen.Job == Job.Mercader ? "Fruta fresca, higos, aceitunas!" : "Tengo trabajo que hacer."; break;
                case Activity.Mercado: doing = "Busco algo bueno en el mercado."; break;
                case Activity.Pasear: doing = Citizen.Job == Job.Nino ? "Jugamos a gladiadores!" : "Un buen dia para pasear."; break;
                case Activity.Agua: doing = "Voy por agua a la fuente."; break;
                case Activity.Rezar: doing = "Que los dioses te protejan."; break;
                case Activity.Banarse: doing = "A las termas, que hoy toca."; break;
                case Activity.Taberna: case Activity.Comer: doing = "Me espera un buen vino."; break;
                default: doing = "Ya me voy a casa."; break;
            }
            string mood = rel >= 60 ? "Eres el mejor de la arena! " : rel >= 25 ? "Ave, gladiador! " : rel > -25 ? "" : rel > -60 ? "Que quieres? " : "Apartate de mi, bruto. ";
            if (loc.Walking) doing = "Voy a " + sim.PlaceName(loc.PlaceId).ToLowerInvariant() + ".";
            if (lastTalkDay != sim.Day) { lastTalkDay = sim.Day; sim.ChangeRelation(Citizen, 1); } // a chat a day helps a little
            return Citizen.Name.ToUpperInvariant() + " (" + Citizen.JobName + "): " + mood + doing;
        }
        int lastTalkDay = -1;

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }
    }
}
