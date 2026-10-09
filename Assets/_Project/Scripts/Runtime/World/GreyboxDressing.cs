using System;
using System.Collections.Generic;
using Downstream.Core.Water;
using Downstream.Water;
using UnityEngine;
using UnityEngine.Rendering;

namespace Downstream.World
{
    /// <summary>
    /// Dresses the greybox river at runtime so the world reads as the design's toy diorama before
    /// any modelled kit exists: rolling grass hills behind the banks, round-canopy and pine trees,
    /// rocks, reeds at the waterline, flower clusters in the pop colours, and a small story cluster every
    /// 100 m of bank. Everything is built from the
    /// block kit's primitives with a fixed seed, nothing is saved in the scene, and the materials are
    /// shared so the SRP batcher keeps the draw count cheap.
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
        [Header("Density")]
        [SerializeField] private float _meadowTreesPer100m = 5f;
        [SerializeField] private float _hillTreesPer100m = 28f;
        [SerializeField] private float _hillDepth = 240f;

        private readonly List<Mesh> _meshes = new List<Mesh>();
        private Mesh _canopy, _cone, _trunkMesh, _rockA, _rockB, _rockC, _reedMesh, _flower, _tentMesh, _postMesh, _lanternMesh, _flag;
        private System.Random _rng;
        private ProceduralRiverSettings _g;
        private bool _built;

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

            var trees = new GameObject("Trees").transform;
            trees.SetParent(transform, false);
            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(transform, false);
            var plants = new GameObject("Plants").transform;
            plants.SetParent(transform, false);
            var story = new GameObject("Story").transform;
            story.SetParent(transform, false);

            float halfWidth = _g.Width * 0.5f;
            float meadowIn = halfWidth + 3.2f;             // past the channel lip
            float meadowOut = halfWidth + _g.FloodableBank - 1f;
            float hillsIn = halfWidth + _g.FloodableBank + 4.5f + 2f; // past the bank block's top

            for (int side = -1; side <= 1; side += 2)
            {
                // Meadow: a few trees, reeds at the water, flowers, rocks.
                ScatterTrees(trees, side, meadowIn, meadowOut, _meadowTreesPer100m, 0.75f);
                ScatterTrees(trees, side, hillsIn, hillsIn + _hillDepth * 0.35f, _hillTreesPer100m, 0.45f);
                ScatterTrees(trees, side, hillsIn + _hillDepth * 0.35f, hillsIn + _hillDepth, _hillTreesPer100m * 0.35f, 0.4f);
                ScatterReeds(plants, side, halfWidth + 1.3f, halfWidth + 2.6f);
                ScatterFlowers(plants, side, meadowIn, meadowOut);
                ScatterRocks(rocks, side, meadowIn, meadowOut, 1.6f);
                ScatterRocks(rocks, side, hillsIn, hillsIn + _hillDepth, 2.2f);
            }
            BuildStoryClusters(story, meadowIn + 1.5f, meadowOut - 1.5f);
        }

        private void OnDestroy()
        {
            foreach (var m in _meshes) if (m != null) Destroy(m);
            _meshes.Clear();
        }

        // ---- river geometry helpers -------------------------------------------------------------

        private float Surface(float z) => ProceduralRiver.SurfaceAt(_g, Mathf.Clamp(z, 0f, _g.Length));

        private Vector3 World(int side, float lateral, float z, float y)
        {
            float cx = ProceduralRiver.CentreX(_g, z);
            return new Vector3(cx + side * lateral, y, z);
        }

        /// <summary>Height of the hills above the river surface at a lateral distance past the bank top.</summary>
        private float HillRise(float lateral, float z, float hillsIn)
        {
            float d = Mathf.Max(0f, lateral - hillsIn);
            float ramp = Mathf.SmoothStep(0f, 1f, d / 40f);
            float n = Fbm(lateral * 0.012f + 3.1f, z * 0.012f, 3) * 2f - 1f;    // -1..1
            float big = Fbm(lateral * 0.004f, z * 0.004f + 9.7f, 2) * 2f - 1f;
            return 2.4f + ramp * (6f + 9f * n + 14f * big + d * 0.045f);
        }

        private float GroundY(int side, float lateral, float z)
        {
            float halfWidth = _g.Width * 0.5f;
            float hillsIn = halfWidth + _g.FloodableBank + 4.5f + 2f;
            float s = Surface(z);
            if (lateral < halfWidth + _g.FloodableBank) return s + ProceduralRiver.BankShelfHeight;
            if (lateral < hillsIn) return s + 2.4f;
            return s + HillRise(lateral, z, hillsIn);
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
            _canopy = Keep(BlockMeshes.FlatSphere(1f, 5, 9, "Canopy"));
            _cone = Keep(BlockMeshes.Cone(1f, 1f, 8, "Cone"));
            _trunkMesh = Keep(BlockMeshes.Cylinder(0.22f, 1f, 7, "Trunk"));
            _rockA = Keep(BlockMeshes.BevelledBox(new Vector3(1.6f, 1.0f, 1.2f), 0.3f, "RockA"));
            _rockB = Keep(BlockMeshes.BevelledBox(new Vector3(2.6f, 1.5f, 2.0f), 0.45f, "RockB"));
            _rockC = Keep(BlockMeshes.BevelledBox(new Vector3(0.8f, 0.6f, 0.7f), 0.18f, "RockC"));
            _reedMesh = Keep(BlockMeshes.Cone(0.07f, 1f, 5, "Reed"));
            _flower = Keep(BlockMeshes.FlatSphere(0.16f, 3, 6, "Flower"));
            _tentMesh = Keep(BlockMeshes.BevelledBox(new Vector3(3.2f, 3.2f, 3.6f), 0.15f, "Tent"));
            _postMesh = Keep(BlockMeshes.Cylinder(0.09f, 1f, 6, "Post"));
            _lanternMesh = Keep(BlockMeshes.BevelledBox(new Vector3(0.35f, 0.45f, 0.35f), 0.06f, "Lantern"));
            _flag = Keep(BlockMeshes.BevelledBox(new Vector3(0.45f, 0.35f, 0.04f), 0.03f, "Flag"));
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
            float halfWidth = _g.Width * 0.5f;
            float hillsIn = halfWidth + _g.FloodableBank + 4.5f;
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
                float y = Surface(z) + HillRise(lateral, z, hillsIn + 2f);
                int i = iz * nl + il;
                verts[i] = World(side, lateral, z, y);
                // R = grass, G = tint variation (design: each block's grass tint varies by a few percent).
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
        }

        // ---- scatter ----------------------------------------------------------------------------

        private void ScatterTrees(Transform parent, int side, float lIn, float lOut, float per100m, float pineShare)
        {
            int count = Mathf.RoundToInt(per100m * _g.Length / 100f);
            for (int i = 0; i < count; i++)
            {
                float z = Rand(-20f, _g.Length + 40f);
                float lateral = Rand(lIn, lOut);
                var pos = World(side, lateral, z, GroundY(side, lateral, z));
                float scale = Rand(0.8f, 1.35f);
                var yaw = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
                if (_rng.NextDouble() < pineShare) Pine(parent, pos, yaw, scale);
                else RoundTree(parent, pos, yaw, scale);
            }
        }

        private void RoundTree(Transform parent, Vector3 pos, Quaternion yaw, float scale)
        {
            var tree = new GameObject("Tree").transform;
            tree.SetParent(parent, false);
            tree.SetPositionAndRotation(pos, yaw);
            float trunkH = 2.2f * scale;
            Prop(tree, "Trunk", _trunkMesh, _trunk, pos, yaw, new Vector3(scale, trunkH, scale));
            var canopy = Pick(_canopies);
            float r = 2.6f * scale;
            Prop(tree, "Canopy", _canopy, canopy, pos + Vector3.up * (trunkH + r * 0.75f), yaw, new Vector3(r, r * 0.85f, r));
            // A second, smaller lobe makes the silhouette less of a lollipop.
            float r2 = r * 0.62f;
            var off = yaw * new Vector3(r * 0.55f, trunkH + r * 0.95f, r * 0.2f);
            Prop(tree, "Canopy2", _canopy, canopy, pos + off, yaw, new Vector3(r2, r2 * 0.8f, r2));
        }

        private void Pine(Transform parent, Vector3 pos, Quaternion yaw, float scale)
        {
            var tree = new GameObject("Pine").transform;
            tree.SetParent(parent, false);
            tree.SetPositionAndRotation(pos, yaw);
            float trunkH = 1.4f * scale;
            Prop(tree, "Trunk", _trunkMesh, _trunk, pos, yaw, new Vector3(scale * 0.9f, trunkH, scale * 0.9f));
            var mat = Pick(_pines);
            float h = trunkH;
            float radius = 2.4f * scale;
            for (int tier = 0; tier < 3; tier++)
            {
                float coneH = 2.6f * scale * (1f - tier * 0.12f);
                Prop(tree, "Tier", _cone, mat, pos + Vector3.up * h, yaw, new Vector3(radius, coneH, radius));
                h += coneH * 0.55f;
                radius *= 0.72f;
            }
        }

        private void ScatterRocks(Transform parent, int side, float lIn, float lOut, float per100m)
        {
            int count = Mathf.RoundToInt(per100m * _g.Length / 100f);
            var meshes = new[] { _rockA, _rockB, _rockC, _rockC };
            for (int i = 0; i < count; i++)
            {
                float z = Rand(0f, _g.Length);
                float lateral = Rand(lIn, lOut);
                var pos = World(side, lateral, z, GroundY(side, lateral, z) - 0.15f);
                var rot = Quaternion.Euler(Rand(-8f, 8f), Rand(0f, 360f), Rand(-8f, 8f));
                float s = Rand(0.7f, 1.4f);
                Prop(parent, "Rock", Pick(meshes), _rock, pos, rot, new Vector3(s, s * Rand(0.8f, 1.1f), s));
            }
        }

        private void ScatterReeds(Transform parent, int side, float lIn, float lOut)
        {
            for (float z = 10f; z < _g.Length; z += Rand(9f, 26f))
            {
                float lateral = Rand(lIn, lOut);
                var centre = World(side, lateral, z, Surface(z) - 0.1f);
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
            for (float z = 25f; z < _g.Length; z += Rand(30f, 70f))
            {
                float lateral = Rand(lIn, lOut);
                var centre = World(side, lateral, z, GroundY(side, lateral, z));
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

        // ---- story clusters -----------------------------------------------------------------------

        private void BuildStoryClusters(Transform parent, float lIn, float lOut)
        {
            int side = 1;
            for (float z = 60f; z < _g.Length - 40f; z += 100f)
            {
                side = -side;
                float lateral = Rand(lIn, lOut);
                var centre = World(side, lateral, z, GroundY(side, lateral, z));
                if (((int)(z / 100f) & 1) == 0) Camp(parent, centre, side);
                else Bunting(parent, centre, side, z);
            }
        }

        private void Camp(Transform parent, Vector3 centre, int side)
        {
            var yaw = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            // A tent: a block stood on its edge reads as a ridge tent.
            Prop(parent, "Tent", _tentMesh, _tent, centre + Vector3.up * 1.2f, yaw * Quaternion.Euler(0f, 0f, 45f), new Vector3(0.75f, 0.75f, 1f));
            var postPos = centre + yaw * new Vector3(3f, 0f, 0.5f);
            Prop(parent, "Post", _postMesh, _post, postPos, Quaternion.identity, new Vector3(1f, 2.2f, 1f));
            Prop(parent, "Lantern", _lanternMesh, _lantern, postPos + Vector3.up * 2.1f, Quaternion.identity, Vector3.one, false);
            for (int i = 0; i < 3; i++)
            {
                var pos = centre + yaw * new Vector3(Rand(-3f, 3f), 0f, Rand(2f, 4f));
                Prop(parent, "Rock", _rockC, _rock, pos, Quaternion.Euler(0f, Rand(0f, 360f), 0f), Vector3.one * Rand(0.9f, 1.4f));
            }
        }

        private void Bunting(Transform parent, Vector3 centre, int side, float z)
        {
            // Two posts along the bank with a line of flags between them.
            var a = centre + new Vector3(0f, 0f, -5f);
            var b = centre + new Vector3(0f, 0f, 5f);
            Prop(parent, "Post", _postMesh, _post, a, Quaternion.identity, new Vector3(1f, 2.8f, 1f));
            Prop(parent, "Post", _postMesh, _post, b, Quaternion.identity, new Vector3(1f, 2.8f, 1f));
            int flags = 12;
            for (int i = 0; i <= flags; i++)
            {
                float t = i / (float)flags;
                float sag = 0.5f * Mathf.Sin(t * Mathf.PI);
                var pos = Vector3.Lerp(a, b, t) + Vector3.up * (2.7f - sag);
                Prop(parent, "Flag", _flag, Pick(_flowers), pos, Quaternion.Euler(0f, 90f, 0f), Vector3.one, false);
            }
        }
    }
}
