using Downstream.Cameras;
using Downstream.Core.Items;
using Downstream.Race;
using Downstream.Water;
using UnityEngine;

namespace Downstream.Items
{
    /// <summary>
    /// The race's hit, item and pickup effects, driven from the sim's events and the item objects'
    /// lifecycle so nothing here holds gameplay state: a pike's muzzle flash and skipping spray, trails
    /// on thrown mines and whirls, a mine's water geyser with smoke, debris and a shock ring, log
    /// splinters, the shield pop, the spin-out stars circling a hit pilot, the shield's circling glints,
    /// the Kingfisher's circling shadow and dive, and the pickup sparkle. Everything is pooled in a few
    /// particle systems and emitted by hand, so idle cost is nil and a busy frame is a few dozen sprites.
    /// Hits also kick the chase camera of whoever is watching.
    /// </summary>
    public sealed class RaceEffects : MonoBehaviour
    {
        [SerializeField] private RaceDirector _director;
        [SerializeField] private Material _spray;
        [SerializeField] private Material _spark;

        private static readonly Color Sunflower = new Color(1f, 0.82f, 0.25f, 1f);
        private static readonly Color Shield = new Color(0.55f, 1f, 0.6f, 1f);
        private static readonly Color Whirl = new Color(0.45f, 0.75f, 1f, 1f);
        private static readonly Color Ember = new Color(1f, 0.6f, 0.2f, 1f);
        private static readonly Color Smoke = new Color(0.32f, 0.32f, 0.34f, 0.85f);
        private static readonly Color Debris = new Color(0.22f, 0.17f, 0.12f, 1f);
        private static readonly Color White = new Color(1f, 1f, 1f, 0.95f);

        private ParticleSystem _splash, _ring, _sparks, _smoke, _debris;
        private Material _sprayInstance, _ringInstance, _sparkInstance, _smokeInstance;
        private readonly ItemObject[] _prev = new ItemObject[ItemSystem.MaxObjects];
        private TrailRenderer[] _trails;
        private ItemSystem _items;

        public void Configure(RaceDirector director, Material spray, Material spark)
        {
            _director = director;
            _spray = spray;
            _spark = spark;
        }

        private void Awake()
        {
            if (_spray == null) return;
            _sprayInstance = new Material(_spray);
            if (_sprayInstance.GetTexture("_BaseMap") == null) _sprayInstance.SetTexture("_BaseMap", WaterTextures.SoftSprite);
            _ringInstance = new Material(_spray); _ringInstance.SetTexture("_BaseMap", WaterTextures.RingSprite);
            _smokeInstance = new Material(_spray); _smokeInstance.SetTexture("_BaseMap", WaterTextures.SmokeSprite);
            _sparkInstance = new Material(_spark != null ? _spark : _spray); _sparkInstance.SetTexture("_BaseMap", WaterTextures.StarSprite);

            _splash = Pool("Fx Splash", _sprayInstance, ParticleSystemRenderMode.Billboard, 1.3f, 600, grow: 1.4f);
            _ring = Pool("Fx Ring", _ringInstance, ParticleSystemRenderMode.HorizontalBillboard, 0f, 64, grow: 3.2f);
            _sparks = Pool("Fx Sparks", _sparkInstance, ParticleSystemRenderMode.Billboard, 0.35f, 600, grow: 0.4f);
            _smoke = Pool("Fx Smoke", _smokeInstance, ParticleSystemRenderMode.Billboard, -0.25f, 200, grow: 2.2f);
            _debris = Pool("Fx Debris", _sprayInstance, ParticleSystemRenderMode.Billboard, 1.8f, 200, grow: 0.9f);

            _trails = new TrailRenderer[ItemSystem.MaxObjects];
            for (int i = 0; i < _trails.Length; i++)
            {
                var go = new GameObject($"Fx Trail {i}");
                go.transform.SetParent(transform, false);
                var tr = go.AddComponent<TrailRenderer>();
                tr.time = 0.4f;
                tr.minVertexDistance = 0.15f;
                tr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(1f, 0.05f));
                tr.sharedMaterial = _sparkInstance;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr.receiveShadows = false;
                tr.emitting = false;
                tr.enabled = false;
                _trails[i] = tr;
            }
        }

        private void OnDestroy()
        {
            foreach (var m in new[] { _sprayInstance, _ringInstance, _sparkInstance, _smokeInstance }) if (m != null) Destroy(m);
        }

        private ParticleSystem Pool(string name, Material material, ParticleSystemRenderMode mode, float gravity, int max, float grow)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.6f;
            main.startSpeed = 0f;
            main.startSize = 0.5f;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            var em = ps.emission; em.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.45f), new GradientAlphaKey(0f, 1f) },
            });
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, grow >= 1f ? 0.45f : 1f), new Keyframe(0.35f, 1f), new Keyframe(1f, grow)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = mode;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = -20f;
            return ps;
        }

        private void LateUpdate()
        {
            var race = _director != null ? _director.Race : null;
            if (race == null || _splash == null) return;
            var items = race.Items;
            if (items == null) return;
            if (items != _items)
            {
                _items = items;
                for (int k = 0; k < _prev.Length; k++) _prev[k] = default;
                foreach (var tr in _trails) { tr.emitting = false; tr.enabled = false; }
            }

            float t = race.Sim.RaceTime;
            var objects = items.Objects;
            for (int k = 0; k < objects.Length; k++)
            {
                var o = objects[k];
                var was = _prev[k];
                if (o.Kind != was.Kind) Transition(was, o, race);
                Continuous(k, o);
                _prev[k] = o;
            }

            for (int i = 0; i < race.Sim.BoatCount; i++)
            {
                if (race.Sim.Parked[i] || i >= _director.Views.Count) continue;
                var view = _director.Views[i];
                if (view == null || !view.gameObject.activeSelf) continue;
                var ev = _director.FrameItemEvents(i);
                if (ev != ItemEvents.None) BoatItemEvents(i, ev, view.transform);
                var boat = race.Sim.State.Boats[i];
                if (boat.SpinTime > 0f) SpinStars(view.transform, i);
                var bi = items.Boats[i];
                if (bi.ShieldTime > 0f) ShieldGlints(view.transform, i);
                if (bi.KingfisherIncoming > 0f) KingfisherShadow(view.transform, bi.KingfisherIncoming);
            }
        }

        // ---- boats ------------------------------------------------------------------------------

        private void BoatItemEvents(int i, ItemEvents ev, Transform boat)
        {
            var p = boat.position;
            var fwd = boat.forward; fwd.y = 0f; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            if ((ev & ItemEvents.Hit) != 0)
            {
                Ring(p, 5.5f, 0.7f, White);
                Burst(_splash, p + Vector3.up * 0.3f, 44, 4f, 9f, Vector3.up, 0.9f, 0.6f, 1.5f, 0.5f, 1f, White);
                Burst(_sparks, p + Vector3.up * 1.2f, 20, 3f, 7f, Vector3.up, 1.2f, 0.35f, 0.7f, 0.35f, 0.7f, Sunflower);
                Kick(i, 1f);
            }
            if ((ev & ItemEvents.Blocked) != 0)
            {
                Ring(p, 3.6f, 0.45f, Shield);
                Burst(_sparks, p + Vector3.up * 0.8f, 22, 3f, 7f, Vector3.up, 1.6f, 0.2f, 0.5f, 0.25f, 0.5f, Shield);
                Kick(i, 0.35f);
            }
            if ((ev & ItemEvents.Dodged) != 0)
                Burst(_sparks, p + Vector3.up * 0.6f, 10, 1.5f, 3.5f, Vector3.up, 1.2f, 0.15f, 0.35f, 0.25f, 0.45f, White);
            if ((ev & ItemEvents.Slung) != 0)
            {
                Ring(p, 3f, 0.4f, Whirl);
                Burst(_sparks, p + Vector3.up * 0.5f, 18, 6f, 12f, fwd, 0.5f, 0.2f, 0.5f, 0.25f, 0.45f, Whirl);
            }
            if ((ev & ItemEvents.Bounced) != 0)
            {
                Burst(_splash, p + Vector3.up * 0.3f, 22, 3f, 6f, right, 0.8f, 0.4f, 0.9f, 0.4f, 0.8f, White);
                Burst(_splash, p + Vector3.up * 0.3f, 22, 3f, 6f, -right, 0.8f, 0.4f, 0.9f, 0.4f, 0.8f, White);
                Kick(i, 0.3f);
            }
            if ((ev & ItemEvents.Swamped) != 0)
            {
                Ring(p, 3.5f, 0.5f, White);
                for (int n = 0; n < 24; n++)
                {
                    var at = p + fwd * Random.Range(-2f, 2f) + right * Random.Range(-1.2f, 1.2f) + Vector3.up * Random.Range(1.2f, 2.2f);
                    Emit(_splash, at, Vector3.down * Random.Range(1f, 3f) + Random.insideUnitSphere, Random.Range(0.5f, 1f), Random.Range(0.4f, 0.7f), White);
                }
            }
            if ((ev & ItemEvents.PickedUp) != 0)
            {
                Ring(p, 2.6f, 0.45f, Sunflower);
                Burst(_sparks, p + Vector3.up * 1f, 26, 2.5f, 6f, Vector3.up, 1.1f, 0.25f, 0.5f, 0.4f, 0.7f, Sunflower);
                Burst(_sparks, p + Vector3.up * 1f, 10, 2f, 4f, Vector3.up, 1.3f, 0.2f, 0.35f, 0.3f, 0.5f, White);
            }
            if ((ev & ItemEvents.Used) != 0)
                Burst(_sparks, p + Vector3.up * 0.9f, 8, 1.5f, 3.5f, Vector3.up, 1.5f, 0.15f, 0.3f, 0.2f, 0.35f, White);
        }

        /// <summary>Spin-out: cartoon stars circling the pilot's head for as long as the spin lasts.</summary>
        private void SpinStars(Transform boat, int i)
        {
            float a = Time.time * 9f + i * 1.3f;
            var head = boat.position + Vector3.up * 1.85f;
            for (int n = 0; n < 3; n++)
            {
                float ang = a + n * (2f * Mathf.PI / 3f);
                var offset = new Vector3(Mathf.Cos(ang), 0.1f * Mathf.Sin(ang * 2f), Mathf.Sin(ang)) * 0.9f;
                // Short-lived stars re-emitted every frame along the orbit, so they read as a ring that turns
                // rather than a spray thrown off the head.
                var tangent = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * 1.5f;
                Emit(_sparks, head + offset, tangent, 0.16f, 0.55f, Sunflower);
            }
        }

        /// <summary>Reed Shield: pale green glints circling the hull at the waterline.</summary>
        private void ShieldGlints(Transform boat, int i)
        {
            float a = Time.time * 4f + i * 0.7f;
            for (int n = 0; n < 3; n++)
            {
                float ang = a + n * (2f * Mathf.PI / 3f);
                var offset = new Vector3(Mathf.Cos(ang) * 1.5f, 0.45f + 0.2f * Mathf.Sin(ang * 2f), Mathf.Sin(ang) * 2.4f);
                Emit(_sparks, boat.TransformPoint(offset), Vector3.up * 0.4f, Random.Range(0.18f, 0.3f), 0.3f, Shield);
            }
        }

        /// <summary>Kingfisher inbound: a dark glint circling high over the target, tightening as the dive nears.</summary>
        private void KingfisherShadow(Transform boat, float seconds)
        {
            float a = Time.time * 5f;
            float radius = Mathf.Lerp(1.2f, 4f, Mathf.Clamp01(seconds / ItemRules.KingfisherWarning));
            var at = boat.position + Vector3.up * (4f + seconds) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
            Emit(_sparks, at, Vector3.zero, 0.45f, 0.25f, new Color(0.2f, 0.45f, 0.9f, 1f));
        }

        // ---- objects ----------------------------------------------------------------------------

        private void Transition(in ItemObject was, in ItemObject now, Core.Race.RaceSession race)
        {
            var p = now.Position.ToUnity();
            var pp = was.Position.ToUnity();
            bool expired = was.Age + 0.02f >= was.Life;
            switch (now.Kind)
            {
                case ItemObjectKind.Pike when was.Kind == ItemObjectKind.None:
                {
                    var dir = now.Velocity.Flat.ToUnity().normalized;
                    Burst(_sparks, p + Vector3.up * 0.4f, 12, 5f, 10f, dir, 0.35f, 0.25f, 0.5f, 0.2f, 0.35f, White);
                    Burst(_splash, p, 10, 2f, 5f, dir, 0.7f, 0.3f, 0.7f, 0.35f, 0.6f, White);
                    break;
                }
                case ItemObjectKind.ThrownMine when was.Kind == ItemObjectKind.None:
                case ItemObjectKind.ThrownWhirl when was.Kind == ItemObjectKind.None:
                    Burst(_sparks, p, 8, 1.5f, 4f, Vector3.up, 1.3f, 0.2f, 0.4f, 0.2f, 0.4f, now.Kind == ItemObjectKind.ThrownWhirl ? Whirl : White);
                    break;
                case ItemObjectKind.Mine when was.Kind == ItemObjectKind.ThrownMine:
                    Ring(p, 2.4f, 0.5f, White);
                    Burst(_splash, p, 18, 2f, 5f, Vector3.up, 0.8f, 0.3f, 0.8f, 0.4f, 0.7f, White);
                    break;
                case ItemObjectKind.Mine when was.Kind == ItemObjectKind.None:
                case ItemObjectKind.Log when was.Kind == ItemObjectKind.None:
                    Ring(p, 1.8f, 0.4f, White);
                    Burst(_splash, p, 8, 1f, 3f, Vector3.up, 0.8f, 0.25f, 0.6f, 0.35f, 0.6f, White);
                    break;
                case ItemObjectKind.Whirlpool when was.Kind == ItemObjectKind.ThrownWhirl:
                    Ring(p, 2f * ItemRules.WhirlRadius, 0.9f, Whirl);
                    Burst(_splash, p, 26, 2f, 6f, Vector3.up, 1.2f, 0.4f, 1f, 0.5f, 0.9f, White);
                    Burst(_sparks, p + Vector3.up * 0.5f, 14, 3f, 7f, Vector3.up, 1.1f, 0.2f, 0.45f, 0.3f, 0.6f, Whirl);
                    break;
                case ItemObjectKind.None:
                    switch (was.Kind)
                    {
                        case ItemObjectKind.Mine:
                            if (expired) { Ring(pp, 1.6f, 0.6f, White); break; }
                            Explosion(pp, race);
                            break;
                        case ItemObjectKind.Log:
                            if (expired) { Ring(pp, 1.6f, 0.6f, White); break; }
                            Ring(pp, 3f, 0.55f, White);
                            Burst(_debris, pp + Vector3.up * 0.3f, 16, 3f, 7f, Vector3.up, 1f, 0.15f, 0.35f, 0.5f, 0.9f, Debris);
                            Burst(_splash, pp, 18, 2f, 6f, Vector3.up, 0.9f, 0.35f, 0.9f, 0.4f, 0.8f, White);
                            break;
                        case ItemObjectKind.Pike:
                            Ring(pp, 2.2f, 0.4f, White);
                            Burst(_splash, pp, 16, 2f, 6f, Vector3.up, 1f, 0.3f, 0.8f, 0.35f, 0.7f, White);
                            Burst(_sparks, pp + Vector3.up * 0.4f, 8, 2f, 5f, Vector3.up, 1.2f, 0.2f, 0.4f, 0.2f, 0.4f, White);
                            break;
                        case ItemObjectKind.Whirlpool:
                            Ring(pp, 1.5f * ItemRules.WhirlRadius, 0.8f, Whirl);
                            Burst(_splash, pp, 14, 1f, 3f, Vector3.up, 1.3f, 0.3f, 0.7f, 0.4f, 0.8f, White);
                            break;
                        case ItemObjectKind.ThrownMine:
                        case ItemObjectKind.ThrownWhirl:
                            Burst(_debris, pp, 8, 1f, 3f, Vector3.up, 1f, 0.15f, 0.3f, 0.4f, 0.7f, Debris); // landed on the bank
                            break;
                        case ItemObjectKind.Kingfisher:
                        {
                            int target = was.Target;
                            if (target < 0 || target >= _director.Views.Count || _director.Views[target] == null) break;
                            var boat = _director.Views[target].transform.position;
                            var from = boat + Vector3.up * 9f + new Vector3(2f, 0f, -2f);
                            for (int n = 0; n < 16; n++)
                                Emit(_sparks, Vector3.Lerp(from, boat + Vector3.up * 0.8f, n / 15f), (boat - from).normalized * 4f, Random.Range(0.25f, 0.45f), 0.25f + 0.02f * n, new Color(0.3f, 0.6f, 1f, 1f));
                            Burst(_splash, boat, 22, 2f, 6f, Vector3.up, 1f, 0.35f, 0.8f, 0.4f, 0.8f, White);
                            Ring(boat, 3f, 0.5f, White);
                            break;
                        }
                    }
                    break;
            }
        }

        /// <summary>A mine going off: a geyser of water, a shock ring, embers, smoke and dark debris, and a kick to nearby cameras.</summary>
        private void Explosion(Vector3 p, Core.Race.RaceSession race)
        {
            Ring(p, 7f, 0.8f, White);
            Ring(p, 3.5f, 0.45f, Ember);
            Burst(_splash, p, 44, 5f, 12f, Vector3.up, 0.45f, 0.5f, 1.4f, 0.6f, 1.1f, White);
            Burst(_splash, p, 20, 3f, 7f, Vector3.up, 1.4f, 0.5f, 1.1f, 0.5f, 0.9f, White);
            Burst(_sparks, p + Vector3.up * 0.4f, 20, 2.5f, 6f, Vector3.up, 1.2f, 0.3f, 0.7f, 0.3f, 0.6f, Ember);
            Burst(_smoke, p + Vector3.up * 0.5f, 16, 1f, 2.5f, Vector3.up, 1.1f, 1f, 2.2f, 0.9f, 1.6f, Smoke);
            Burst(_debris, p + Vector3.up * 0.4f, 18, 4f, 9f, Vector3.up, 0.8f, 0.12f, 0.3f, 0.6f, 1.1f, Debris);
            foreach (var cam in _director.Cameras)
            {
                if (cam == null || cam.Target == null) continue;
                float d = Vector3.Distance(cam.Target.transform.position, p);
                if (d < 30f) cam.Kick(1f - d / 30f);
            }
        }

        /// <summary>Per-frame object presentation: trails on anything flying and skipping spray behind a pike.</summary>
        private void Continuous(int k, in ItemObject o)
        {
            bool flying = o.Active && (o.Kind == ItemObjectKind.Pike || o.Kind == ItemObjectKind.ThrownMine || o.Kind == ItemObjectKind.ThrownWhirl);
            var tr = _trails[k];
            if (!flying)
            {
                if (tr.emitting) { tr.emitting = false; tr.enabled = false; }
                return;
            }
            var p = o.Position.ToUnity();
            if (!tr.emitting)
            {
                tr.transform.position = p;
                tr.enabled = true;
                tr.Clear();
                tr.emitting = true;
                var c = o.Kind == ItemObjectKind.ThrownWhirl ? Whirl : o.Kind == ItemObjectKind.Pike ? new Color(0.85f, 0.95f, 1f, 1f) : new Color(1f, 0.8f, 0.6f, 1f);
                tr.startColor = c;
                tr.endColor = new Color(c.r, c.g, c.b, 0f);
            }
            tr.transform.position = p + (o.Kind == ItemObjectKind.Pike ? Vector3.up * 0.25f : Vector3.zero);
            if (o.Kind == ItemObjectKind.Pike)
            {
                var v = o.Velocity.ToUnity();
                for (int n = 0; n < 2; n++)
                    Emit(_splash, p + Random.insideUnitSphere * 0.3f, -v * 0.08f + Vector3.up * Random.Range(1.5f, 3.5f) + Random.insideUnitSphere * 1.2f, Random.Range(0.3f, 0.5f), Random.Range(0.25f, 0.5f), White);
            }
        }

        // ---- emit helpers -------------------------------------------------------------------------

        private void Kick(int boat, float strength)
        {
            foreach (var cam in _director.Cameras)
                if (cam != null && cam.Target != null && cam.Target.BoatIndex == boat) cam.Kick(strength);
        }

        /// <summary>A flat ring on the water that spreads and fades.</summary>
        private void Ring(Vector3 p, float size, float life, Color colour)
        {
            var race = _director.Race;
            float y = p.y;
            if (race != null)
            {
                var w = race.Sim.Water.Sample(p.x, p.z, race.Sim.RaceTime);
                if (w.IsWet) y = w.SurfaceHeight;
            }
            Emit(_ring, new Vector3(p.x, y + 0.12f, p.z), Vector3.zero, life, size, colour);
        }

        private static void Burst(ParticleSystem ps, Vector3 p, int count, float speedMin, float speedMax, Vector3 dir, float spread,
            float sizeMin, float sizeMax, float lifeMin, float lifeMax, Color colour)
        {
            for (int n = 0; n < count; n++)
            {
                var v = (dir + Random.insideUnitSphere * spread).normalized * Random.Range(speedMin, speedMax);
                Emit(ps, p + Random.insideUnitSphere * 0.25f, v, Random.Range(lifeMin, lifeMax), Random.Range(sizeMin, sizeMax), colour);
            }
        }

        private static void Emit(ParticleSystem ps, Vector3 p, Vector3 v, float life, float size, Color colour)
        {
            ps.Emit(new ParticleSystem.EmitParams
            {
                position = p,
                velocity = v,
                startLifetime = life,
                startSize = size,
                startColor = colour,
                rotation = Random.Range(0f, 360f),
            }, 1);
        }

        // ---- review hooks ---------------------------------------------------------------------------

        /// <summary>Plays one effect by name at a boat, for the editor review helper (no gameplay effect).</summary>
        public void Preview(string name, int boat)
        {
            if (_director == null || _director.Race == null || boat >= _director.Views.Count) return;
            var view = _director.Views[boat];
            var p = view.transform.position;
            var fwd = view.transform.forward; fwd.y = 0f; fwd.Normalize();
            switch (name)
            {
                case "hit": BoatItemEvents(boat, ItemEvents.Hit, view.transform); break;
                case "blocked": BoatItemEvents(boat, ItemEvents.Blocked, view.transform); break;
                case "slung": BoatItemEvents(boat, ItemEvents.Slung, view.transform); break;
                case "swamped": BoatItemEvents(boat, ItemEvents.Swamped, view.transform); break;
                case "pickup": BoatItemEvents(boat, ItemEvents.PickedUp, view.transform); break;
                case "explosion": Explosion(p + fwd * 7f, _director.Race); break;
                case "pike":
                    Transition(default, new ItemObject { Kind = ItemObjectKind.Pike, Position = (p + fwd * 3f).ToSim(), Velocity = (fwd * ItemRules.PikeSpeed).ToSim() }, _director.Race);
                    break;
                case "log":
                    Transition(new ItemObject { Kind = ItemObjectKind.Log, Position = (p + fwd * 6f).ToSim(), Life = 30f }, new ItemObject { Kind = ItemObjectKind.None, Position = (p + fwd * 6f).ToSim() }, _director.Race);
                    break;
                case "whirl":
                    Transition(new ItemObject { Kind = ItemObjectKind.ThrownWhirl }, new ItemObject { Kind = ItemObjectKind.Whirlpool, Position = (p + fwd * 8f).ToSim() }, _director.Race);
                    break;
                case "kingfisher":
                    Transition(new ItemObject { Kind = ItemObjectKind.Kingfisher, Target = (sbyte)boat }, new ItemObject { Kind = ItemObjectKind.None }, _director.Race);
                    break;
                case "stars": for (int n = 0; n < 6; n++) SpinStars(view.transform, boat); break;
                case "shield": for (int n = 0; n < 6; n++) ShieldGlints(view.transform, boat); break;
            }
        }
    }
}
