using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Gladiator.Character
{
    /// <summary>Up to two bone influences per vertex (all we need for a low-poly PS2 body).</summary>
    public struct BW
    {
        public int A, B;
        public float WB;
        public BW(int a) { A = a; B = a; WB = 0f; }
        public BW(int a, int b, float wb) { A = a; B = b; WB = wb; }
        public static implicit operator BW(int bone) => new BW(bone);
    }

    /// <summary>
    /// Builds a multi-material skinned mesh out of lofts (rings along a limb), parametric grids (head, hair)
    /// and boxes. Normals are computed per "smooth group" so UV seams don't show, and the PS2 hemispheric
    /// ambient is baked into vertex colours like every other mesh in the prototype.
    /// </summary>
    public class SkinnedMeshBuilder
    {
        public const int SubCount = 9;

        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<BoneWeight> weights = new List<BoneWeight>();
        readonly List<int> groups = new List<int>();
        readonly List<int>[] tris = new List<int>[SubCount];
        int nextGroup;

        public SkinnedMeshBuilder()
        {
            for (int i = 0; i < SubCount; i++) tris[i] = new List<int>();
        }

        public int TriangleCount
        {
            get { int n = 0; foreach (var t in tris) n += t.Count / 3; return n; }
        }

        public struct Ring
        {
            public Vector3 C, U, V;
            public float RU, RVPos, RVNeg;
            public BW W;
            public Ring(Vector3 c, Vector3 u, Vector3 v, float ru, float rvPos, float rvNeg, BW w)
            { C = c; U = u; V = v; RU = ru; RVPos = rvPos; RVNeg = rvNeg; W = w; }

            public Ring Inflated(float mul, float add)
            {
                var r = this;
                r.RU = RU * mul + add; r.RVPos = RVPos * mul + add; r.RVNeg = RVNeg * mul + add;
                return r;
            }

            public Vector3 Point(float a)
            {
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                return C + U * (c * RU) + V * (s * (s >= 0f ? RVPos : RVNeg));
            }
        }

        int Add(Vector3 p, Vector2 uv, BW w, int group)
        {
            verts.Add(p);
            uvs.Add(uv);
            groups.Add(group);
            var bw = new BoneWeight { boneIndex0 = w.A, weight0 = 1f - w.WB, boneIndex1 = w.B, weight1 = w.WB };
            if (w.A == w.B) { bw.weight0 = 1f; bw.weight1 = 0f; bw.boneIndex1 = 0; }
            weights.Add(bw);
            return verts.Count - 1;
        }

        /// <summary>Adds a triangle and flips it if needed so its front face points along <paramref name="outward"/>.</summary>
        void TriFacing(int sub, int a, int b, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            if (Vector3.Dot(n, outward) < 0f) { int t = b; b = c; c = t; }
            tris[sub].Add(a); tris[sub].Add(b); tris[sub].Add(c);
        }

        /// <summary>Loft through rings (all with the same segment count). Caps close the first/last ring.</summary>
        public void Loft(IList<Ring> rings, int segments, int sub, bool capStart, bool capEnd, float uvV = 2f)
        {
            int group = nextGroup++;
            int stride = segments + 1;
            int start = verts.Count;
            float v = 0f;
            for (int j = 0; j < rings.Count; j++)
            {
                if (j > 0) v += Vector3.Distance(rings[j].C, rings[j - 1].C) * uvV;
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    Add(rings[j].Point(a), new Vector2(i / (float)segments, v), rings[j].W, group);
                }
            }
            for (int j = 0; j < rings.Count - 1; j++)
            {
                Vector3 mid = (rings[j].C + rings[j + 1].C) * 0.5f;
                for (int i = 0; i < segments; i++)
                {
                    int a = start + j * stride + i, b = a + 1, c = a + stride, d = c + 1;
                    Vector3 qc = (verts[a] + verts[b] + verts[c] + verts[d]) * 0.25f;
                    Vector3 outward = qc - mid;
                    TriFacing(sub, a, c, d, outward);
                    TriFacing(sub, a, d, b, outward);
                }
            }
            if (capStart) Cap(rings[0], rings.Count > 1 ? rings[0].C - rings[1].C : -rings[0].V, start, segments, sub);
            if (capEnd)
            {
                int last = rings.Count - 1;
                Cap(rings[last], rings.Count > 1 ? rings[last].C - rings[last - 1].C : rings[last].V, start + last * stride, segments, sub);
            }
        }

        void Cap(Ring r, Vector3 outward, int ringStart, int segments, int sub)
        {
            int group = nextGroup++;
            int c = Add(r.C, new Vector2(0.5f, 0.5f), r.W, group);
            var idx = new int[segments];
            for (int i = 0; i < segments; i++)
            {
                Vector3 p = verts[ringStart + i];
                idx[i] = Add(p, uvs[ringStart + i], r.W, group);
            }
            for (int i = 0; i < segments; i++)
                TriFacing(sub, c, idx[i], idx[(i + 1) % segments], outward);
        }

        /// <summary>Parametric patch: point(u, v) for u in [0..cols], v in [0..rows]; front faces point away from <paramref name="inside"/>.</summary>
        public void Grid(int cols, int rows, System.Func<int, int, Vector3> point, System.Func<int, int, BW> weight,
                         Vector3 inside, int sub)
        {
            int group = nextGroup++;
            int start = verts.Count;
            for (int r = 0; r <= rows; r++)
                for (int c = 0; c <= cols; c++)
                    Add(point(c, r), new Vector2(c / (float)cols, r / (float)rows), weight(c, r), group);
            int stride = cols + 1;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int a = start + r * stride + c, b = a + 1, d = a + stride, e = d + 1;
                    Vector3 qc = (verts[a] + verts[b] + verts[d] + verts[e]) * 0.25f;
                    Vector3 outward = qc - inside;
                    TriFacing(sub, a, d, e, outward);
                    TriFacing(sub, a, e, b, outward);
                }
        }

        /// <summary>Hard-edged box (each face its own smooth group).</summary>
        public void Box(Vector3 center, Vector3 size, Quaternion rot, int sub, BW w)
        {
            Vector3 h = size * 0.5f;
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var fn in n)
            {
                Vector3 up = Mathf.Abs(fn.y) > 0.5f ? Vector3.forward : Vector3.up;
                Vector3 rt = Vector3.Cross(up, fn);
                Vector3 c0 = Vector3.Scale(fn, h);
                Vector3 ru = Vector3.Scale(rt, h), uu = Vector3.Scale(up, h);
                int group = nextGroup++;
                int a = Add(center + rot * (c0 - ru - uu), new Vector2(0, 0), w, group);
                int b = Add(center + rot * (c0 - ru + uu), new Vector2(0, 1), w, group);
                int c = Add(center + rot * (c0 + ru + uu), new Vector2(1, 1), w, group);
                int d = Add(center + rot * (c0 + ru - uu), new Vector2(1, 0), w, group);
                Vector3 outward = rot * fn;
                TriFacing(sub, a, b, c, outward);
                TriFacing(sub, a, c, d, outward);
            }
        }

        /// <summary>Triangle fan closing a loop of points (bottom of the head, etc.).</summary>
        public void Fan(Vector3 center, IList<Vector3> pts, Vector3 outward, int sub, BW w)
        {
            int group = nextGroup++;
            int c = Add(center, new Vector2(0.5f, 0.5f), w, group);
            int s = verts.Count;
            for (int i = 0; i < pts.Count; i++) Add(pts[i], new Vector2(i / (float)pts.Count, 0f), w, group);
            for (int i = 0; i < pts.Count; i++)
                TriFacing(sub, c, s + i, s + (i + 1) % pts.Count, outward);
        }

        /// <summary>Small pyramid (nose): base quad + apex.</summary>
        public void Pyramid(Vector3 b0, Vector3 b1, Vector3 b2, Vector3 b3, Vector3 apex, int sub, BW w)
        {
            Vector3 center = (b0 + b1 + b2 + b3) * 0.25f;
            Vector3[] bs = { b0, b1, b2, b3 };
            for (int i = 0; i < 4; i++)
            {
                int group = nextGroup++;
                Vector3 p0 = bs[i], p1 = bs[(i + 1) % 4];
                int a = Add(p0, new Vector2(0, 0), w, group);
                int b = Add(p1, new Vector2(1, 0), w, group);
                int c = Add(apex, new Vector2(0.5f, 1), w, group);
                Vector3 outward = (p0 + p1 + apex) / 3f - center;
                TriFacing(sub, a, b, c, outward);
            }
        }

        /// <summary>Builds the mesh: welded smooth normals per group, ambient in vertex colours, only non-empty submeshes.</summary>
        public Mesh Build(string name, Color sky, Color ground, out int[] usedSubmeshes)
        {
            int n = verts.Count;
            var normals = new Vector3[n];
            for (int s = 0; s < SubCount; s++)
            {
                var t = tris[s];
                for (int i = 0; i < t.Count; i += 3)
                {
                    Vector3 fn = Vector3.Cross(verts[t[i + 1]] - verts[t[i]], verts[t[i + 2]] - verts[t[i]]);
                    normals[t[i]] += fn; normals[t[i + 1]] += fn; normals[t[i + 2]] += fn;
                }
            }
            // Weld normals of coincident vertices of the same group (UV seams, ring wrap).
            var sum = new Dictionary<(int, int, int, int), Vector3>();
            var keys = new (int, int, int, int)[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = verts[i] * 2000f;
                var k = (Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z), groups[i]);
                keys[i] = k;
                sum.TryGetValue(k, out var acc);
                sum[k] = acc + normals[i];
            }
            var colors = new Color[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 nn = sum[keys[i]];
                nn = nn.sqrMagnitude > 1e-12f ? nn.normalized : Vector3.up;
                normals[i] = nn;
                colors[i] = Color.Lerp(ground, sky, nn.y * 0.5f + 0.5f);
            }

            var m = new Mesh { name = name };
            if (n > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.colors = colors;
            m.boneWeights = weights.ToArray();
            var used = new List<int>();
            for (int s = 0; s < SubCount; s++) if (tris[s].Count > 0) used.Add(s);
            m.subMeshCount = used.Count;
            for (int i = 0; i < used.Count; i++) m.SetTriangles(tris[used[i]], i);
            m.RecalculateBounds();
            usedSubmeshes = used.ToArray();
            return m;
        }
    }
}
