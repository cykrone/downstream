using Downstream.Core.Water;
using UnityEngine;
using UnityEngine.Rendering;

namespace Downstream.Water
{
    /// <summary>
    /// Builds a flat-shaded mesh of the gameplay water surface so greybox tracks have something to
    /// look at before the real water shader exists. Vertex colours show flow speed (blue to white)
    /// and mark current lanes, so designers can read the field in play mode.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class GreyboxWaterMesh : MonoBehaviour
    {
        [SerializeField] private float _spacing = 2f;
        [SerializeField] private float _maxFlowForColour = 8f;

        /// <summary>
        /// Lines both banks with box colliders (and plain cubes to see them) so boats have something
        /// to slide along. Placeholder for the modular block kit.
        /// </summary>
        public void BuildBanks(RiverDefinition river, Material material)
        {
            var root = new GameObject("Greybox Banks").transform;
            root.SetParent(transform, false);
            var g = river.Greybox;
            float offset = g.Width * 0.5f + g.FloodableBank + 1.5f;
            var points = river.SampleCentreline();
            for (int i = 0; i < points.Length - 1; i++)
            {
                var a = points[i].ToUnity();
                var b = points[i + 1].ToUnity();
                var dir = b - a;
                dir.y = 0f;
                float len = dir.magnitude;
                if (len < 0.01f) continue;
                dir /= len;
                var side = new Vector3(dir.z, 0f, -dir.x);
                for (int s = -1; s <= 1; s += 2)
                {
                    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.name = s < 0 ? "Bank L" : "Bank R";
                    wall.transform.SetParent(root, false);
                    wall.transform.position = (a + b) * 0.5f + side * (offset * s) + Vector3.up * 1f;
                    wall.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                    wall.transform.localScale = new Vector3(3f, 6f, len + 0.5f);
                    if (material != null) wall.GetComponent<MeshRenderer>().sharedMaterial = material;
                }
            }
        }

        public void Build(RiverWater water)
        {
            var field = water.Field;
            float minX = field.OriginX, minZ = field.OriginZ;
            float maxX = minX + field.TexelCountX * field.CellSize;
            float maxZ = minZ + field.TexelCountZ * field.CellSize;
            int nx = Mathf.CeilToInt((maxX - minX) / _spacing) + 1;
            int nz = Mathf.CeilToInt((maxZ - minZ) / _spacing) + 1;

            var vertices = new Vector3[nx * nz];
            var colours = new Color[nx * nz];
            var wet = new bool[nx * nz];
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int i = z * nx + x;
                float wx = minX + x * _spacing, wz = minZ + z * _spacing;
                var s = water.Sample(wx, wz, 0f);
                wet[i] = s.IsWet;
                vertices[i] = new Vector3(wx, s.IsWet ? s.SurfaceHeight : 0f, wz);
                float speed = Mathf.Clamp01(s.Flow.Magnitude / _maxFlowForColour);
                var c = Color.Lerp(new Color(0.12f, 0.35f, 0.45f), new Color(0.75f, 0.92f, 0.95f), speed);
                if ((s.Features & WaterFeature.CurrentLane) != 0) c = Color.Lerp(c, new Color(0.95f, 0.98f, 1f), 0.35f);
                colours[i] = c;
            }

            var triangles = new System.Collections.Generic.List<int>();
            for (int z = 0; z < nz - 1; z++)
            for (int x = 0; x < nx - 1; x++)
            {
                int a = z * nx + x, b = a + 1, c = a + nx, d = c + 1;
                if (!(wet[a] && wet[b] && wet[c] && wet[d])) continue;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }

            var mesh = new Mesh { name = "GreyboxWater", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colours);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
    }
}
