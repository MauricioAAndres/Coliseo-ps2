using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Gladiator.Town
{
    /// <summary>
    /// Bakes a navigation mesh for the town at load time from its colliders (ground, houses, walls, fountain, stalls),
    /// so the townsfolk follow the streets instead of walking through houses. Paths are asked with
    /// <see cref="Path"/>; the citizens steer their Fighter along the corners (no NavMeshAgent).
    /// </summary>
    public static class TownNavMesh
    {
        static NavMeshDataInstance instance;
        public static bool Ready { get; private set; }
        public static float BuildSeconds { get; private set; }
        public static int Triangles { get; private set; }

        public static void Build(Vector3 center, Vector3 size)
        {
            Clear();
            float t0 = Time.realtimeSinceStartup;
            var bounds = new Bounds(center, size);
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            // the fighters themselves are not obstacles
            sources.RemoveAll(s => s.component != null && s.component.GetComponentInParent<Combat.Fighter>() != null);
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.35f;
            settings.agentHeight = 1.8f;
            settings.agentClimb = 0.3f;
            settings.agentSlope = 40f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.15f;
            settings.overrideTileSize = true;
            settings.tileSize = 128;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) { Debug.LogWarning("[TOWN] navmesh build failed"); return; }
            instance = NavMesh.AddNavMeshData(data);
            Ready = instance.valid;
            BuildSeconds = Time.realtimeSinceStartup - t0;
            Triangles = NavMesh.CalculateTriangulation().indices.Length / 3;
            Debug.Log($"[TOWN] navmesh sources={sources.Count} tris={Triangles} in {BuildSeconds:0.00}s");
        }

        public static void Clear()
        {
            if (instance.valid) NavMesh.RemoveNavMeshData(instance);
            instance = default;
            Ready = false;
        }

        /// <summary>Nearest walkable point (within a few metres), or the point itself if there is none.</summary>
        public static bool Snap(Vector3 p, out Vector3 snapped, float radius = 4f)
        {
            if (Ready && NavMesh.SamplePosition(p, out var hit, radius, NavMesh.AllAreas)) { snapped = hit.position; return true; }
            snapped = p;
            return false;
        }

        static readonly NavMeshPath scratch = new NavMeshPath();

        /// <summary>Street corners from a to b (both snapped). Falls back to a straight line.</summary>
        public static void Path(Vector3 a, Vector3 b, List<Vector3> corners)
        {
            corners.Clear();
            if (Ready && Snap(a, out var sa, 3f) && Snap(b, out var sb, 4f) && NavMesh.CalculatePath(sa, sb, NavMesh.AllAreas, scratch)
                && scratch.status != NavMeshPathStatus.PathInvalid)
            {
                var c = scratch.corners;
                for (int i = 1; i < c.Length; i++) corners.Add(c[i]);
                if (corners.Count > 0) return;
            }
            corners.Add(b);
        }
    }
}
