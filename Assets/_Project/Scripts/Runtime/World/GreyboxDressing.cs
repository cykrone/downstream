using System.Collections.Generic;
using Downstream.Core.Water;
using Downstream.Water;
using UnityEngine;
using UnityEngine.Rendering;

namespace Downstream.World
{
    /// <summary>
    /// Dresses the greybox river at runtime so the world reads as the design's toy diorama before
    /// any modelled kit exists: rolling grass hills behind the banks, puffy round trees and pines
    /// built from a few shared mesh variants, bushes, rounded rocks, reeds at the waterline, flower
    /// clusters in the pop colours, lantern posts along the water, cairns on the outside of bends,
    /// a bridge, docks with crates, a shrine on a hill, and a camp or bunting every 100 m of bank.
    /// Everything is built from the block kit's primitives with a fixed seed, nothing is saved in
    /// the scene, and props share materials so the SRP batcher keeps the draw count cheap.
    /// </summary>
    public sealed class GreyboxDressing : MonoBehaviour
    {
        [SerializeField] private RiverDefinition _river;
        [SerializeField] private int _seed = 7;
        [Header("Materials")]
        [SerializeField] private Material _hills;
        [SerializeField] private Material[] _canopies;
        [SerializeField] private Material[] _pines;
        [SerializeField] private Material _trunk;
        [SerializeField] private Material _rock;
        [SerializeField] private Material _reed;
        [SerializeField] private Material[] _flowers;
        [SerializeField] private Material _tent;
        [SerializeField] private Material _post;
        [SerializeField] private Material _lantern;
        [SerializeField] private Material _plank;
        [SerializeField] private Material _shrine;
        [Header("Density")]
        [SerializeField] private float _meadowTreesPer100m = 5f;
        [SerializeField] private float _hillTreesPer100m = 20f;
        [SerializeField] private float _bushesPer100m = 8f;
        [SerializeField] private float _hillDepth = 240f;

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private Mesh[] _canopyVariants, _pineVariants, _rockVariants;
        private Mesh _sphere, _cone, _trunkMesh, _reedMesh, _flower, _tentMesh, _postMesh, _lanternMesh, _flag, _plankMesh, _crate, _barrel, _roof, _box;
        private System.Random _rng;
        private ProceduralRiverSettings _g;
        private bool _built;

        private const float BankTop = 1.9f; // height of the bank block's top above the water

        private void Start()
        {
            if (!_built) Build();
        }

        public void Build()
        {
            if (_river == null) return;
            _built = true;
            _rng = new System.Random(_seed);
            _g = _river.Greybox;
            BuildMeshes();

            var hills = new GameObject("Hills").transform;
            hills.SetParent(transform, false);
            for (int side = -1; side <= 1; side += 2) BuildHills(hills, side);

            var trees = Group("Trees");
            var bushes = Group("Bushes");
            var rocks = Group("Rocks");
            var plants = Group("Plants");
            var story = Group("Story");
            var landmarks = Group("Landmarks");

            for (int side = -1; side <= 1; side += 2)
            {
                ScatterTrees(trees, side, 3.2f, 10.5f, _meadowTreesPer100m, 0.7f, false);
                ScatterTrees(trees, side, HillsIn, HillsIn + _hillDepth * 0.35f, _hillTreesPer100m, 0.45f, true);
                ScatterTrees(trees, side, HillsIn + _hillDepth * 0.35f, HillsIn + _hillDepth, _hillTreesPer100m * 0.35f, 0.4f, true);
                ScatterBushes(bushes, side, 3.2f, 10.5f, _bushesPer100m, false);
                ScatterBushes(bushes, side, HillsIn, HillsIn + _hillDepth * 0.5f, _bushesPer100m, true);
                ScatterReeds(plants, side);
                ScatterFlowers(plants, side, 3.2f, 10.5f);
                ScatterRocks(rocks, side, 2.2f, 10.5f, 1.4f, false);
                ScatterRocks(rocks, side, HillsIn, HillsIn + _hillDepth, 2.2f, true);
                LanternPosts(story, side);
            }
            BuildStoryClusters(story);
            Cairns(landmarks);
            Bridge(landmarks, 560f);
            Dock(landmarks, 330f, -1);
            Dock(landmarks, 1180f, 1);
            Shrine(landmarks, 900f, 1);
            SetUpLighting();
        }

        private void OnDestroy()
        {
            foreach (var m in _meshes) if (m != null) Destroy(m);
            _meshes.Clear();
        }

        private Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        // ---- river geometry helpers -------------------------------------------------------------

        private float Surface(float z) => ProceduralRiver.SurfaceAt(_g, Mathf.Clamp(z, 0f, _g.Length));
        private float HalfWidth(float z) => ProceduralRiver.WidthAt(_g, z) * 0.5f;
        /// <summary>Lateral distance from the centreline to the inner foot of the hills (past the bank block's top).</summary>
        private float HillsIn => _g.Width * (1f + _g.WidthVariation) * 0.5f + _g.FloodableBank + 4.5f + 2f;
        private float BankIn => _g.Width * (1f + _g.WidthVariation) * 0.5f + _g.FloodableBank;

        private Vector3 World(int side, float lateral, float z, float y)
        {
            float cx = ProceduralRiver.CentreX(_g, z);
            return new Vector3(cx + side * lateral, y, z);
        }

        private Vector3 Tangent(float z)
        {
            float d = ProceduralRiver.CentreSlope(_g, z);
            return new Vector3(d, 0f, 1f).normalized;
        }

        private float HillRise(float lateral, float z)
        {
            float d = Mathf.Max(0f, lateral - HillsIn);
            float ramp = Mathf.SmoothStep(0f, 1f, d / 40f);
            float n = Fbm(lateral * 0.012f + 3.1f, z * 0.012f, 3) * 2f - 1f;
            float big = Fbm(lateral * 0.004f, z * 0.004f + 9.7f, 2) * 2f - 1f;
            float ridge = Mathf.Pow(Fbm(lateral * 0.02f + 21f, z * 0.006f + 4f, 2), 3f) * 12f;
            return BankTop + ramp * (6f + 9f * n + 14f * big + ridge + d * 0.045f);
        }

        /// <summary>Ground height for a prop: the meadow shelf, the bank block's top, or the hills.</summary>
        private float GroundY(int side, float lateral, float z)
        {
            float s = Surface(z);
            if (lateral < BankIn) return s + ProceduralRiver.BankShelfHeight;
            if (lateral < HillsIn) return s + BankTop;
            return s + HillRise(lateral, z);
        }

        /// <summary>Meadow placement: lateral is measured from the channel edge so it follows the breathing width.</summary>
        private Vector3 Meadow(int side, float fromEdge, float z, float lift = 0f)
        {
            float lateral = HalfWidth(z) + fromEdge;
            return World(side, lateral, z, GroundY(side, lateral, z) + lift);
        }

        private static float Fbm(float x, float y, int octaves)
        {
            float v = 0f, a = 0.5f, f = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                v += Mathf.PerlinNoise(x * f, y * f) * a;
                norm += a;
                a *= 0.5f;
                f *= 2.1f;
            }
            return v / norm;
        }

        private float Rand(float min, float max) => min + (float)_rng.NextDouble() * (max - min);
        private T Pick<T>(T[] items) => items == null || items.Length == 0 ? default : items[_rng.Next(items.Length)];

        // ---- meshes -----------------------------------------------------------------------------

        private Mesh Keep(Mesh m) { _meshes.Add(m); return m; }

        private void BuildMeshes()
        {
            _sphere = Keep(BlockMeshes.SmoothSphere(1f, 10, 14, "Sphere"));
            _cone = Keep(BlockMeshes.SmoothCone(1f, 1f, 14, "Cone"));
            _trunkMesh = Keep(BlockMeshes.Cylinder(0.22f, 1f, 8, "Trunk"));
            _reedMesh = Keep(BlockMeshes.Cone(0.07f, 1f, 5, "Reed"));
            _flower = Keep(BlockMeshes.SmoothSphere(0.16f, 5, 8, "Flower"));
            _tentMesh = Keep(BlockMeshes.BevelledBox(new Vector3(3.2f, 3.2f, 3.6f), 0.15f, "Tent"));
            _postMesh = Keep(BlockMeshes.Cylinder(0.09f, 1f, 6, "Post"));
            _lanternMesh = Keep(BlockMeshes.BevelledBox(new Vector3(0.35f, 0.45f, 0.35f), 0.06f, "Lantern"));
            _flag = Keep(BlockMeshes.BevelledBox(new Vector3(0.45f, 0.35f, 0.04f), 0.03f, "Flag"));
            _plankMesh = Keep(BlockMeshes.BevelledBox(new Vector3(1f, 0.18f, 1f), 0.03f, "Plank"));
            _crate = Keep(BlockMeshes.BevelledBox(new Vector3(0.9f, 0.9f, 0.9f), 0.08f, "Crate"));
            _barrel = Keep(BlockMeshes.Cylinder(0.42f, 1f, 10, "Barrel"));
            _roof = Keep(BlockMeshes.Cone(1f, 1f, 4, "Roof"));
            _box = Keep(BlockMeshes.BevelledBox(Vector3.one, 0.08f, "Box"));

            // Puffy canopies: a main sphere and a few lobes, merged so a tree is two draws (trunk, canopy).
            _canopyVariants = new Mesh[6];
            for (int v = 0; v < _canopyVariants.Length; v++)
            {
                var parts = new List<(Mesh, Matrix4x4)> { (_sphere, Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, 0.85f, 1f))) };
                int lobes = 2 + _rng.Next(3);
                for (int l = 0; l < lobes; l++)
                {
                    float ang = Rand(0f, Mathf.PI * 2f);
                    float r = Rand(0.5f, 0.72f);
                    var pos = new Vector3(Mathf.Cos(ang) * Rand(0.45f, 0.7f), Rand(-0.2f, 0.45f), Mathf.Sin(ang) * Rand(0.45f, 0.7f));
                    parts.Add((_sphere, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(r, r * 0.85f, r))));
                }
                _canopyVariants[v] = Keep(BlockMeshes.Merge("Canopy" + v, parts));
            }

            // Pines: three or four drooping tiers merged into one mesh.
            _pineVariants = new Mesh[3];
            for (int v = 0; v < _pineVariants.Length; v++)
            {
                var parts = new List<(Mesh, Matrix4x4)>();
                float h = 0f, radius = 1f;
                int tiers = 3 + v % 2;
                for (int t = 0; t < tiers; t++)
                {
                    float coneH = 1.1f * (1f - t * 0.1f);
                    parts.Add((_cone, Matrix4x4.TRS(new Vector3(0f, h, 0f), Quaternion.Euler(0f, Rand(0f, 360f), 0f), new Vector3(radius, coneH, radius))));
                    h += coneH * 0.5f;
                    radius *= 0.74f;
                }
                _pineVariants[v] = Keep(BlockMeshes.Merge("Pine" + v, parts));
            }

            // Rounded rocks: squashed spheres with a smaller lump.
            _rockVariants = new Mesh[4];
            for (int v = 0; v < _rockVariants.Length; v++)
            {
                var parts = new List<(Mesh, Matrix4x4)>
                {
                    (_sphere, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(Rand(-15f, 15f), Rand(0f, 360f), Rand(-15f, 15f)), new Vector3(1f, Rand(0.55f, 0.8f), Rand(0.7f, 1f)))),
                    (_sphere, Matrix4x4.TRS(new Vector3(Rand(-0.4f, 0.4f), Rand(-0.1f, 0.2f), Rand(-0.4f, 0.4f)), Quaternion.Euler(0f, Rand(0f, 360f), 0f), new Vector3(Rand(0.5f, 0.8f), Rand(0.4f, 0.6f), Rand(0.5f, 0.8f)))),
                };
                _rockVariants[v] = Keep(BlockMeshes.Merge("Rock" + v, parts));
            }
        }

        private GameObject Prop(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale, bool shadows = true)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        // ---- hills ------------------------------------------------------------------------------

        private void BuildHills(Transform parent, int side)
        {
            float hillsIn = HillsIn - 2f;
            const float spacing = 6f;
            float z0 = -60f, z1 = _g.Length + 80f;
            int nl = Mathf.CeilToInt(_hillDepth / spacing) + 1;
            int nz = Mathf.CeilToInt((z1 - z0) / spacing) + 1;
            var verts = new Vector3[nl * nz];
            var cols = new Color[nl * nz];
            var uvs = new Vector2[nl * nz];
            for (int iz = 0; iz < nz; iz++)
            for (int il = 0; il < nl; il++)
            {
                float z = z0 + iz * spacing;
                float lateral = hillsIn + il * spacing;
                float y = Surface(z) + HillRise(lateral, z);
                int i = iz * nl + il;
                verts[i] = World(side, lateral, z, y);
                float tint = Fbm(lateral * 0.05f + 11f, z * 0.05f, 2);
                cols[i] = new Color(1f, tint, 0f, 1f);
                uvs[i] = new Vector2(verts[i].x, verts[i].z);
            }
            var tris = new List<int>(nl * nz * 6);
            for (int iz = 0; iz < nz - 1; iz++)
            for (int il = 0; il < nl - 1; il++)
            {
                int a = iz * nl + il, b = a + 1, c = a + nl, d = c + 1;
                if (side > 0) { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d); }
                else { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c); }
            }
            var mesh = new Mesh { name = side < 0 ? "HillsL" : "HillsR", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Keep(mesh);
            var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _hills;
            r.shadowCastingMode = ShadowCastingMode.On;
            var block = new MaterialPropertyBlock();
            block.SetTexture("_DetailMap", WaterTextures.SoftNoise);
            r.SetPropertyBlock(block);
        }

        // ---- scatter ----------------------------------------------------------------------------

        /// <param name="onHills">When true, lIn/lOut are lateral distances from the centreline; otherwise from the channel edge.</param>
        private Vector3 Place(int side, float lIn, float lOut, float z, bool onHills)
        {
            if (onHills)
            {
                float lateral = Rand(lIn, lOut);
                return World(side, lateral, z, GroundY(side, lateral, z));
            }
            return Meadow(side, Rand(lIn, lOut), z);
        }

        private void ScatterTrees(Transform parent, int side, float lIn, float lOut, float per100m, float pineShare, bool onHills)
        {
            int count = Mathf.RoundToInt(per100m * _g.Length / 100f);
            for (int i = 0; i < count; i++)
            {
                float z = Rand(-20f, _g.Length + 40f);
                var pos = Place(side, lIn, lOut, z, onHills);
                float scale = Rand(0.8f, 1.4f);
                if (_rng.NextDouble() < 0.06) scale *= 1.6f; // the odd landmark tree
                var yaw = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
                if (_rng.NextDouble() < pineShare) Pine(parent, pos, yaw, scale);
                else RoundTree(parent, pos, yaw, scale);
            }
        }

        private void RoundTree(Transform parent, Vector3 pos, Quaternion yaw, float scale)
        {
            float trunkH = 2.4f * scale;
            float r = 2.7f * scale;
            Prop(parent, "Trunk", _trunkMesh, _trunk, pos, yaw, new Vector3(scale * 1.1f, trunkH + r * 0.5f, scale * 1.1f));
            Prop(parent, "Canopy", Pick(_canopyVariants), Pick(_canopies), pos + Vector3.up * (trunkH + r * 0.7f), yaw, new Vector3(r, r, r));
        }

        private void Pine(Transform parent, Vector3 pos, Quaternion yaw, float scale)
        {
            float trunkH = 1.6f * scale;
            Prop(parent, "Trunk", _trunkMesh, _trunk, pos, yaw, new Vector3(scale * 0.9f, trunkH + 1f, scale * 0.9f));
            float radius = 2.5f * scale;
            Prop(parent, "Pine", Pick(_pineVariants), Pick(_pines), pos + Vector3.up * trunkH, yaw, new Vector3(radius, 3.0f * scale, radius));
        }

        private void ScatterBushes(Transform parent, int side, float lIn, float lOut, float per100m, bool onHills)
        {
            int count = Mathf.RoundToInt(per100m * _g.Length / 100f);
            for (int i = 0; i < count; i++)
            {
                float z = Rand(-10f, _g.Length + 20f);
                var pos = Place(side, lIn, lOut, z, onHills);
                float r = Rand(0.9f, 1.8f);
                Prop(parent, "Bush", Pick(_canopyVariants), Pick(_canopies), pos + Vector3.up * (r * 0.45f), Quaternion.Euler(0f, Rand(0f, 360f), 0f), new Vector3(r, r * 0.7f, r));
            }
        }

        private void ScatterRocks(Transform parent, int side, float lIn, float lOut, float per100m, bool onHills)
        {
            int count = Mathf.RoundToInt(per100m * _g.Length / 100f);
            for (int i = 0; i < count; i++)
            {
                float z = Rand(0f, _g.Length);
                var pos = Place(side, lIn, lOut, z, onHills);
                float s = Rand(0.5f, 1.6f);
                Prop(parent, "Rock", Pick(_rockVariants), _rock, pos + Vector3.up * (s * 0.35f), Quaternion.Euler(0f, Rand(0f, 360f), 0f), new Vector3(s, s, s));
            }
        }

        private void ScatterReeds(Transform parent, int side)
        {
            for (float z = 10f; z < _g.Length; z += Rand(9f, 24f))
            {
                var centre = World(side, HalfWidth(z) + Rand(1.3f, 2.6f), z, Surface(z) - 0.1f);
                int n = _rng.Next(5, 11);
                for (int i = 0; i < n; i++)
                {
                    var pos = centre + new Vector3(Rand(-0.6f, 0.6f), 0f, Rand(-0.6f, 0.6f));
                    float h = Rand(1.1f, 1.9f);
                    var rot = Quaternion.Euler(Rand(-10f, 10f), Rand(0f, 360f), Rand(-10f, 10f));
                    Prop(parent, "Reed", _reedMesh, _reed, pos, rot, new Vector3(1f, h, 1f), false);
                }
            }
        }

        private void ScatterFlowers(Transform parent, int side, float lIn, float lOut)
        {
            for (float z = 25f; z < _g.Length; z += Rand(28f, 60f))
            {
                var centre = Meadow(side, Rand(lIn, lOut), z);
                var mat = Pick(_flowers);
                int n = _rng.Next(6, 14);
                for (int i = 0; i < n; i++)
                {
                    var pos = centre + new Vector3(Rand(-1.6f, 1.6f), 0.28f, Rand(-1.6f, 1.6f));
                    Prop(parent, "Flower", _flower, mat, pos, Quaternion.identity, Vector3.one * Rand(0.8f, 1.3f), false);
                    Prop(parent, "Stem", _reedMesh, _reed, pos - Vector3.up * 0.28f, Quaternion.identity, new Vector3(0.5f, 0.3f, 0.5f), false);
                }
            }
        }

        private void LanternPosts(Transform parent, int side)
        {
            for (float z = 45f + (side > 0 ? 35f : 0f); z < _g.Length - 20f; z += 70f)
            {
                var pos = Meadow(side, 2.4f, z);
                Prop(parent, "Post", _postMesh, _post, pos, Quaternion.identity, new Vector3(1f, 2.4f, 1f));
                Prop(parent, "Lantern", _lanternMesh, _lantern, pos + Vector3.up * 2.3f, Quaternion.identity, Vector3.one, false);
            }
        }

        // ---- story clusters and landmarks -----------------------------------------------------------

        private void BuildStoryClusters(Transform parent)
        {
            int side = 1;
            for (float z = 60f; z < _g.Length - 40f; z += 100f)
            {
                side = -side;
                var centre = Meadow(side, Rand(4f, 8f), z);
                if (((int)(z / 100f) & 1) == 0) Camp(parent, centre);
                else Bunting(parent, centre);
            }
        }

        private void Camp(Transform parent, Vector3 centre)
        {
            var yaw = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            Prop(parent, "Tent", _tentMesh, _tent, centre + Vector3.up * 1.2f, yaw * Quaternion.Euler(0f, 0f, 45f), new Vector3(0.75f, 0.75f, 1f));
            var postPos = centre + yaw * new Vector3(3f, 0f, 0.5f);
            Prop(parent, "Post", _postMesh, _post, postPos, Quaternion.identity, new Vector3(1f, 2.2f, 1f));
            Prop(parent, "Lantern", _lanternMesh, _lantern, postPos + Vector3.up * 2.1f, Quaternion.identity, Vector3.one, false);
            for (int i = 0; i < 3; i++)
            {
                var pos = centre + yaw * new Vector3(Rand(-3f, 3f), 0.25f, Rand(2f, 4f));
                Prop(parent, "Rock", Pick(_rockVariants), _rock, pos, Quaternion.Euler(0f, Rand(0f, 360f), 0f), Vector3.one * Rand(0.5f, 0.9f));
            }
            Prop(parent, "Crate", _crate, _plank, centre + yaw * new Vector3(-2.6f, 0.45f, 1.5f), yaw, Vector3.one);
        }

        private void Bunting(Transform parent, Vector3 centre)
        {
            var a = centre + new Vector3(0f, 0f, -5f);
            var b = centre + new Vector3(0f, 0f, 5f);
            Prop(parent, "Post", _postMesh, _post, a, Quaternion.identity, new Vector3(1f, 2.8f, 1f));
            Prop(parent, "Post", _postMesh, _post, b, Quaternion.identity, new Vector3(1f, 2.8f, 1f));
            const int flags = 12;
            for (int i = 0; i <= flags; i++)
            {
                float t = i / (float)flags;
                float sag = 0.5f * Mathf.Sin(t * Mathf.PI);
                var pos = Vector3.Lerp(a, b, t) + Vector3.up * (2.7f - sag);
                Prop(parent, "Flag", _flag, Pick(_flowers), pos, Quaternion.Euler(0f, 90f, 0f), Vector3.one, false);
            }
        }

        /// <summary>Stacked stones on the outside of each bend: the design's authored guidance for forks and lines.</summary>
        private void Cairns(Transform parent)
        {
            for (float z = 120f; z < _g.Length - 60f; z += 150f)
            {
                float curve = ProceduralRiver.CentreCurvature(_g, z, out _);
                int outside = curve > 0f ? -1 : 1;
                var basePos = Meadow(outside, 1.6f, z);
                float h = 0f;
                for (int i = 0; i < 4; i++)
                {
                    float s = 0.9f - i * 0.17f;
                    Prop(parent, "Cairn", Pick(_rockVariants), _rock, basePos + Vector3.up * (h + s * 0.3f), Quaternion.Euler(0f, Rand(0f, 360f), 0f), new Vector3(s, s * 0.8f, s));
                    h += s * 0.5f;
                }
            }
        }

        /// <summary>A plank bridge over the river, high enough to race under.</summary>
        private void Bridge(Transform parent, float z)
        {
            float half = HalfWidth(z) + _g.FloodableBank * 0.4f;
            var tangent = Tangent(z);
            var right = new Vector3(tangent.z, 0f, -tangent.x);
            var centre = new Vector3(ProceduralRiver.CentreX(_g, z), Surface(z) + 6.5f, z);
            var yaw = Quaternion.LookRotation(right, Vector3.up);
            int planks = Mathf.CeilToInt(half * 2f / 0.9f);
            for (int i = 0; i < planks; i++)
            {
                float t = (i + 0.5f) / planks;
                var pos = centre + right * Mathf.Lerp(-half, half, t);
                Prop(parent, "Plank", _plankMesh, _plank, pos, yaw, new Vector3(4f, 1f, 0.82f));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Prop(parent, "Rail", _plankMesh, _post, centre + tangent * (s * 1.9f) + Vector3.up * 1.1f, yaw, new Vector3(0.12f, 0.7f, half * 2f + 0.3f));
                for (float t = -half; t <= half; t += 4f)
                    Prop(parent, "Post", _postMesh, _post, centre + right * t + tangent * (s * 1.9f), Quaternion.identity, new Vector3(1.2f, 1.2f, 1.2f));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                var foot = centre + right * (s * half * 0.55f);
                const float depth = 9f;
                Prop(parent, "Pier", _postMesh, _post, foot - Vector3.up * depth, Quaternion.identity, new Vector3(4.5f, depth, 4.5f));
            }
        }

        /// <summary>A plank dock reaching from the meadow over the water, with crates and a barrel.</summary>
        private void Dock(Transform parent, float z, int side)
        {
            var tangent = Tangent(z);
            var outward = new Vector3(tangent.z, 0f, -tangent.x) * side;
            var edge = World(side, HalfWidth(z), z, Surface(z) + 0.55f);
            var yaw = Quaternion.LookRotation(-outward, Vector3.up);
            for (int i = 0; i < 6; i++)
            {
                var pos = edge + outward * (2.5f - i * 1.0f);
                Prop(parent, "Plank", _plankMesh, _plank, pos, yaw, new Vector3(3.2f, 1f, 0.92f));
            }
            for (int i = 0; i < 3; i++)
                Prop(parent, "Pile", _postMesh, _post, edge + outward * (2.5f - i * 2.4f) + tangent * 1.5f - Vector3.up * 2f, Quaternion.identity, new Vector3(1.6f, 2.8f, 1.6f));
            Prop(parent, "Crate", _crate, _plank, edge + outward * 2.2f - tangent * 0.9f + Vector3.up * 0.55f, yaw, Vector3.one);
            Prop(parent, "Crate", _crate, _plank, edge + outward * 2.2f - tangent * 0.9f + Vector3.up * 1.45f, yaw * Quaternion.Euler(0f, 20f, 0f), Vector3.one * 0.8f);
            Prop(parent, "Barrel", _barrel, _tent, edge + outward * 1.0f - tangent * 1.0f + Vector3.up * 0.12f, Quaternion.identity, new Vector3(1f, 1.1f, 1f));
        }

        /// <summary>A small roofed shrine on the hill, a landmark you can see from the water.</summary>
        private void Shrine(Transform parent, float z, int side)
        {
            float lateral = HillsIn + 28f;
            var pos = World(side, lateral, z, GroundY(side, lateral, z));
            var yaw = Quaternion.Euler(0f, side > 0 ? -90f : 90f, 0f);
            Prop(parent, "Shrine", _box, _shrine, pos + Vector3.up * 1.6f, yaw, new Vector3(3.2f, 3.2f, 3.2f));
            Prop(parent, "Roof", _roof, _tent, pos + Vector3.up * 3.1f, yaw * Quaternion.Euler(0f, 45f, 0f), new Vector3(2.9f, 2.2f, 2.9f));
            Prop(parent, "Step", _box, _rock, pos + Vector3.up * 0.2f, yaw, new Vector3(4.6f, 0.4f, 4.6f));
            for (int s = -1; s <= 1; s += 2)
            {
                var postPos = pos + yaw * new Vector3(s * 2.6f, 0f, 3.5f);
                Prop(parent, "Post", _postMesh, _post, postPos, Quaternion.identity, new Vector3(1.4f, 3.2f, 1.4f));
                Prop(parent, "Lantern", _lanternMesh, _lantern, postPos + Vector3.up * 3f, Quaternion.identity, Vector3.one * 1.3f, false);
            }
        }

        // ---- lighting -----------------------------------------------------------------------------

        private void SetUpLighting()
        {
            DynamicGI.UpdateEnvironment();
            var go = new GameObject("Sky Reflection Probe");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(ProceduralRiver.CentreX(_g, _g.Length * 0.5f), Surface(_g.Length * 0.5f) + 40f, _g.Length * 0.5f);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 256;
            probe.size = new Vector3(6000f, 3000f, 6000f);
            probe.boxProjection = false;
            probe.importance = 1;
            probe.RenderProbe();
        }
    }
}
