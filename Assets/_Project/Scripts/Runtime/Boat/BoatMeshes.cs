using System.Collections.Generic;
using Downstream.World;
using UnityEngine;

namespace Downstream.Boat
{
    /// <summary>
    /// Procedural meshes for the race boat: a lofted hull with a crowned deck and rub rail, a
    /// seated pilot in a vest and helmet, and a paddle. Sized to the sim's 2 x 0.6 x 4 m hull
    /// box so the visual matches what the sim collides. Reference: stylised whitewater raft and
    /// open canoe silhouettes (fine bow entry, full midships, flat transom).
    /// </summary>
    public static class BlockBoat
    {
        public const float Length = 4f;
        private const int Stations = 16;
        private const int RingPoints = 11;

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Half beam at fraction t along the hull (0 = transom, 1 = bow).</summary>
        private static float HalfWidth(float t)
        {
            float stern = Mathf.Lerp(0.72f, 1.0f, Smooth(0f, 0.38f, t));
            float bow = 1f - Mathf.Pow(Smooth(0.42f, 1f, t), 1.7f) * 0.95f;
            return stern * bow;
        }

        private static float KeelY(float t) => -0.34f + 0.26f * Smooth(0.55f, 1f, t) + 0.06f * (1f - Smooth(0f, 0.25f, t));

        private static float SheerY(float t) => 0.32f + 0.24f * Mathf.Pow(Smooth(0.55f, 1f, t), 1.4f) + 0.05f * (1f - Smooth(0f, 0.3f, t));

        private static float Z(float t) => -Length * 0.5f + Length * t;

        /// <summary>The hull skin: rings from transom to bow plus a flat transom, smooth-shaded.</summary>
        public static Mesh Hull(string name = "BoatHull")
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int s = 0; s <= Stations; s++)
            {
                float t = s / (float)Stations;
                float w = HalfWidth(t), k = KeelY(t), sh = SheerY(t);
                for (int r = 0; r < RingPoints; r++)
                {
                    float a = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, r / (float)(RingPoints - 1));
                    float c = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a)), 0.75f); // fuller bilge than a plain ellipse
                    verts.Add(new Vector3(w * Mathf.Sin(a) * (1f + 0.06f * (1f - c)), sh - (sh - k) * c, Z(t)));
                }
            }
            for (int s = 0; s < Stations; s++)
            for (int r = 0; r < RingPoints - 1; r++)
            {
                int a = s * RingPoints + r, b = a + 1, c = a + RingPoints, d = c + 1;
                // Outward faces: winding chosen so the normal points away from the keel.
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
            var m = new Mesh { name = name };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            // Transom: a flat cap, own vertices so the edge stays crisp.
            var cap = new BlockMeshes.MeshBuilder();
            {
                float w = HalfWidth(0f), k = KeelY(0f), sh = SheerY(0f);
                var centre = new Vector3(0f, (k + sh) * 0.5f, Z(0f));
                for (int r = 0; r < RingPoints - 1; r++)
                {
                    cap.AddTriangleOutward(centre, verts[r], verts[r + 1], Vector3.back);
                }
                cap.AddTriangleOutward(centre, verts[RingPoints - 1], new Vector3(0f, sh + 0.05f, Z(0f)), Vector3.back);
                cap.AddTriangleOutward(centre, new Vector3(0f, sh + 0.05f, Z(0f)), verts[0], Vector3.back);
            }
            var hullUv = new Vector2[verts.Count];
            for (int i = 0; i < verts.Count; i++) hullUv[i] = new Vector2(verts[i].z, verts[i].y);
            m.SetUVs(0, new List<Vector2>(hullUv));
            var merged = BlockMeshes.Merge(name, new List<(Mesh, Matrix4x4)> { (m, Matrix4x4.identity), (cap.ToMesh("Transom"), Matrix4x4.identity) });
            return merged;
        }

        /// <summary>Crowned deck, the rub rail around the sheer and the cockpit coaming: the trim colour.</summary>
        public static Mesh Deck(string name = "BoatDeck")
        {
            var b = new BlockMeshes.MeshBuilder();
            const int deckPoints = 7;
            Vector3[] prev = null;
            for (int s = 0; s <= Stations; s++)
            {
                float t = s / (float)Stations;
                float w = HalfWidth(t), sh = SheerY(t);
                var row = new Vector3[deckPoints];
                for (int r = 0; r < deckPoints; r++)
                {
                    float u = r / (float)(deckPoints - 1) * 2f - 1f;
                    row[r] = new Vector3(w * u, sh + 0.05f * (1f - u * u), Z(t));
                }
                if (prev != null)
                    for (int r = 0; r < deckPoints - 1; r++)
                        b.AddQuad(prev[r], row[r], row[r + 1], prev[r + 1], Vector3.up);
                prev = row;
            }
            // Rub rail: a rounded band just outside the sheer line, 12 cm tall.
            var rail = new List<Vector3>();
            var railTris = new List<int>();
            const int railRing = 5;
            for (int s = 0; s <= Stations; s++)
            {
                float t = s / (float)Stations;
                float w = HalfWidth(t) + 0.05f, sh = SheerY(t);
                for (int side = -1; side <= 1; side += 2)
                for (int r = 0; r < railRing; r++)
                {
                    float a = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, r / (float)(railRing - 1));
                    rail.Add(new Vector3(side * (w + 0.05f * Mathf.Cos(a)), sh + 0.06f * Mathf.Sin(a), Z(t)));
                }
            }
            int stride = railRing * 2;
            for (int s = 0; s < Stations; s++)
            for (int side = 0; side < 2; side++)
            for (int r = 0; r < railRing - 1; r++)
            {
                int a = s * stride + side * railRing + r, bb = a + 1, c = a + stride, d = c + 1;
                if (side == 1) { railTris.Add(a); railTris.Add(bb); railTris.Add(c); railTris.Add(bb); railTris.Add(d); railTris.Add(c); }
                else { railTris.Add(a); railTris.Add(c); railTris.Add(bb); railTris.Add(bb); railTris.Add(c); railTris.Add(d); }
            }
            var railMesh = new Mesh { name = "RubRail" };
            railMesh.SetVertices(rail);
            railMesh.SetTriangles(railTris, 0);
            railMesh.RecalculateNormals();
            var railUv = new Vector2[rail.Count];
            for (int i = 0; i < rail.Count; i++) railUv[i] = new Vector2(rail[i].z, rail[i].y);
            railMesh.SetUVs(0, new List<Vector2>(railUv));

            // Cockpit coaming: four rounded bars around the seat well.
            var coaming = new List<(Mesh, Matrix4x4)>();
            var bar = BlockMeshes.BevelledBox(new Vector3(0.1f, 0.12f, 1.5f), 0.04f, "CoamingSide");
            var barEnd = BlockMeshes.BevelledBox(new Vector3(1.2f, 0.12f, 0.1f), 0.04f, "CoamingEnd");
            float deckY = SheerY(0.38f) + 0.05f;
            coaming.Add((bar, Matrix4x4.TRS(new Vector3(-0.6f, deckY + 0.04f, -0.5f), Quaternion.identity, Vector3.one)));
            coaming.Add((bar, Matrix4x4.TRS(new Vector3(0.6f, deckY + 0.04f, -0.5f), Quaternion.identity, Vector3.one)));
            coaming.Add((barEnd, Matrix4x4.TRS(new Vector3(0f, deckY + 0.04f, 0.25f), Quaternion.identity, Vector3.one)));
            coaming.Add((barEnd, Matrix4x4.TRS(new Vector3(0f, deckY + 0.04f, -1.25f), Quaternion.identity, Vector3.one)));
            coaming.Add((b.ToMesh("Deck"), Matrix4x4.identity));
            coaming.Add((railMesh, Matrix4x4.identity));
            return BlockMeshes.Merge(name, coaming);
        }

        /// <summary>Seat height for the pilot at the cockpit.</summary>
        public static float CockpitDeckY => SheerY(0.38f) + 0.05f;

        /// <summary>Head and arms: skin tone.</summary>
        public static Mesh PilotSkin(string name = "PilotSkin")
        {
            float y0 = CockpitDeckY;
            var head = BlockMeshes.SmoothSphere(0.26f, 10, 14, "Head");
            var arm = BlockMeshes.Cylinder(0.065f, 0.52f, 10, "Arm");
            var parts = new List<(Mesh, Matrix4x4)>
            {
                (head, Matrix4x4.TRS(new Vector3(0f, y0 + 0.98f, -0.55f), Quaternion.identity, Vector3.one)),
            };
            // Arms angle forward and down from the shoulders to the paddle shaft.
            foreach (int side in new[] { -1, 1 })
            {
                var shoulder = new Vector3(side * 0.33f, y0 + 0.62f, -0.5f);
                var hand = new Vector3(side * 0.42f, y0 + 0.30f, -0.05f);
                var dir = hand - shoulder;
                var rot = Quaternion.FromToRotation(Vector3.up, dir.normalized);
                parts.Add((arm, Matrix4x4.TRS(shoulder, rot, new Vector3(1f, dir.magnitude / 0.52f, 1f))));
            }
            return BlockMeshes.Merge(name, parts);
        }

        /// <summary>The life vest and seat: trim colour.</summary>
        public static Mesh Vest(string name = "PilotVest")
        {
            float y0 = CockpitDeckY;
            var torso = BlockMeshes.BevelledBox(new Vector3(0.58f, 0.52f, 0.40f), 0.12f, "Torso");
            var seat = BlockMeshes.BevelledBox(new Vector3(0.9f, 0.16f, 0.6f), 0.05f, "Seat");
            return BlockMeshes.Merge(name, new List<(Mesh, Matrix4x4)>
            {
                (torso, Matrix4x4.TRS(new Vector3(0f, y0 + 0.42f, -0.55f), Quaternion.Euler(-6f, 0f, 0f), Vector3.one)),
                (seat, Matrix4x4.TRS(new Vector3(0f, y0 + 0.08f, -0.6f), Quaternion.identity, Vector3.one)),
            });
        }

        /// <summary>Helmet: a flattened dome in the hull colour.</summary>
        public static Mesh Helmet(string name = "PilotHelmet")
        {
            float y0 = CockpitDeckY;
            var dome = BlockMeshes.SmoothSphere(0.30f, 8, 14, "Dome");
            return BlockMeshes.Merge(name, new List<(Mesh, Matrix4x4)>
            {
                (dome, Matrix4x4.TRS(new Vector3(0f, y0 + 1.06f, -0.55f), Quaternion.identity, new Vector3(1f, 0.72f, 1f))),
            });
        }

        /// <summary>A double-bladed paddle held across the boat, one blade dipped.</summary>
        public static Mesh Paddle(string name = "Paddle")
        {
            float y0 = CockpitDeckY;
            var shaft = BlockMeshes.Cylinder(0.035f, 2.3f, 8, "Shaft");
            var blade = BlockMeshes.BevelledBox(new Vector3(0.05f, 0.46f, 0.26f), 0.02f, "Blade");
            var centre = new Vector3(0f, y0 + 0.30f, -0.05f);
            var tilt = Quaternion.Euler(0f, 0f, 90f + 18f); // shaft along x, dipped to the right
            var parts = new List<(Mesh, Matrix4x4)>
            {
                (shaft, Matrix4x4.TRS(centre, tilt, Vector3.one) * Matrix4x4.Translate(new Vector3(0f, -1.15f, 0f))),
            };
            foreach (float end in new[] { -1.15f, 1.15f })
            {
                var local = Matrix4x4.TRS(centre, tilt, Vector3.one) * Matrix4x4.TRS(new Vector3(0f, end, 0f), Quaternion.Euler(0f, 0f, 0f), Vector3.one);
                // Blades sit at the shaft ends, twisted 90 degrees from each other like a real kayak paddle.
                var twist = end > 0 ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
                parts.Add((blade, local * Matrix4x4.Rotate(twist)));
            }
            return BlockMeshes.Merge(name, parts);
        }
    }
}
