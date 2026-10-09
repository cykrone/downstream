using System.Collections.Generic;
using Downstream.Core.Boat;
using Downstream.Core.Water;
using Downstream.Water;
using UnityEngine;

namespace Downstream.Boat
{
    /// <summary>
    /// Motion effects that sell speed: a stern wake and two bow wakes left on the water, bow spray
    /// that scales with speed, drift spray off the outside of a slide (coloured by tier), a boost
    /// plume and a landing splash. Everything is driven from the presented <see cref="BoatState"/>
    /// so it stays in step with the sim and costs nothing when the boat is still.
    /// </summary>
    public sealed class BoatEffects : MonoBehaviour
    {
        [SerializeField] private Material _wake;
        [SerializeField] private Material _spray;
        [SerializeField] private float _fullWakeSpeed = 18f;

        private static readonly Color[] DriftTiers =
        {
            new Color(1f, 1f, 1f, 0.9f),
            new Color(0.75f, 0.92f, 1f, 0.95f),
            new Color(1f, 0.84f, 0.45f, 1f),
            new Color(1f, 0.55f, 0.35f, 1f),
        };

        private WakeRibbon _stern, _port, _starboard;
        private ParticleSystem _bow, _driftL, _driftR, _boost;
        private Material _wakeInstance, _sprayInstance;
        private float _prevSlap;
        private bool _wasAirborne;

        public void Configure(Material wake, Material spray)
        {
            _wake = wake;
            _spray = spray;
        }

        private void Awake()
        {
            if (_wake == null || _spray == null) return;
            _wakeInstance = new Material(_wake);
            _sprayInstance = new Material(_spray);
            if (_wakeInstance.GetTexture("_BaseMap") == null) _wakeInstance.SetTexture("_BaseMap", WaterTextures.WakeBand);
            if (_sprayInstance.GetTexture("_BaseMap") == null) _sprayInstance.SetTexture("_BaseMap", WaterTextures.SoftSprite);

            _stern = new WakeRibbon("Wake Stern", _wakeInstance, new Vector3(0f, 0f, -1.95f), 2.2f, 0.9f, 3.0f, 0.75f);
            _port = new WakeRibbon("Wake Port", _wakeInstance, new Vector3(-0.95f, 0f, 0.5f), 2.8f, 0.25f, 1.5f, 0.5f);
            _starboard = new WakeRibbon("Wake Starboard", _wakeInstance, new Vector3(0.95f, 0f, 0.5f), 2.8f, 0.25f, 1.5f, 0.5f);

            _bow = Spray("Bow Spray", new Vector3(0f, 0.25f, 1.7f), Quaternion.Euler(-62f, 0f, 0f), 38f, 0.3f);
            var bowEm = _bow.emission; bowEm.rateOverDistance = 4f; bowEm.rateOverTime = 0f;
            _driftL = Spray("Drift Spray L", new Vector3(-0.9f, 0.2f, 0.2f), Quaternion.Euler(-40f, -90f, 0f), 30f, 0.2f);
            _driftR = Spray("Drift Spray R", new Vector3(0.9f, 0.2f, 0.2f), Quaternion.Euler(-40f, 90f, 0f), 30f, 0.2f);
            foreach (var d in new[] { _driftL, _driftR }) { var em = d.emission; em.rateOverDistance = 10f; em.rateOverTime = 0f; }
            _boost = Spray("Boost Plume", new Vector3(0f, 0.2f, -2f), Quaternion.Euler(-25f, 180f, 0f), 22f, 0.25f);
            var boostEm = _boost.emission; boostEm.rateOverTime = 90f; boostEm.rateOverDistance = 0f;
            var boostMain = _boost.main; boostMain.startSpeed = new ParticleSystem.MinMaxCurve(4f, 7f); boostMain.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        }

        private void OnDestroy()
        {
            _stern?.Dispose(); _port?.Dispose(); _starboard?.Dispose();
            if (_wakeInstance != null) Destroy(_wakeInstance);
            if (_sprayInstance != null) Destroy(_sprayInstance);
        }

        private void OnDisable()
        {
            // A respawned boat starts a fresh wake.
            _stern?.Clear(); _port?.Clear(); _starboard?.Clear();
        }

        public void Apply(in BoatState s, RiverWater water, float raceTime)
        {
            if (_stern == null) return;
            float speed = s.Velocity.ToUnity().magnitude;
            bool wet = !s.Airborne && s.WetFraction > 0.2f;
            float k = Mathf.Clamp01(speed / _fullWakeSpeed);

            bool wake = wet && speed > 1.5f;
            float width = Mathf.Lerp(0.55f, 1.35f, k);
            float now = Time.time;
            _stern.Tick(now, transform, wake, width, water, raceTime);
            _port.Tick(now, transform, wake, width * 0.75f, water, raceTime);
            _starboard.Tick(now, transform, wake, width * 0.75f, water, raceTime);

            SetEmitting(_bow, wet && speed > 4f);
            var bowMain = _bow.main;
            bowMain.startSpeed = new ParticleSystem.MinMaxCurve(1.5f + 3f * k, 3f + 5f * k);
            bowMain.startSize = new ParticleSystem.MinMaxCurve(0.3f + 0.4f * k, 0.7f + 0.7f * k);

            bool drifting = wet && s.DriftDirection != 0 && speed > 3f;
            // Spray flies off the outside of the slide: drifting right (+1) throws water to the left.
            var tier = DriftTiers[Mathf.Clamp(s.DriftTier, 0, DriftTiers.Length - 1)];
            SetEmitting(_driftL, drifting && s.DriftDirection > 0);
            SetEmitting(_driftR, drifting && s.DriftDirection < 0);
            foreach (var d in new[] { _driftL, _driftR }) { var m = d.main; m.startColor = tier; }

            SetEmitting(_boost, wet && s.BoostTime > 0f);

            // Landing splash: a burst when the hull slaps down or comes back from the air.
            bool landed = (_wasAirborne && !s.Airborne) || (s.SlapTime > 0f && _prevSlap <= 0f);
            if (landed && wet)
            {
                _bow.Emit(new ParticleSystem.EmitParams { startSize = 1.2f, startLifetime = 0.8f, velocity = Vector3.up * 3.5f }, 1);
                _bow.Emit(28);
                _driftL.Emit(14);
                _driftR.Emit(14);
            }
            _wasAirborne = s.Airborne;
            _prevSlap = s.SlapTime;

        }

        private static void SetEmitting(ParticleSystem ps, bool on)
        {
            var em = ps.emission;
            if (em.enabled != on) em.enabled = on;
        }

        private ParticleSystem Spray(string name, Vector3 local, Quaternion rotation, float coneAngle, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            go.transform.localRotation = rotation;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(1f, 1f, 1f, 0.9f);
            main.gravityModifier = 1.3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var em = ps.emission; em.enabled = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = radius;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) },
            });
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.3f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = _sprayInstance;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = -10f;
            ps.Play();
            return ps;
        }
        /// <summary>
        /// A wake ribbon built by hand: a strip of quads along the path of one point on the hull, widening and
        /// fading toward the tail, every vertex re-seated on the water surface so waves never cut it. Lives at
        /// the world origin so the hull's pitch and roll never tilt it.
        /// </summary>
        private sealed class WakeRibbon
        {
            private struct Point { public Vector3 Position; public float Time; }

            private readonly List<Point> _points = new List<Point>(128);
            private readonly GameObject _go;
            private readonly Mesh _mesh;
            private readonly MeshRenderer _renderer;
            private readonly Vector3 _local;
            private readonly float _life, _headWidth, _tailWidth, _alpha;
            private const float Spacing = 0.35f;
            private const float Lift = 0.08f;
            private readonly List<Vector3> _verts = new List<Vector3>(256);
            private readonly List<Vector2> _uvs = new List<Vector2>(256);
            private readonly List<Color> _cols = new List<Color>(256);
            private readonly List<int> _tris = new List<int>(768);
            private int _frame;

            public WakeRibbon(string name, Material material, Vector3 local, float life, float headWidth, float tailWidth, float alpha)
            {
                _local = local; _life = life; _headWidth = headWidth; _tailWidth = tailWidth; _alpha = alpha;
                _go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                _mesh = new Mesh { name = name };
                _mesh.MarkDynamic();
                _go.GetComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = _go.GetComponent<MeshRenderer>();
                _renderer.sharedMaterial = material;
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _renderer.enabled = false;
            }

            public void Clear() { _points.Clear(); if (_renderer != null) _renderer.enabled = false; }

            public void Dispose()
            {
                if (_go != null) Destroy(_go);
                if (_mesh != null) Destroy(_mesh);
            }

            public void Tick(float now, Transform hull, bool emitting, float widthScale, RiverWater water, float raceTime)
            {
                if (emitting)
                {
                    var p = hull.TransformPoint(_local);
                    if (_points.Count == 0 || (p - _points[_points.Count - 1].Position).sqrMagnitude >= Spacing * Spacing)
                        _points.Add(new Point { Position = p, Time = now });
                }
                while (_points.Count > 0 && now - _points[0].Time > _life) _points.RemoveAt(0);
                int n = _points.Count;
                if (n < 2) { _renderer.enabled = false; return; }
                _renderer.enabled = true;
                _frame++;

                _verts.Clear(); _uvs.Clear(); _cols.Clear(); _tris.Clear();
                for (int i = 0; i < n; i++)
                {
                    var pt = _points[i];
                    var p = pt.Position;
                    // Re-seat on the water a third of the points per frame: waves move slowly, boats do not.
                    if (water != null && (i + _frame) % 3 == 0)
                    {
                        var sample = water.Sample(p.x, p.z, raceTime);
                        if (sample.IsWet) { p.y = sample.SurfaceHeight + Lift; pt.Position = p; _points[i] = pt; }
                    }
                    var ahead = _points[Mathf.Min(i + 1, n - 1)].Position - _points[Mathf.Max(i - 1, 0)].Position;
                    ahead.y = 0f;
                    if (ahead.sqrMagnitude < 1e-6f) ahead = Vector3.forward;
                    var side = Vector3.Cross(Vector3.up, ahead.normalized);
                    float age = Mathf.Clamp01((now - pt.Time) / _life);
                    float w = Mathf.Lerp(_headWidth, _tailWidth, Mathf.Sqrt(age)) * widthScale * 0.5f;
                    float a = _alpha * (1f - age) * (1f - age);
                    _verts.Add(p - side * w); _verts.Add(p + side * w);
                    _uvs.Add(new Vector2(1f - age, 0f)); _uvs.Add(new Vector2(1f - age, 1f));
                    var c = new Color(1f, 1f, 1f, a);
                    _cols.Add(c); _cols.Add(c);
                    if (i > 0)
                    {
                        int b = i * 2;
                        _tris.Add(b - 2); _tris.Add(b); _tris.Add(b - 1);
                        _tris.Add(b - 1); _tris.Add(b); _tris.Add(b + 1);
                    }
                }
                _mesh.Clear();
                _mesh.SetVertices(_verts);
                _mesh.SetUVs(0, _uvs);
                _mesh.SetColors(_cols);
                _mesh.SetTriangles(_tris, 0);
                _mesh.RecalculateBounds();
            }
        }
    }
}
