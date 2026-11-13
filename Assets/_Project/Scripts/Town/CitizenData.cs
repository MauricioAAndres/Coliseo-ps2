using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.Town
{
    /// <summary>What a citizen does for a living (decides the workplace and the timetable).</summary>
    public enum Job
    {
        Herrero, Panadero, Tabernero, Sacerdote, Alfarero, Banero, Escriba, Tendero, Mercader, Guardia, Hortelano,
        Aguador, AmaDeCasa, Anciano, Nino
    }

    /// <summary>When the working hours fall: morning (7-15), afternoon (13-21), night (21-5) or none.</summary>
    public enum Shift { Manana, Tarde, Noche, Ninguno }

    /// <summary>What a citizen is doing at a given time.</summary>
    public enum Activity { Dormir, EnCasa, Trabajar, Comer, Mercado, Rezar, Banarse, Pasear, Taberna, Agua, Reunion }

    /// <summary>One row of a timetable: from this hour on, this activity at this place (until the next row).</summary>
    [System.Serializable]
    public struct ScheduleEntry
    {
        public float Start;         // hour of the day, 0..24
        public Activity Activity;
        public string PlaceId;      // a TownPlace id (home12, work3, plaza, fountain, stall2, arena_gate...)
        public ScheduleEntry(float start, Activity a, string place) { Start = start; Activity = a; PlaceId = place; }
        public override string ToString() => $"{TownSimulation.Clock(Start)} {Activity} @{PlaceId}";
    }

    /// <summary>
    /// A townsperson as pure data: no GameObject. Who they are, where they live and work, their timetable and what they
    /// think of you. The town simulation answers where they are at any hour from this alone.
    /// </summary>
    [System.Serializable]
    public class CitizenData
    {
        public int Id;
        public string Name;
        public string Family;        // shared by the people of one house
        public bool Female;
        public int Age;
        public Job Job;
        public Shift Shift;
        public string HomeId;        // TownPlace id of the house
        public string WorkId;        // TownPlace id of the workplace (null: no job)
        /// <summary>What they think of the player: -100 (hate) .. 100 (adore). Saved.</summary>
        public int Relation;
        /// <summary>Base timetable (the leisure stops change a little from day to day, see TownSimulation.ScheduleFor).</summary>
        public List<ScheduleEntry> Schedule = new List<ScheduleEntry>();
        /// <summary>Where exactly they stand at a busy place (a few metres around it), so a crowd does not pile up.</summary>
        public Vector2 SpotOffset;
        /// <summary>Seed for the body they get when they materialise (tunic colour, height, hair...).</summary>
        public int LookSeed;

        public string JobName => TownSimulation.JobName(Job, Female);
        public string RelationLabel => Relation >= 60 ? "te adora" : Relation >= 25 ? "te aprecia" : Relation > -25 ? "neutral"
                                     : Relation > -60 ? "no te soporta" : "te odia";
        public override string ToString() => $"#{Id} {Name} ({JobName}, {Age}) casa={HomeId} trabajo={WorkId ?? "-"} turno={Shift} relacion={Relation}";
    }

    /// <summary>The answer to "where is this citizen at this hour".</summary>
    public struct CitizenLocation
    {
        public Activity Activity;
        public string PlaceId;       // where they are, or where they are going while walking
        public string FromPlaceId;   // where they came from (walking only)
        public bool Walking;
        public bool Indoors;         // at home, asleep or working inside a building (no body needed in the street)
        public float Progress;       // 0..1 along the walk
        public Vector3 Position;     // world position in the town (straight line while walking)
        public override string ToString() =>
            Walking ? $"de camino ({Progress:P0}) de {FromPlaceId} a {PlaceId} para {Activity}" : $"{Activity} en {PlaceId}" + (Indoors ? " (dentro)" : "");
    }
}
