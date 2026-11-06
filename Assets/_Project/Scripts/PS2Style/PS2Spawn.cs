using UnityEngine;
using UnityEngine.Rendering;

namespace Gladiator.PS2Style
{
    /// <summary>Helpers to drop procedural meshes into the scene with PS2-friendly renderer settings.</summary>
    public static class PS2Spawn
    {
        public static GameObject Mesh(Transform parent, string name, Mesh mesh, Vector3 localPos, Material mat, Quaternion? localRot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On; // only drawn with the PS3 look (PS2: shadow distance 0)
            mr.receiveShadows = false;
            return go;
        }

        public static GameObject Box(Transform parent, string name, Vector3 size, Vector3 localPos, Material mat, Quaternion? localRot = null, float uvScale = 0f)
            => Mesh(parent, name, LowPolyMesh.Box(size, Vector3.zero, uvScale), localPos, mat, localRot);

        public static BoxCollider BoxCollider(Transform parent, Vector3 center, Vector3 size, Quaternion? rot = null)
        {
            var go = new GameObject("Col");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = rot ?? Quaternion.identity;
            var c = go.AddComponent<BoxCollider>();
            c.size = size;
            return c;
        }

        /// <summary>Flat unlit dark disc used as a fake shadow.</summary>
        public static GameObject Blob(Transform parent, float radius, Vector3 localPos)
        {
            return Mesh(parent, "Blob", LowPolyMesh.Disc(radius, 10), localPos, PS2Materials.Flat(new Color(0.3f, 0.22f, 0.14f)));
        }
    }
}
