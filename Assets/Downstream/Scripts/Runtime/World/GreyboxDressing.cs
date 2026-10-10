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
        [SerializeField] private Material _spray;
        [Header("Vendor packs (CC0): used instead of the block props when set")]
        [SerializeField] private GameObject[] _vendorBroadleaf;
        [SerializeField] private GameObject[] _vendorPines;
        [SerializeField] private GameObject[] _vendorBushes;
        [SerializeField] private GameObject[] _vendorRocks;
        [SerializeField] private GameObject[] _vendorGrass;
        [SerializeField] private GameObject[] _vendorFlowers;
        [SerializeField] private float _grassPer100m = 30f;
        private readonly Dictionary<GameObject, float> _prefabHeights = new Dictionary<GameObject, float>();
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

        private const float BankTop = 1.9f;      // height of the grass terrace above the water
        private const float TerraceRun = 1.8f;   // metres over which the meadow rises to the terrace: a short earth riser
        private const float Tier = 4.5f;         // height of each hill tier (the reference's cliff layers)

        [Header("Ground seating")]
        [SerializeField, Tooltip("How far below the rendered ground the base of a scattered prop sits.")]
        private float _sinkDepth = 0.05f;
        [SerializeField, Tooltip("Trees lean with the slope only up to this angle.")]
        private float _treeTiltLimit = 8f;
        private readonly List<Collider> _ground = new List<Collider>();
        private readonly Dictionary<string, int[]> _seated = new Dictionary<string, int[]>();
        private string _category = "props";
        private Vector3 _lastNormal = Vector3.up;

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
            PrepareGround(hills);

            var trees = Group("Trees");
            var bushes = Group("Bushes");
            var rocks = Group("Rocks");
            var plants = Group("Plants");
            var story = Group("Story");
            var landmarks = Group("Landmarks");

            for (int side = -1; side <= 1; side += 2)
            {
                ScatterTrees(trees, side, 3.2f, 10.5f, _meadowTreesPer100m, 0.7f, false);
                ScatterTrees(trees, side, HillsIn + 4f, HillsIn + _hillDepth * 0.35f, _hillTreesPer100m, 0.45f, true);
                ScatterTrees(trees, side, HillsIn + _hillDepth * 0.35f, HillsIn + _hillDepth, _hillTreesPer100m * 0.35f, 0.4f, true);
                ScatterBushes(bushes, side, 3.2f, 10.5f, _bushesPer100m, false);
                ScatterBushes(bushes, side, HillsIn + 2f, HillsIn + _hillDepth * 0.5f, _bushesPer100m, true);
                ScatterReeds(plants, side);
                ScatterFlowers(plants, side, 3.2f, 10.5f);
                ScatterGrass(plants, side, 2.4f, 12f, _grassPer100m, false);
                ScatterGrass(plants, side, HillsIn + 2f, HillsIn + 40f, _grassPer100m * 0.5f, true);
                ScatterRocks(rocks, side, 2.2f, 10.5f, 1.4f, false);
                ScatterRocks(rocks, side, HillsIn + 2f, HillsIn + _hillDepth, 2.2f, true);
                LanternPosts(story, side);
            }
            BuildStoryClusters(story);
            Cairns(landmarks);
            Bridge(landmarks, 560f);
            Dock(landmarks, 330f, -1);
            Dock(landmarks, 1180f, 1);
            Shrine(landmarks, 900f, 1);
            FallsMist(landmarks);
            RiverBoulders(landmarks);
            SetUpLighting();
            foreach (var kv in _seated)
                Debug.Log($"[GreyboxDressing] {kv.Key}: {kv.Value[1]} of {kv.Value[0]} moved onto the rendered ground (by more than 5 cm)");
        }

        // ---- ground seating ----------------------------------------------------------------------

        /// <summary>
        /// The analytic land profile and the meshes built from it differ: the hills are piecewise linear on
        /// a 2.5-6 m grid and the bed mesh ramps down to the water over the bank, so anything placed on the
        /// formula floats or sinks where they disagree. Every scattered prop is therefore seated by a ray
        /// against the rendered ground: the bed and meadow, both hill sheets and the far ground.
        /// </summary>
        private void PrepareGround(Transform hills)
        {
            foreach (var mf in hills.GetComponentsInChildren<MeshFilter>()) AddGround(mf);
            var water = FindFirstObjectByType<GreyboxWaterMesh>();
            if (water != null && water.Bed != null) AddGround(water.Bed.GetComponent<MeshFilter>());
            Physics.SyncTransforms();
        }

        private void AddGround(MeshFilter mf)
        {
            if (mf == null || mf.sharedMesh == null) return;
            var c = mf.GetComponent<MeshCollider>();
            if (c == null) c = mf.gameObject.AddComponent<MeshCollider>();
            c.sharedMesh = mf.sharedMesh;
            _ground.Add(c);
        }

        /// <summary>Drops the point onto the highest rendered ground under it, a sink below the surface; keeps
        /// the ground normal for <see cref="Aligned"/>. Counts how many moved, per category.</summary>
        private Vector3 Seat(Vector3 pos, float sink = -1f)
        {
            if (sink < 0f) sink = _sinkDepth;
            _lastNormal = Vector3.up;
            if (!_seated.TryGetValue(_category, out var tally)) _seated[_category] = tally = new int[2];
            tally[0]++;
            if (_ground.Count == 0) return pos;
            var hits = Physics.RaycastAll(new Vector3(pos.x, pos.y + 300f, pos.z), Vector3.down, 1000f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MinValue; bool found = false;
            foreach (var h in hits)
            {
                if (!_ground.Contains(h.collider)) continue;
                if (h.point.y > best) { best = h.point.y; _lastNormal = h.normal; found = true; }
            }
            if (!found) return pos;
            float y = best - sink;
            if (Mathf.Abs(y - pos.y) > 0.05f) tally[1]++;
            pos.y = y;
            return pos;
        }

        /// <summary>The yaw tilted onto the last seated ground normal, up to a limit (90 = fully aligned).</summary>
        private Quaternion Aligned(Quaternion yaw, float tiltLimit)
        {
            float angle = Vector3.Angle(Vector3.up, _lastNormal);
            if (angle < 0.01f) return yaw;
            var tilt = Quaternion.FromToRotation(Vector3.up, _lastNormal);
            if (angle > tiltLimit) tilt = Quaternion.Slerp(Quaternion.identity, tilt, tiltLimit / angle);
            return tilt * yaw;
        }

        private readonly List<Material> _materials = new List<Material>();

        private void OnDestroy()
        {
            foreach (var m in _materials) if (m != null) Destroy(m);
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
        /// <summary>Lateral distance (from the centreline) where the terrain mesh begins: the outer edge of the floodable meadow.</summary>
        private float TerrainIn(float z) => HalfWidth(z) + _g.FloodableBank;
        /// <summary>Lateral distance past the meadow where the hills proper begin (past the terrace).</summary>
        private float HillsIn => _g.Width * (1f + _g.WidthVariation) * 0.5f + _g.FloodableBank + TerraceRun + 2f;

        private Vector3 World(int side, float lateral, float z, float y)
        {
            float cx = ProceduralRiver.CentreX(_g, z);
            float d = ProceduralRiver.CentreSlope(_g, z);
            // Lateral is measured across the channel; on a bend the same distance spans more x.
            return new Vector3(cx + side * lateral * Mathf.Sqrt(1f + d * d), y, z);
        }

        private Vector3 Tangent(float z)
        {
            float d = ProceduralRiver.CentreSlope(_g, z);
            return new Vector3(d, 0f, 1f).normalized;
        }

        /// <summary>
        /// Height of the land above the water surface at a lateral distance from the centreline: the
        /// floodable meadow, a grass terrace rising to the bank top, then hills in soft tiers (the
        /// reference's layered cliffs) with gentle noise on each tier.
        /// </summary>
        private float LandRise(float lateral, float z) => LandRise(lateral, z, z);

        private float LandRise(float lateral, float z, float zEdge)
        {
            float meadowEdge = TerrainIn(zEdge);
            if (lateral <= meadowEdge) return ProceduralRiver.BankShelfHeight;
            float t = Mathf.Clamp01((lateral - meadowEdge) / TerraceRun);
            float terrace = Mathf.Lerp(ProceduralRiver.BankShelfHeight, BankTop, t * t * (3f - 2f * t));
            float d = Mathf.Max(0f, lateral - meadowEdge - TerraceRun);
            if (d <= 0f) return terrace;
            float ramp = Mathf.SmoothStep(0f, 1f, d / 50f);
            float n = Fbm(lateral * 0.012f + 3.1f, z * 0.012f, 3) * 2f - 1f;
            float big = Fbm(lateral * 0.004f, z * 0.004f + 9.7f, 2) * 2f - 1f;
            float ridge = Mathf.Pow(Fbm(lateral * 0.02f + 21f, z * 0.006f + 4f, 2), 3f) * 12f;
            float rough = 6f + 9f * n + 14f * big + ridge + d * 0.045f;
            rough = 0.5f * (rough + Mathf.Sqrt(rough * rough + 9f)); // soft floor at zero: no pits below the terrace
            rough *= ramp;
            // Tiers: plateaus with short steep risers, softened so they read as layers, not stairs.
            float tiers = rough / Tier;
            float level = Mathf.Floor(tiers);
            float frac = tiers - level;
            float u = Mathf.Clamp01((frac - 0.3f) / 0.4f);
            float riser = u * u * (3f - 2f * u);
            float tiered = (level + riser) * Tier;
            float blend = Mathf.SmoothStep(0f, 1f, d / 25f); // the first few metres stay smooth
            float plateauNoise = (Fbm(lateral * 0.05f + 7f, z * 0.05f + 2f, 2) - 0.5f) * 0.8f;
            return terrace + Mathf.Lerp(rough, tiered + plateauNoise, blend);
        }

        /// <summary>Ground height for a prop at a lateral distance from the centreline.</summary>
        private float GroundY(int side, float lateral, float z) => SurfaceAtPoint(World(side, lateral, z, 0f).x, z, lateral) + LandRise(lateral, z);

        /// <summary>
        /// Water level under a terrain point. Near the channel it follows the water (level along the line
        /// square to the flow); out on the hills it eases back to level along world x, because the
        /// perpendiculars of a bend cross each other beyond its radius and a far point has no one distance.
        /// </summary>
        private float SurfaceAtPoint(float x, float z, float lateral)
        {
            float w = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((lateral - 30f) / 30f));
            float d = Mathf.Clamp(Mathf.Lerp(z, ProceduralRiver.DistanceAt(_g, x, z), w), 0f, _g.Length);
            return ProceduralRiver.SurfaceAt(_g, d);
        }

        /// <summary>Meadow placement: lateral is measured from the channel edge so it follows the breathing width.</summary>
        private Vector3 Meadow(int side, float fromEdge, float z, float lift = 0f)
        {
            float lateral = HalfWidth(z) + fromEdge;
            return Seat(World(side, lateral, z, GroundY(side, lateral, z))) + Vector3.up * lift;
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

        /// <summary>
        /// Re-skins the river boulders with the vendor rocks (wet: darker, glossier) and puts a standing
        /// splash on each upstream face, so an obstacle reads as a rock in moving water, not a block.
        /// </summary>
        private void RiverBoulders(Transform parent)
        {
            if (!Has(_vendorRocks) || _g.Boulders == null) return;
            var waterMesh = FindAnyObjectByType<GreyboxWaterMesh>();
            if (waterMesh == null) return;
            var byName = new Dictionary<string, Transform>();
            foreach (var tr in waterMesh.GetComponentsInChildren<Transform>(true)) if (tr.name.StartsWith("Boulder ")) byName[tr.name] = tr;
            for (int i = 0; i < _g.Boulders.Length; i++)
            {
                var b = _g.Boulders[i];
                if (!byName.TryGetValue($"Boulder {i}", out var blockT)) continue;
                var block = blockT.gameObject;
                var pos = block.transform.position;
                var mr = block.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
                var rock = VendorProp(parent, _vendorRocks[i % _vendorRocks.Length], pos - Vector3.up * (b.Radius * 0.45f), Quaternion.Euler(0f, 37f * i, 0f), b.Radius * 1.9f, true);
                foreach (var r in rock.GetComponentsInChildren<Renderer>())
                {
                    var mat = r.material; // instance: wet rock
                    _materials.Add(mat);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.GetColor("_BaseColor") * new Color(0.62f, 0.66f, 0.72f, 1f));
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.42f);
                    if (mat.HasProperty("_EnvironmentReflections")) { mat.SetFloat("_EnvironmentReflections", 1f); mat.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
                }
                var tangent = Tangent(b.Distance);
                Splash(parent, pos - tangent * (b.Radius * 0.85f) + Vector3.up * 0.1f, -tangent, b.Radius);
            }
        }

        /// <summary>A standing splash: water piling up and breaking on the upstream side of a rock.</summary>
        private void Splash(Transform parent, Vector3 pos, Vector3 upstream, float radius)
        {
            if (_spray == null) return;
            var go = new GameObject("Rock Splash");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(Vector3.up * 0.8f + upstream * 0.6f, Vector3.up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f * radius, 0.8f * radius);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 1f, 1f, 0.75f);
            main.gravityModifier = 1.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            var em = ps.emission; em.rateOverTime = 14f * radius;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = radius * 0.5f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) },
            });
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(1f, 1.3f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(_spray);
            if (mat.GetTexture("_BaseMap") == null) mat.SetTexture("_BaseMap", WaterTextures.SoftSprite);
            _materials.Add(mat);
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
        }

        /// <summary>Mist boiling up from the plunge pool below the waterfall, drifting downstream.</summary>
        private void FallsMist(Transform parent)
        {
            if (_spray == null || _g.WaterfallDistance < 0f || _g.WaterfallDrop <= 0f) return;
            float z = _g.WaterfallDistance + 2.5f;
            float width = ProceduralRiver.WidthAt(_g, z);
            var go = new GameObject("Falls Mist");
            go.transform.SetParent(parent, false);
            go.transform.position = World(0, 0f, z, Surface(z) + 0.3f);
            // Local Z up (the emitter's axis), local Y along the river, local X across it.
            go.transform.rotation = Quaternion.LookRotation(Vector3.up, Tangent(z));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 1f, 1f, 0.34f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.gravityModifier = -0.02f;
            var em = ps.emission; em.rateOverTime = 80f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(width * 0.9f, 4f, 0.5f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) },
            });
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.4f)));
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            var drift = Tangent(z) * 1.2f;
            vel.x = drift.x; vel.y = 0.2f; vel.z = drift.z;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.7f;
            noise.frequency = 0.25f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(_spray);
            if (mat.GetTexture("_BaseMap") == null) mat.SetTexture("_BaseMap", WaterTextures.SoftSprite);
            _materials.Add(mat);
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
        }

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
                var parts = new List<(Mesh, Matrix4x4)> { (_sphere, Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, 0.8f, 1f))) };
                int lobes = 3 + _rng.Next(3);
                for (int l = 0; l < lobes; l++)
                {
                    float ang = Rand(0f, Mathf.PI * 2f);
                    float r = Rand(0.55f, 0.78f);
                    var pos = new Vector3(Mathf.Cos(ang) * Rand(0.4f, 0.65f), Rand(0.05f, 0.5f), Mathf.Sin(ang) * Rand(0.4f, 0.65f));
                    parts.Add((_sphere, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(r, r * 0.8f, r))));
                }
                float knob = Rand(0.45f, 0.6f);
                parts.Add((_sphere, Matrix4x4.TRS(new Vector3(Rand(-0.15f, 0.15f), 0.72f, Rand(-0.15f, 0.15f)), Quaternion.identity, new Vector3(knob, knob * 0.8f, knob))));
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
                    h += coneH * 0.42f;
                    radius *= 0.74f;
                }
                parts.Add((_sphere, Matrix4x4.TRS(new Vector3(0f, h + 0.25f, 0f), Quaternion.identity, Vector3.one * 0.16f))); // soft tip
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

        /// <summary>
        /// One terrain sheet per side, in river coordinates: dense across the terrace, sparse out on the
        /// hills, then coarser still out to the fog in every direction, past both ends of the course as well
        /// (the dry valley continues, meandering on). Beyond the built hills the land rises toward eye level
        /// with rolling relief, so from a river that has dropped far below its start the horizon is still
        /// land dissolving into fog, never the sky's lower half. Inside the course the columns across the
        /// channel collapse under the meadow edge, where the bed mesh draws the ground; past the water's ends
        /// they spread across the valley floor, switching over 0.1 m so nothing shows.
        /// </summary>
        private void BuildHills(Transform parent, int side)
        {
            const float reach = 3600f;
            // Columns: u < 0 spans the channel (lateral = TerrainIn * (1 + u)); u >= 0 is the offset past the meadow.
            var cols = new List<float> { -1f, -0.7f, -0.4f, -0.15f };
            for (float o = 0.6f; o < TerraceRun + 2f; o += 0.35f) cols.Add(o); // starts past the bed mesh's meadow: no overlap to fight over
            for (float o = TerraceRun + 2f; o < TerraceRun + 6f; o += 1f) cols.Add(o);
            for (float o = TerraceRun + 6f; o < 40f; o += 2.5f) cols.Add(o);
            for (float o = 40f; o <= _hillDepth; o += 6f) cols.Add(o);
            for (float o = _hillDepth + 12f, step = 12f; o < reach; o += step, step *= 1.08f) cols.Add(o);
            // Rows: 3 m along the course, growing past the ends, plus the water's two ends.
            float wStart = -4f, wEnd = _g.Length + ProceduralRiver.RunOut;
            var rows = new List<float>();
            for (float z = -60f; z <= _g.Length + 80f + 0.01f; z += 3f) rows.Add(z);
            rows.Add(wStart); rows.Add(wStart + 0.1f); rows.Add(wEnd - 0.1f); rows.Add(wEnd);
            for (float step = 3f, z = -60f - step; z > -reach; step *= 1.08f, z -= step) rows.Add(z);
            for (float step = 3f, z = _g.Length + 80f + step; z < _g.Length + reach; step *= 1.08f, z += step) rows.Add(z);
            rows.Sort();
            for (int i = rows.Count - 1; i > 0; i--) if (rows[i] - rows[i - 1] < 0.05f) rows.RemoveAt(i);
            int nl = cols.Count, nz = rows.Count;
            var verts = new Vector3[nl * nz];
            var vcols = new Color[nl * nz];
            var uvs = new Vector2[nl * nz];
            for (int iz = 0; iz < nz; iz++)
            for (int il = 0; il < nl; il++)
            {
                float z = rows[iz], u = cols[il];
                float zc = Mathf.Clamp(z, 0f, _g.Length);
                bool overWater = z > wStart + 0.05f && z < wEnd - 0.05f;
                float edge = TerrainIn(zc);
                // Over the water the channel columns span from just past the bank ramp to the meadow edge, 8 cm
                // under the bed mesh's meadow: wherever that meadow stops short, this floors the gap to the terrace.
                float inner = HalfWidth(zc) + ProceduralRiver.BankRampLength + 1f;
                float lateral = u < 0f ? (overWater ? Mathf.Lerp(Mathf.Min(inner, edge - 0.5f), edge, 1f + u) : edge * (1f + u)) : edge + u;
                int i = iz * nl + il;
                var flat = World(side, lateral, z, 0f);
                // The channel columns take the bed's own meadow height (the two laterals differ a little on a
                // bend, so the land formula would poke through the bed), 8 cm under it.
                float y = u < 0f && overWater
                    ? SurfaceAtPoint(flat.x, z, lateral) + ProceduralRiver.BankShelfHeight - 0.08f
                    : SurfaceAtPoint(flat.x, z, lateral) + LandRise(lateral, z, zc);
                // Beyond the built hills (sideways or past the ends): rolling relief and a steady climb to eye level.
                float overshoot = Mathf.Max(0f, -60f - z, z - (_g.Length + 80f));
                float beyond = Mathf.Max(u - _hillDepth, overshoot);
                float far = Mathf.SmoothStep(0f, 1f, beyond / 250f);
                y += far * (Fbm(flat.x * 0.0009f + 31f, flat.z * 0.0009f, 3) * 90f + Fbm(flat.x * 0.004f, flat.z * 0.004f + 17f, 2) * 12f)
                   + Mathf.Max(0f, beyond) * 0.06f;
                verts[i] = new Vector3(flat.x, y, flat.z);
                float tint = Fbm(lateral * 0.05f + 11f, z * 0.05f, 2);
                vcols[i] = new Color(1f, tint, 0f, 1f);
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
            mesh.SetColors(vcols);
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

        private Vector3 Place(int side, float lIn, float lOut, float z, bool onHills)
        {
            if (onHills)
            {
                float lateral = Rand(lIn, lOut);
                return Seat(World(side, lateral, z, GroundY(side, lateral, z)));
            }
            return Meadow(side, Rand(lIn, lOut), z);
        }

        private void ScatterTrees(Transform parent, int side, float lIn, float lOut, float per100m, float pineShare, bool onHills)
        {
            _category = "trees";
            int count = Mathf.RoundToInt(per100m * _g.Length / 100f);
            for (int i = 0; i < count; i++)
            {
                float z = Rand(-20f, _g.Length + 40f);
                var pos = Place(side, lIn, lOut, z, onHills);
                float scale = Rand(0.8f, 1.4f);
                if (_rng.NextDouble() < 0.06) scale *= 1.6f; // the odd landmark tree
                var yaw = Aligned(Quaternion.Euler(0f, Rand(0f, 360f), 0f), _treeTiltLimit);
                if (_rng.NextDouble() < pineShare) Pine(parent, pos, yaw, scale);
                else RoundTree(parent, pos, yaw, scale);
            }
        }

        private static bool Has(GameObject[] set) => set != null && set.Length > 0 && set[0] != null;

        /// <summary>
        /// Places a vendor model scaled to a target height (the packs are not all in the same units), so the
        /// same density and silhouette rules hold whichever pack is wired.
        /// </summary>
        private GameObject VendorProp(Transform parent, GameObject prefab, Vector3 pos, Quaternion rot, float targetHeight, bool shadows)
        {
            if (!_prefabHeights.TryGetValue(prefab, out float h))
            {
                var probe = Instantiate(prefab);
                var b = new Bounds(probe.transform.position, Vector3.zero);
                bool any = false;
                foreach (var r in probe.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                h = any ? Mathf.Max(b.size.y, 0.01f) : 1f;
                Destroy(probe);
                _prefabHeights[prefab] = h;
            }
            var go = Instantiate(prefab, pos, rot, parent);
            go.transform.localScale = Vector3.one * (targetHeight / h);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.receiveShadows = true;
            }
            return go;
        }

        private void ScatterGrass(Transform parent, int side, float lIn, float lOut, float per100m, bool onHills)
        {
            if (!Has(_vendorGrass)) return;
            _category = "grass";
            int count = Mathf.RoundToInt(per100m * _g.Length / 100f);
            for (int i = 0; i < count; i++)
            {
                float z = Rand(0f, _g.Length);
                var pos = Place(side, lIn, lOut, z, onHills);
                VendorProp(parent, Pick(_vendorGrass), pos, Aligned(Quaternion.Euler(0f, Rand(0f, 360f), 0f), 90f), Rand(0.45f, 0.9f), false);
            }
        }

        private void RoundTree(Transform parent, Vector3 pos, Quaternion yaw, float scale)
        {
            if (Has(_vendorBroadleaf)) { VendorProp(parent, Pick(_vendorBroadleaf), pos, yaw, 7.5f * scale, true); return; }
            float trunkH = 1.7f * scale;
            float r = 2.6f * scale;
            Prop(parent, "Trunk", _trunkMesh, _trunk, pos, yaw, new Vector3(scale * 1.5f, trunkH + r * 0.6f, scale * 1.5f));
            Prop(parent, "Canopy", Pick(_canopyVariants), Pick(_canopies), pos + Vector3.up * (trunkH + r * 0.75f), yaw, new Vector3(r, r, r));
        }

        private void Pine(Transform parent, Vector3 pos, Quaternion yaw, float scale)
        {
            if (Has(_vendorPines)) { VendorProp(parent, Pick(_vendorPines), pos, yaw, 10f * scale, true); return; }
            float trunkH = 1.6f * scale;
            Prop(parent, "Trunk", _trunkMesh, _trunk, pos, yaw, new Vector3(scale * 0.9f, trunkH + 1f, scale * 0.9f));
            float radius = 2.5f * scale;
            Prop(parent, "Pine", Pick(_pineVariants), Pick(_pines), pos + Vector3.up * trunkH, yaw, new Vector3(radius, 3.0f * scale, radius));
        }

        private void ScatterBushes(Transform parent, int side, float lIn, float lOut, float per100m, bool onHills)
        {
            _category = "bushes";
            int count = Mathf.RoundToInt(per100m * _g.Length / 100f);
            for (int i = 0; i < count; i++)
            {
                float z = Rand(-10f, _g.Length + 20f);
                var pos = Place(side, lIn, lOut, z, onHills);
                float r = Rand(0.9f, 1.8f);
                if (Has(_vendorBushes)) { VendorProp(parent, Pick(_vendorBushes), pos, Aligned(Quaternion.Euler(0f, Rand(0f, 360f), 0f), 20f), r * 1.1f, true); continue; }
                Prop(parent, "Bush", Pick(_canopyVariants), Pick(_canopies), pos + Vector3.up * (r * 0.45f), Quaternion.Euler(0f, Rand(0f, 360f), 0f), new Vector3(r, r * 0.7f, r));
            }
        }

        private void ScatterRocks(Transform parent, int side, float lIn, float lOut, float per100m, bool onHills)
        {
            _category = "rocks";
            int count = Mathf.RoundToInt(per100m * _g.Length / 100f);
            for (int i = 0; i < count; i++)
            {
                float z = Rand(0f, _g.Length);
                var pos = Place(side, lIn, lOut, z, onHills);
                float s = Rand(0.5f, 1.6f);
                // Rocks bed in: a seventh of their height under the turf, and they lie with the slope.
                if (Has(_vendorRocks)) { VendorProp(parent, Pick(_vendorRocks), pos - Vector3.up * (s * 1.2f * 0.14f), Aligned(Quaternion.Euler(0f, Rand(0f, 360f), 0f), 90f), s * 1.2f, true); continue; }
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
            _category = "flowers";
            for (float z = 25f; z < _g.Length; z += Rand(28f, 60f))
            {
                var centre = Meadow(side, Rand(lIn, lOut), z);
                var mat = Pick(_flowers);
                int n = _rng.Next(6, 14);
                if (Has(_vendorFlowers))
                {
                    for (int i = 0; i < n / 2; i++)
                    {
                        var at = Seat(centre + new Vector3(Rand(-2.2f, 2.2f), 0f, Rand(-2.2f, 2.2f)));
                        VendorProp(parent, Pick(_vendorFlowers), at, Aligned(Quaternion.Euler(0f, Rand(0f, 360f), 0f), 90f), Rand(0.5f, 0.9f), false);
                    }
                    continue;
                }
                for (int i = 0; i < n; i++)
                {
                    var pos = Seat(centre + new Vector3(Rand(-1.6f, 1.6f), 0f, Rand(-1.6f, 1.6f))) + Vector3.up * 0.28f;
                    Prop(parent, "Flower", _flower, mat, pos, Quaternion.identity, Vector3.one * Rand(0.8f, 1.3f), false);
                    Prop(parent, "Stem", _reedMesh, _reed, pos - Vector3.up * 0.28f, Quaternion.identity, new Vector3(0.5f, 0.3f, 0.5f), false);
                }
            }
        }

        private void LanternPosts(Transform parent, int side)
        {
            _category = "props";
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
            _category = "props";
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

        /// <summary>
        /// A plank bridge over the river, high enough to race under. The deck spans from bank to bank and
        /// lands on a stone abutment at each end, with a plank ramp down to the terrace behind it, so it
        /// is anchored to the land rather than hanging in the air over the meadow; two piers stand in the
        /// channel (the river's pier boulders sit beside them).
        /// </summary>
        private void Bridge(Transform parent, float z)
        {
            _category = "props";
            var tangent = Tangent(z);
            var right = new Vector3(tangent.z, 0f, -tangent.x);
            float deckY = Surface(z) + 6.5f;
            var centre = new Vector3(ProceduralRiver.CentreX(_g, z), deckY, z);
            var yaw = Quaternion.LookRotation(right, Vector3.up);
            // The abutments stand on the terrace riser just past the meadow, where the land starts to climb.
            float abutmentLateral = TerrainIn(z) + 1.6f;
            float half = abutmentLateral - 1.6f; // the deck meets the abutment's inner face
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
                var foot = centre + right * (s * HalfWidth(z) * 0.55f);
                const float depth = 9f;
                Prop(parent, "Pier", _postMesh, _post, foot - Vector3.up * depth, Quaternion.identity, new Vector3(4.5f, depth, 4.5f));
            }
            for (int s = -1; s <= 1; s += 2)
            {
                // Stone abutment: a block from below the ground up to the deck, seated on the rendered terrain.
                var at = centre + right * (s * abutmentLateral);
                var ground = Seat(new Vector3(at.x, GroundY(s, abutmentLateral, z), at.z), 0f);
                float height = deckY + 0.15f - (ground.y - 1.5f);
                Prop(parent, "Abutment", _box, _rock, new Vector3(at.x, ground.y - 1.5f + height * 0.5f, at.z), yaw, new Vector3(4.6f, height, 3.4f));
                Prop(parent, "AbutmentCap", _plankMesh, _plank, new Vector3(at.x, deckY, at.z), yaw, new Vector3(4.2f, 1f, 3.6f));
                // Plank ramp from the abutment down to the terrace, a few metres further out.
                float rampLateral = abutmentLateral + 7.5f;
                var rampEnd = centre + right * (s * rampLateral);
                var rampGround = Seat(new Vector3(rampEnd.x, GroundY(s, rampLateral, z), rampEnd.z), 0f);
                var top = new Vector3(at.x, deckY, at.z) + right * (s * 1.7f);
                var bottom = new Vector3(rampGround.x, rampGround.y + 0.25f, rampGround.z);
                var run = bottom - top;
                float length = run.magnitude;
                var rampRot = Quaternion.LookRotation(run.normalized, Vector3.up);
                Prop(parent, "Ramp", _plankMesh, _plank, (top + bottom) * 0.5f, rampRot, new Vector3(3.4f, 1f, length));
                for (int k = 0; k < 3; k++)
                {
                    // Posts beside the ramp, from the ground up to the deck line, so it reads as a trestle.
                    var along = Vector3.Lerp(top, bottom, (k + 0.5f) / 3f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var p = along + tangent * (side * 1.6f);
                        var g = Seat(new Vector3(p.x, p.y - 2f, p.z), 0f);
                        float h = Mathf.Max(0.6f, p.y + 0.9f - g.y);
                        Prop(parent, "Post", _postMesh, _post, g, Quaternion.identity, new Vector3(1.1f, h, 1.1f));
                    }
                }
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
            // The painted sky's clouds come from the same generated noise as the ground; the asset cannot hold it.
            var sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty("_CloudMap")) sky.SetTexture("_CloudMap", WaterTextures.SoftNoise);
            // Sky-matched fog: the ground, water and prop shaders fog toward the panorama in the view direction.
            if (sky != null && sky.HasProperty("_Rotation") && sky.HasProperty("_MaxBrightness") && sky.GetTexture("_MainTex") != null)
            {
                Shader.SetGlobalTexture("_DownstreamSkyTex", sky.GetTexture("_MainTex"));
                Shader.SetGlobalVector("_DownstreamSkyParams", new Vector4(sky.GetFloat("_Rotation"), sky.GetFloat("_Exposure"), sky.GetFloat("_MaxBrightness"), 1f));
            }
            else Shader.SetGlobalVector("_DownstreamSkyParams", Vector4.zero);
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
