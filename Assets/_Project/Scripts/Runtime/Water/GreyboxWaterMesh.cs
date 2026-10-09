using System.Collections.Generic;
using Downstream.Core.Water;
using Downstream.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Downstream.Water
{
    /// <summary>
    /// The visible river for greybox tracks. <see cref="Build"/> uploads the gameplay water to the GPU
    /// (<see cref="RiverFieldGpu"/>), lays a flat grid over the field for the River Water shader to
    /// displace and shade, and builds a stone bed under it so refraction has something to show.
    /// <see cref="BuildBanks"/> adds the terraced bank blocks and the boulders from the block kit.
    /// Nothing here is read by physics: boats, AI and items sample <see cref="RiverWater"/> directly.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class GreyboxWaterMesh : MonoBehaviour
    {
        [Tooltip("Vertex spacing of the surface grid, metres. The shader shades per pixel; this only shapes the silhouette.")]
        [SerializeField] private float _spacing = 1f;
        [Tooltip("The river bed and meadow (Downstream/Greybox Ground): stones under the water blending into grass by vertex colour.")]
        [SerializeField] private Material _bedMaterial;

        private static readonly int RippleNormalId = Shader.PropertyToID("_RippleNormal");
        private static readonly int FoamTexId = Shader.PropertyToID("_FoamTex");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseSlopeId = Shader.PropertyToID("_RiverBaseSlope");

        private RiverFieldGpu _gpu;
        private MeshRenderer _bed;

        /// <summary>The GPU copy of the water, once <see cref="Build"/> has run.</summary>
        public RiverFieldGpu Gpu => _gpu;

        /// <summary>
        /// Lines both banks with terraced, bevelled block strips, invisible box colliders for the boats to
        /// slide along, and a bevelled block for each boulder. Placeholder for the modular block kit.
        /// </summary>
        public void BuildBanks(RiverDefinition river, Material grass, Material earth, Material rock)
        {
            var root = new GameObject("Greybox Banks").transform;
            root.SetParent(transform, false);
            var g = river.Greybox;
            float innerOffset = g.Width * 0.5f + g.FloodableBank;
            var points = river.SampleCentreline();
            var path = new List<Vector3>(points.Length);
            foreach (var p in points) path.Add(p.ToUnity());

            for (int s = -1; s <= 1; s += 2)
            {
                var bank = new GameObject(s < 0 ? "Bank L" : "Bank R", typeof(MeshFilter), typeof(MeshRenderer));
                bank.transform.SetParent(root, false);
                bank.GetComponent<MeshFilter>().sharedMesh = BlockMeshes.BankStrip(path, s, innerOffset, 4f, 2.4f, 0.15f, s < 0 ? "GreyboxBankL" : "GreyboxBankR");
                var r = bank.GetComponent<MeshRenderer>();
                r.sharedMaterials = new[] { grass, earth };
                r.shadowCastingMode = ShadowCastingMode.On;
            }

            // The low grass-lipped block that edges the channel, just where the bank ramp reaches the meadow.
            float lipOffset = g.Width * 0.5f + 1.0f;
            for (int s = -1; s <= 1; s += 2)
            {
                var lip = new GameObject(s < 0 ? "Channel Lip L" : "Channel Lip R", typeof(MeshFilter), typeof(MeshRenderer));
                lip.transform.SetParent(root, false);
                lip.GetComponent<MeshFilter>().sharedMesh = BlockMeshes.ChannelLip(path, s, lipOffset, 2.0f, s < 0 ? "GreyboxLipL" : "GreyboxLipR");
                var lr = lip.GetComponent<MeshRenderer>();
                lr.sharedMaterials = new[] { grass, earth };
                lr.shadowCastingMode = ShadowCastingMode.On;
            }

            // Collision: one box per centreline segment, at the inner face of the bank, never drawn.
            var colliders = new GameObject("Bank Colliders").transform;
            colliders.SetParent(root, false);
            for (int i = 0; i < points.Length - 1; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                var dir = b - a;
                dir.y = 0f;
                float len = dir.magnitude;
                if (len < 0.01f) continue;
                dir /= len;
                var side = new Vector3(dir.z, 0f, -dir.x);
                for (int s = -1; s <= 1; s += 2)
                {
                    var wall = new GameObject(s < 0 ? "Bank L" : "Bank R", typeof(BoxCollider));
                    wall.transform.SetParent(colliders, false);
                    wall.transform.position = (a + b) * 0.5f + side * ((innerOffset + 1.5f) * s) + Vector3.up * 1f;
                    wall.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                    wall.GetComponent<BoxCollider>().size = new Vector3(3f, 7f, len + 0.5f);
                }
            }

            var boulders = g.Boulders;
            if (boulders == null) return;
            for (int i = 0; i < boulders.Length; i++)
            {
                float d = boulders[i].Radius * 2f;
                var go = new GameObject($"Boulder {i}", typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
                go.transform.SetParent(root, false);
                go.transform.position = river.BoulderPosition(boulders[i]) + Vector3.up * 0.45f;
                go.transform.rotation = Quaternion.Euler(0f, 17f * (i + 1), 0f);
                var size = new Vector3(d, 1.6f, d * 0.85f);
                go.GetComponent<MeshFilter>().sharedMesh = BlockMeshes.BevelledBox(size, 0.35f, $"GreyboxBoulder{i}");
                go.GetComponent<BoxCollider>().size = size;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = rock;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        /// <summary>Uploads the water, builds the surface grid and the bed. Call again for a different river.</summary>
        public void Build(RiverWater water) => Build(water, null);

        /// <summary>
        /// As <see cref="Build(RiverWater)"/>; with a <paramref name="river"/> the visual bed follows the
        /// river's analytic centreline (a smooth ramp from the channel floor up to the meadow) instead of
        /// the 0.5 m texels, so the shoreline does not read as a sawtooth. Physics still uses the field.
        /// </summary>
        public void Build(RiverWater water, RiverDefinition river)
        {
            _gpu ??= new RiverFieldGpu();
            _gpu.Upload(water);
            RiverFieldGpu.SetRaceTime(0f);
            Shader.SetGlobalFloat(BaseSlopeId, river != null ? river.Greybox.Gradient : 0f);

            var field = water.Field;
            float minX = field.OriginX, minZ = field.OriginZ;
            float maxX = minX + field.TexelCountX * field.CellSize;
            float maxZ = minZ + field.TexelCountZ * field.CellSize;
            int nx = Mathf.CeilToInt((maxX - minX) / _spacing) + 1;
            int nz = Mathf.CeilToInt((maxZ - minZ) / _spacing) + 1;

            var surface = new Vector3[nx * nz];
            var bed = new Vector3[nx * nz];
            var bedUv = new Vector2[nx * nz];
            var has = new bool[nx * nz];
            var grass = new float[nx * nz];
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int i = z * nx + x;
                float wx = minX + x * _spacing, wz = minZ + z * _spacing;
                var s = field.SampleStatic(wx, wz);
                has[i] = s.HasData;
                surface[i] = new Vector3(wx, s.HasData ? s.SurfaceHeight : 0f, wz);
                float grassWeight = s.HasData && (s.Features & WaterFeature.Floodable) != 0 ? 1f : 0f;
                float bedY = s.HasData ? s.BedHeight : 0f;
                if (river != null && s.HasData) bedY = VisualBed(river, wx, wz, s.BedHeight, out grassWeight);
                bed[i] = new Vector3(wx, bedY, wz);
                bedUv[i] = new Vector2(wx, wz) * 1.0f; // 2 m tile: stones about 0.1-0.2 m
                grass[i] = grassWeight;
            }

            // Smooth the visual bed so the shelf step does not zig-zag along the 1 m grid.
            var smoothed = (Vector3[])bed.Clone();
            for (int pass = 0; pass < 2; pass++)
            {
                for (int z = 1; z < nz - 1; z++)
                for (int x = 1; x < nx - 1; x++)
                {
                    int i = z * nx + x;
                    if (!has[i]) continue;
                    float sum = 0f;
                    int n = 0;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int j = i + dz * nx + dx;
                        if (!has[j]) continue;
                        sum += bed[j].y;
                        n++;
                    }
                    smoothed[i].y = sum / n;
                }
                System.Array.Copy(smoothed, bed, bed.Length);
            }

            var surfaceTris = new List<int>();
            var bedTris = new List<int>();
            for (int z = 0; z < nz - 1; z++)
            for (int x = 0; x < nx - 1; x++)
            {
                int a = z * nx + x, b = a + 1, c = a + nx, d = c + 1;
                if (!(has[a] && has[b] && has[c] && has[d])) continue;
                surfaceTris.Add(a); surfaceTris.Add(c); surfaceTris.Add(b);
                surfaceTris.Add(b); surfaceTris.Add(c); surfaceTris.Add(d);
                bedTris.Add(a); bedTris.Add(c); bedTris.Add(b);
                bedTris.Add(b); bedTris.Add(c); bedTris.Add(d);
            }

            var mesh = new Mesh { name = "GreyboxWater", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(surface);
            mesh.SetTriangles(surfaceTris, 0);
            mesh.RecalculateBounds();
            // Floods and waves move the surface after the bounds are computed; keep it from being culled.
            var bounds = mesh.bounds;
            bounds.Expand(new Vector3(0f, 6f, 0f));
            mesh.bounds = bounds;
            GetComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetTexture(RippleNormalId, WaterTextures.Ripple);
            block.SetTexture(FoamTexId, WaterTextures.Foam);
            renderer.SetPropertyBlock(block);

            BuildBed(bed, bedUv, grass, bedTris);
        }

        /// <summary>
        /// Smooth stand-in for the greybox bed: the generator's parabolic channel, then a 2.5 m ramp up to
        /// the floodable meadow that crosses the water level at the channel edge. Dry rocks keep the field's height.
        /// </summary>
        private static float VisualBed(RiverDefinition river, float x, float z, float fieldBed, out float grass)
        {
            grass = 0f;
            var g = river.Greybox;
            float distance = Mathf.Clamp(z, 0f, g.Length);
            float cx = Core.Water.ProceduralRiver.CentreX(g, z);
            float dcx = Core.Water.ProceduralRiver.CentreSlope(g, z);
            float tZ = 1f / Mathf.Sqrt(1f + dcx * dcx);
            float lateral = Mathf.Abs((x - cx) * tZ);
            float halfWidth = g.Width * 0.5f;
            float surface = Core.Water.ProceduralRiver.SurfaceAt(g, distance);
            if (fieldBed > surface + 0.6f) return fieldBed; // a dry rock in the channel
            // Grass takes over as the ramp breaks the surface; stones stay under the water.
            float k = Mathf.Clamp01((lateral - halfWidth - Core.Water.ProceduralRiver.BankRampLength * 0.3f) / (Core.Water.ProceduralRiver.BankRampLength * 0.5f));
            grass = k * k * (3f - 2f * k);
            // Same shape as ProceduralRiver.Build, evaluated at the vertex instead of on the 0.5 m texels.
            if (lateral <= halfWidth)
            {
                float u = lateral / halfWidth;
                return surface - g.Depth * (1f - 0.6f * u * u);
            }
            float t = Mathf.Clamp01((lateral - halfWidth) / Core.Water.ProceduralRiver.BankRampLength);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(surface - g.Depth * 0.4f, surface + Core.Water.ProceduralRiver.BankShelfHeight, t);
        }

        private void BuildBed(Vector3[] vertices, Vector2[] uv, float[] grass, List<int> bedTris)
        {
            if (_bed == null)
            {
                var go = new GameObject("Greybox Bed", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                _bed = go.GetComponent<MeshRenderer>();
            }
            var mesh = new Mesh { name = "GreyboxBed", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            var colours = new Color[vertices.Length];
            for (int i = 0; i < colours.Length; i++) colours[i] = new Color(grass[i], 0.5f, 0f, 1f);
            mesh.SetColors(colours);
            mesh.SetTriangles(bedTris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _bed.GetComponent<MeshFilter>().sharedMesh = mesh;
            _bed.sharedMaterial = _bedMaterial;
            _bed.shadowCastingMode = ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetTexture(BaseMapId, WaterTextures.Pebbles);
            block.SetTexture("_DetailMap", WaterTextures.SoftNoise);
            _bed.SetPropertyBlock(block);
        }

        /// <summary>Race time for the shader's floods, tide and waves: the interpolated time the views are drawn at.</summary>
        public void SetRaceTime(float raceTime) => RiverFieldGpu.SetRaceTime(Mathf.Max(0f, raceTime));

        private void OnDestroy()
        {
            _gpu?.Release();
            _gpu = null;
        }
    }
}
