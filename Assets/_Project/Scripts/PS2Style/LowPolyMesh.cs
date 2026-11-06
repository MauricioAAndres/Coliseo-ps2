using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gladiator.PS2Style
{
    /// <summary>
    /// Small procedural mesh builder used for every placeholder in the prototype.
    /// Meshes are deliberately low-poly (6-32 segments) and always carry vertex colours,
    /// because PS2_Lit reads baked ambient light from them.
    /// Winding: Unity front faces are clockwise as seen from the viewer.
    /// </summary>
    public class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> tris = new List<int>();

        public int VertexCount => verts.Count;

        public int Add(Vector3 p, Vector3 n, Vector2 uv, Color c)
        {
            verts.Add(p); normals.Add(n); uvs.Add(uv); colors.Add(c);
            return verts.Count - 1;
        }

        public void Tri(int a, int b, int c) { tris.Add(a); tris.Add(b); tris.Add(c); }
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        static float Extent(Vector3 axis, Vector3 half)
        {
            return Mathf.Abs(axis.x) * half.x + Mathf.Abs(axis.y) * half.y + Mathf.Abs(axis.z) * half.z;
        }

        /// <param name="uvScale">0 = each face maps 0..1, otherwise UVs = size * uvScale (world-ish tiling).</param>
        public void AddBox(Vector3 center, Vector3 size, Quaternion rot, Color color, float uvScale = 0f)
        {
            Vector3 h = size * 0.5f;
            AddBoxFace(center, rot, Vector3.right, Vector3.up, h, color, uvScale);
            AddBoxFace(center, rot, Vector3.left, Vector3.up, h, color, uvScale);
            AddBoxFace(center, rot, Vector3.forward, Vector3.up, h, color, uvScale);
            AddBoxFace(center, rot, Vector3.back, Vector3.up, h, color, uvScale);
            AddBoxFace(center, rot, Vector3.up, Vector3.forward, h, color, uvScale);
            AddBoxFace(center, rot, Vector3.down, Vector3.forward, h, color, uvScale);
        }

        void AddBoxFace(Vector3 center, Quaternion rot, Vector3 n, Vector3 u, Vector3 h, Color col, float uvScale)
        {
            Vector3 r = Vector3.Cross(u, -n);
            float en = Extent(n, h), er = Extent(r, h), eu = Extent(u, h);
            Vector3 c = n * en;
            Vector3 bl = c - r * er - u * eu, tl = c - r * er + u * eu, tr = c + r * er + u * eu, br = c + r * er - u * eu;
            float us = uvScale > 0f ? er * 2f * uvScale : 1f;
            float vs = uvScale > 0f ? eu * 2f * uvScale : 1f;
            Vector3 nw = rot * n;
            int a = Add(center + rot * bl, nw, new Vector2(0, 0), col);
            int b = Add(center + rot * tl, nw, new Vector2(0, vs), col);
            int d = Add(center + rot * tr, nw, new Vector2(us, vs), col);
            int e = Add(center + rot * br, nw, new Vector2(us, 0), col);
            Quad(a, b, d, e);
        }

        /// <summary>Profile entry for a lathe: ring radius, height and 2D normal (radial, up).</summary>
        public struct Ring
        {
            public float Radius, Y, NR, NY;
            public Ring(float radius, float y, float nr, float ny) { Radius = radius; Y = y; NR = nr; NY = ny; }
        }

        /// <summary>Revolves a profile around Y. inward = faces point to the axis (arena walls, sky dome).</summary>
        public void AddLathe(IList<Ring> profile, int segments, Vector3 offset, Color color, bool inward = false,
                             float uTile = 1f, float vScale = 0f)
        {
            int rows = profile.Count;
            int start = verts.Count;
            for (int j = 0; j < rows; j++)
            {
                Ring r = profile[j];
                float v = vScale > 0f ? r.Y * vScale : (rows > 1 ? j / (float)(rows - 1) : 0f);
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 p = offset + dir * r.Radius + Vector3.up * r.Y;
                    Vector3 n = (dir * r.NR + Vector3.up * r.NY).normalized;
                    if (inward) n = -n;
                    Add(p, n, new Vector2(i / (float)segments * uTile, v), color);
                }
            }
            int stride = segments + 1;
            for (int j = 0; j < rows - 1; j++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int b0 = start + j * stride + i, b1 = b0 + 1;
                    int t0 = b0 + stride, t1 = t0 + 1;
                    if (inward) Quad(b0, b1, t1, t0);
                    else Quad(b0, t0, t1, b1);
                }
            }
        }

        /// <summary>Flat disc subdivided in concentric rings (needed so vertex lighting has vertices to work with).</summary>
        public void AddPolarDisc(float radius, int rings, int segments, float y, Color color, float uvWorldScale, bool facingUp = true)
        {
            Vector3 n = facingUp ? Vector3.up : Vector3.down;
            int center = Add(new Vector3(0, y, 0), n, Vector2.zero, color);
            int ringStart = verts.Count;
            for (int k = 1; k <= rings; k++)
            {
                float rr = radius * k / rings;
                for (int i = 0; i < segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    Vector3 p = new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr);
                    Add(p, n, new Vector2(p.x, p.z) * uvWorldScale, color);
                }
            }
            for (int i = 0; i < segments; i++)
            {
                int i0 = ringStart + i, i1 = ringStart + (i + 1) % segments;
                if (facingUp) Tri(center, i1, i0); else Tri(center, i0, i1);
            }
            for (int k = 0; k < rings - 1; k++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int in0 = ringStart + k * segments + i;
                    int in1 = ringStart + k * segments + (i + 1) % segments;
                    int out0 = in0 + segments, out1 = in1 + segments;
                    if (facingUp) Quad(in0, in1, out1, out0); else Quad(in0, out0, out1, in1);
                }
            }
        }

        /// <summary>Upward facing ring between two radii (stand steps, wall top).</summary>
        public void AddAnnulus(float rIn, float rOut, float y, int segments, Color color, float uvWorldScale)
        {
            int start = verts.Count;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                Vector3 pi = d * rIn + Vector3.up * y, po = d * rOut + Vector3.up * y;
                Add(pi, Vector3.up, new Vector2(pi.x, pi.z) * uvWorldScale, color);
                Add(po, Vector3.up, new Vector2(po.x, po.z) * uvWorldScale, color);
            }
            for (int i = 0; i < segments; i++)
            {
                int i0 = start + i * 2, o0 = i0 + 1;
                int i1 = start + ((i + 1) % segments) * 2, o1 = i1 + 1;
                Quad(i0, i1, o1, o0);
            }
        }

        public void AddDiscCap(float radius, float y, int segments, Color color, bool up)
        {
            Vector3 n = up ? Vector3.up : Vector3.down;
            int c = Add(new Vector3(0, y, 0), n, new Vector2(0.5f, 0.5f), color);
            int s = verts.Count;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius), n,
                    new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f), color);
            }
            for (int i = 0; i < segments; i++)
            {
                int a0 = s + i, a1 = s + (i + 1) % segments;
                if (up) Tri(c, a1, a0); else Tri(c, a0, a1);
            }
        }

        /// <summary>Upward-facing subdivided rectangle centred on the origin (floors that need vertex lighting).</summary>
        public void AddGrid(float width, float depth, int divX, int divZ, float y, Color color, float uvWorldScale)
        {
            int start = verts.Count;
            for (int z = 0; z <= divZ; z++)
                for (int x = 0; x <= divX; x++)
                {
                    Vector3 p = new Vector3(-width * 0.5f + width * x / divX, y, -depth * 0.5f + depth * z / divZ);
                    Add(p, Vector3.up, new Vector2(p.x, p.z) * uvWorldScale, color);
                }
            int row = divX + 1;
            for (int z = 0; z < divZ; z++)
                for (int x = 0; x < divX; x++)
                {
                    int a = start + z * row + x;
                    Quad(a, a + row, a + row + 1, a + 1);
                }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    public static class LowPolyMesh
    {
        public static Mesh Box(Vector3 size, Vector3 center = default, float uvScale = 0f)
        {
            var b = new MeshBuilder();
            b.AddBox(center, size, Quaternion.identity, Color.white, uvScale);
            return b.Build("LP_Box");
        }

        public static Mesh Cylinder(float radius, float height, int segments, bool caps = true, bool inward = false, float uTile = 1f, float vScale = 0f)
        {
            var b = new MeshBuilder();
            var prof = new[] { new MeshBuilder.Ring(radius, 0, 1, 0), new MeshBuilder.Ring(radius, height, 1, 0) };
            b.AddLathe(prof, segments, Vector3.zero, Color.white, inward, uTile, vScale);
            if (caps)
            {
                b.AddDiscCap(radius, height, segments, Color.white, true);
                b.AddDiscCap(radius, 0, segments, Color.white, false);
            }
            return b.Build("LP_Cylinder");
        }

        /// <summary>Capsule with its base at y = 0.</summary>
        public static Mesh Capsule(float radius, float height, int segments = 8, int hemiRings = 3)
        {
            var prof = new List<MeshBuilder.Ring>();
            for (int k = 0; k <= hemiRings; k++)
            {
                float t = -90f + 90f * k / hemiRings;
                float rad = t * Mathf.Deg2Rad;
                prof.Add(new MeshBuilder.Ring(radius * Mathf.Cos(rad), radius + radius * Mathf.Sin(rad), Mathf.Cos(rad), Mathf.Sin(rad)));
            }
            for (int k = 0; k <= hemiRings; k++)
            {
                float t = 90f * k / hemiRings;
                float rad = t * Mathf.Deg2Rad;
                prof.Add(new MeshBuilder.Ring(radius * Mathf.Cos(rad), height - radius + radius * Mathf.Sin(rad), Mathf.Cos(rad), Mathf.Sin(rad)));
            }
            var b = new MeshBuilder();
            b.AddLathe(prof, segments, Vector3.zero, Color.white);
            return b.Build("LP_Capsule");
        }

        /// <summary>Sphere centred at origin.</summary>
        public static Mesh Sphere(float radius, int segments = 8, int rings = 6, bool inward = false, float lowerLimitDeg = -90f)
        {
            var prof = new List<MeshBuilder.Ring>();
            for (int k = 0; k <= rings; k++)
            {
                float t = Mathf.Lerp(lowerLimitDeg, 90f, k / (float)rings) * Mathf.Deg2Rad;
                prof.Add(new MeshBuilder.Ring(radius * Mathf.Cos(t), radius * Mathf.Sin(t), Mathf.Cos(t), Mathf.Sin(t)));
            }
            var b = new MeshBuilder();
            b.AddLathe(prof, segments, Vector3.zero, Color.white, inward);
            return b.Build("LP_Sphere");
        }

        public static Mesh Disc(float radius, int segments)
        {
            var b = new MeshBuilder();
            b.AddDiscCap(radius, 0f, segments, Color.white, true);
            return b.Build("LP_Disc");
        }

        /// <summary>
        /// Bakes a hemispheric ambient term (sky above / bounced ground light below) into the
        /// vertex colours of every mesh under root. Existing vertex colours are multiplied,
        /// so per-vertex tints (crowd, AO) survive.
        /// </summary>
        public static void BakeAmbient(GameObject root, Color sky, Color ground)
        {
            var done = new HashSet<Mesh>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null || !done.Add(m)) continue;
                Vector3[] n = m.normals;
                Color[] cols = m.colors;
                if (cols == null || cols.Length != n.Length)
                {
                    cols = new Color[n.Length];
                    for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
                }
                for (int i = 0; i < n.Length; i++)
                {
                    Vector3 wn = mf.transform.TransformDirection(n[i]);
                    float t = wn.y * 0.5f + 0.5f;
                    Color hemi = Color.Lerp(ground, sky, t);
                    Color c = cols[i];
                    cols[i] = new Color(c.r * hemi.r, c.g * hemi.g, c.b * hemi.b, c.a);
                }
                m.colors = cols;
            }
        }
    }
}
