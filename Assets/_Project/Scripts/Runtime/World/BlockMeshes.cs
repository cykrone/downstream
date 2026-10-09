using System.Collections.Generic;
using UnityEngine;

namespace Downstream.World
{
    /// <summary>
    /// Mesh builders for the greybox block kit: every block is bevelled so its edges catch a thin
    /// highlight and every face is flat-shaded, so each block reads as a block (design doc:
    /// rendering rules). Stand-ins for the modelled 4 m kit pieces.
    /// </summary>
    public static class BlockMeshes
    {
        /// <summary>A box of <paramref name="size"/> with every edge chamfered by <paramref name="bevel"/>, centred on the origin.</summary>
        public static Mesh BevelledBox(Vector3 size, float bevel, string name = "BevelledBox")
        {
            bevel = Mathf.Min(bevel, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.45f);
            var h = size * 0.5f;
            var b = new MeshBuilder();

            // Six faces, inset by the bevel.
            AddQuadOutward(b, new Vector3(-h.x + bevel, h.y, -h.z + bevel), new Vector3(h.x - bevel, h.y, -h.z + bevel), new Vector3(h.x - bevel, h.y, h.z - bevel), new Vector3(-h.x + bevel, h.y, h.z - bevel), Vector3.up);
            AddQuadOutward(b, new Vector3(-h.x + bevel, -h.y, -h.z + bevel), new Vector3(h.x - bevel, -h.y, -h.z + bevel), new Vector3(h.x - bevel, -h.y, h.z - bevel), new Vector3(-h.x + bevel, -h.y, h.z - bevel), Vector3.down);
            AddQuadOutward(b, new Vector3(-h.x + bevel, -h.y + bevel, -h.z), new Vector3(h.x - bevel, -h.y + bevel, -h.z), new Vector3(h.x - bevel, h.y - bevel, -h.z), new Vector3(-h.x + bevel, h.y - bevel, -h.z), Vector3.back);
            AddQuadOutward(b, new Vector3(-h.x + bevel, -h.y + bevel, h.z), new Vector3(h.x - bevel, -h.y + bevel, h.z), new Vector3(h.x - bevel, h.y - bevel, h.z), new Vector3(-h.x + bevel, h.y - bevel, h.z), Vector3.forward);
            AddQuadOutward(b, new Vector3(-h.x, -h.y + bevel, -h.z + bevel), new Vector3(-h.x, -h.y + bevel, h.z - bevel), new Vector3(-h.x, h.y - bevel, h.z - bevel), new Vector3(-h.x, h.y - bevel, -h.z + bevel), Vector3.left);
            AddQuadOutward(b, new Vector3(h.x, -h.y + bevel, -h.z + bevel), new Vector3(h.x, -h.y + bevel, h.z - bevel), new Vector3(h.x, h.y - bevel, h.z - bevel), new Vector3(h.x, h.y - bevel, -h.z + bevel), Vector3.right);

            // Twelve edge chamfers.
            foreach (int sy in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                // Edges along X.
                var a = new Vector3(-h.x + bevel, sy * h.y, sz * (h.z - bevel));
                var c = new Vector3(-h.x + bevel, sy * (h.y - bevel), sz * h.z);
                var a2 = a; a2.x = h.x - bevel;
                var c2 = c; c2.x = h.x - bevel;
                AddQuadOutward(b, a, a2, c2, c, new Vector3(0f, sy, sz));
            }
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                // Edges along Y.
                var a = new Vector3(sx * h.x, -h.y + bevel, sz * (h.z - bevel));
                var c = new Vector3(sx * (h.x - bevel), -h.y + bevel, sz * h.z);
                var a2 = a; a2.y = h.y - bevel;
                var c2 = c; c2.y = h.y - bevel;
                AddQuadOutward(b, a, a2, c2, c, new Vector3(sx, 0f, sz));
            }
            foreach (int sx in new[] { -1, 1 })
            foreach (int sy in new[] { -1, 1 })
            {
                // Edges along Z.
                var a = new Vector3(sx * h.x, sy * (h.y - bevel), -h.z + bevel);
                var c = new Vector3(sx * (h.x - bevel), sy * h.y, -h.z + bevel);
                var a2 = a; a2.z = h.z - bevel;
                var c2 = c; c2.z = h.z - bevel;
                AddQuadOutward(b, a, a2, c2, c, new Vector3(sx, sy, 0f));
            }

            // Eight corner triangles.
            foreach (int sx in new[] { -1, 1 })
            foreach (int sy in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                var px = new Vector3(sx * h.x, sy * (h.y - bevel), sz * (h.z - bevel));
                var py = new Vector3(sx * (h.x - bevel), sy * h.y, sz * (h.z - bevel));
                var pz = new Vector3(sx * (h.x - bevel), sy * (h.y - bevel), sz * h.z);
                b.AddTriangleOutward(px, py, pz, new Vector3(sx, sy, sz));
            }

            return b.ToMesh(name);
        }

        /// <summary>
        /// One terraced bank: a block profile (a low step, a tall face with a grass lip, a bevelled top)
        /// extruded along a path. Submesh 0 is the tops (grass), submesh 1 the faces (earth).
        /// <paramref name="path"/> points carry the water surface height; <paramref name="side"/> is -1 for
        /// river left, +1 for river right, and <paramref name="innerOffset"/> is the lateral distance from the
        /// path to the inner foot of the bank.
        /// </summary>
        public static Mesh BankStrip(IReadOnlyList<Vector3> path, int side, float innerOffset, float thickness, float height, float bevel, string name)
        {
            // Profile in (lateral from the inner foot, height above the water surface). Walk inner foot to outer face.
            // Each segment is flagged top (grass) or face (earth).
            var profile = BankProfile(thickness, height, bevel);
            return Strip(path, side, innerOffset, profile, name);
        }

        /// <summary>
        /// The low block that edges the channel on the reference banks: a short earth face dropping into the
        /// shallows, a grass lip overhanging it, and a bevelled grass top a little above the meadow.
        /// </summary>
        public static Mesh ChannelLip(IReadOnlyList<Vector3> path, int side, float innerOffset, float width, string name)
        {
            const float bevel = 0.08f;
            var profile = new List<(Vector2 p, bool top)>
            {
                (new Vector2(0f, -0.45f), false),
                (new Vector2(0f, 0.18f), false),                 // short earth face
                (new Vector2(-0.16f, 0.26f), false),             // lip underside, overhanging the water
                (new Vector2(-0.16f, 0.46f), false),             // lip front
                (new Vector2(-0.16f + bevel, 0.56f), true),      // chamfer onto the top
                (new Vector2(width - bevel, 0.56f), true),       // grass top
                (new Vector2(width, 0.48f), false),              // outer chamfer
                (new Vector2(width, 0.3f), false),               // down into the meadow
            };
            return Strip(path, side, innerOffset, profile, name);
        }

        private static List<(Vector2 p, bool top)> BankProfile(float thickness, float height, float bevel)
        {
            return new List<(Vector2 p, bool top)>
            {
                (new Vector2(0f, -2.5f), false),
                (new Vector2(0f, 0.85f), false),                      // step riser
                (new Vector2(bevel, 1.0f), true),                      // step chamfer
                (new Vector2(1.4f - bevel, 1.0f), true),               // step tread
                (new Vector2(1.4f, 1.0f + bevel), false),              // chamfer into the face
                (new Vector2(1.4f, height - 0.55f), false),            // tall face
                (new Vector2(1.4f - 0.35f, height - 0.4f), false),     // grass lip underside, overhanging
                (new Vector2(1.4f - 0.35f, height - bevel), false),    // lip front
                (new Vector2(1.4f - 0.35f + bevel, height), true),     // lip chamfer onto the top
                (new Vector2(thickness - bevel, height), true),        // top
                (new Vector2(thickness, height - bevel), false),       // outer chamfer
                (new Vector2(thickness, -2.5f), false),                // outer face
            };
        }

        /// <summary>Extrudes a (lateral, height) profile along a path on one side of it. Submesh 0 = tops, 1 = faces.</summary>
        public static Mesh Strip(IReadOnlyList<Vector3> path, int side, float innerOffset, List<(Vector2 p, bool top)> profile, string name)
        {
            var tops = new MeshBuilder();
            var faces = new MeshBuilder();
            int n = path.Count;
            for (int i = 0; i < n - 1; i++)
            {
                var p0 = path[i];
                var p1 = path[i + 1];
                var t0 = Tangent(path, i);
                var t1 = Tangent(path, i + 1);
                var r0 = new Vector3(t0.z, 0f, -t0.x) * side;
                var r1 = new Vector3(t1.z, 0f, -t1.x) * side;
                for (int j = 0; j < profile.Count - 1; j++)
                {
                    var (a, top) = profile[j];
                    var (c, _) = profile[j + 1];
                    var v00 = p0 + r0 * (innerOffset + a.x) + Vector3.up * a.y;
                    var v01 = p0 + r0 * (innerOffset + c.x) + Vector3.up * c.y;
                    var v10 = p1 + r1 * (innerOffset + a.x) + Vector3.up * a.y;
                    var v11 = p1 + r1 * (innerOffset + c.x) + Vector3.up * c.y;
                    // The profile walks the bank clockwise from the inner foot, so its left-hand normal
                    // (-dy, dx) in (lateral, up) points out of the block: into the river on the inner
                    // face, up on the treads, away from the river on the outer face.
                    var pn = new Vector2(c.x - a.x, c.y - a.y);
                    var outward = r0 * -pn.y + Vector3.up * pn.x;
                    var builder = top || Mathf.Abs(pn.y) < 1e-4f ? tops : faces;
                    AddQuadOutward(builder, v00, v10, v11, v01, outward);
                }
            }
            return MeshBuilder.Combine(name, tops, faces);
        }

        private static Vector3 Tangent(IReadOnlyList<Vector3> path, int i)
        {
            var a = path[Mathf.Max(i - 1, 0)];
            var b = path[Mathf.Min(i + 1, path.Count - 1)];
            var d = b - a;
            d.y = 0f;
            return d.sqrMagnitude < 1e-6f ? Vector3.forward : d.normalized;
        }

        private static void AddQuadOutward(MeshBuilder b, Vector3 a, Vector3 c, Vector3 d, Vector3 e, Vector3 outward)
        {
            var n = Vector3.Cross(c - a, e - a);
            if (Vector3.Dot(n, outward) < 0f) b.AddQuad(a, e, d, c, (-n).normalized);
            else b.AddQuad(a, c, d, e, n.normalized);
        }

        /// <summary>Flat-shaded mesh accumulator: every face gets its own vertices.</summary>
        public sealed class MeshBuilder
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();

            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                int i = Vertices.Count;
                Vertices.Add(a); Vertices.Add(b); Vertices.Add(c); Vertices.Add(d);
                for (int k = 0; k < 4; k++) Normals.Add(n);
                AddPlanarUvs(n, a, b, c, d);
                Triangles.Add(i); Triangles.Add(i + 1); Triangles.Add(i + 2);
                Triangles.Add(i); Triangles.Add(i + 2); Triangles.Add(i + 3);
            }

            public void AddTriangleOutward(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                var n = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(n, outward) < 0f) { (b, c) = (c, b); n = -n; }
                int i = Vertices.Count;
                Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
                n.Normalize();
                for (int k = 0; k < 3; k++) Normals.Add(n);
                AddPlanarUvs(n, a, b, c);
                Triangles.Add(i); Triangles.Add(i + 1); Triangles.Add(i + 2);
            }

            private void AddPlanarUvs(Vector3 n, params Vector3[] points)
            {
                // World-planar projection along the dominant axis, in metres, so textures tile evenly.
                var an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
                foreach (var p in points)
                {
                    if (an.y >= an.x && an.y >= an.z) Uvs.Add(new Vector2(p.x, p.z));
                    else if (an.x >= an.z) Uvs.Add(new Vector2(p.z, p.y));
                    else Uvs.Add(new Vector2(p.x, p.y));
                }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = Vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(Vertices);
                m.SetNormals(Normals);
                m.SetUVs(0, Uvs);
                m.SetTriangles(Triangles, 0);
                m.RecalculateBounds();
                return m;
            }

            public static Mesh Combine(string name, params MeshBuilder[] parts)
            {
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                var v = new List<Vector3>();
                var n = new List<Vector3>();
                var uv = new List<Vector2>();
                foreach (var p in parts) { v.AddRange(p.Vertices); n.AddRange(p.Normals); uv.AddRange(p.Uvs); }
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.subMeshCount = parts.Length;
                int offset = 0;
                for (int s = 0; s < parts.Length; s++)
                {
                    var tris = new List<int>(parts[s].Triangles.Count);
                    foreach (int t in parts[s].Triangles) tris.Add(t + offset);
                    m.SetTriangles(tris, s);
                    offset += parts[s].Vertices.Count;
                }
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
