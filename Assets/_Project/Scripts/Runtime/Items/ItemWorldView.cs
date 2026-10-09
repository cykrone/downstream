using System.Collections.Generic;
using Downstream.Core.Items;
using Downstream.Water;
using Downstream.World;
using UnityEngine;

namespace Downstream.Items
{
    /// <summary>
    /// Presentation of items: pickup buoys, floating mines and logs, pikes, thrown items and whirlpools
    /// as small procedural props, pooled and placed from the sim every frame. Holds no gameplay state.
    /// </summary>
    public sealed class ItemWorldView : MonoBehaviour
    {
        [SerializeField] private Material _material;
        [SerializeField] private Material _whirlMaterial;

        private ItemSystem _items;
        private readonly List<Transform> _buoys = new List<Transform>();
        private Transform[] _objects;
        private MeshFilter[] _objectFilters;
        private Renderer[] _objectRenderers;
        private ItemObjectKind[] _shownKind;
        private MaterialPropertyBlock _block;
        private Material _whirlInstance;
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private Mesh _buoyFloat, _buoyRing, _mine, _log, _pike, _ball, _whirl;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly Color Sunflower = new Color(1f, 0.78f, 0.2f);

        public void Bind(ItemSystem items)
        {
            _items = items;
            _block ??= new MaterialPropertyBlock();
            EnsureMeshes();
            foreach (var b in _buoys) Destroy(b.gameObject);
            _buoys.Clear();
            for (int i = 0; i < items.Buoys.Length; i++)
            {
                // A striped float on a white ring: the pickup, bobbing and turning so it catches the eye.
                var root = new GameObject($"Item Buoy {i}").transform;
                root.SetParent(transform, false);
                root.position = items.Buoys[i].Position.ToUnity() + Vector3.up * 0.2f;
                Make(root, "Float", _buoyFloat, _material, Sunflower);
                Make(root, "Ring", _buoyRing, _material, new Color(0.96f, 0.95f, 0.9f));
                _buoys.Add(root);
            }

            if (_objects == null)
            {
                _objects = new Transform[ItemSystem.MaxObjects];
                _objectFilters = new MeshFilter[ItemSystem.MaxObjects];
                _objectRenderers = new Renderer[ItemSystem.MaxObjects];
                _shownKind = new ItemObjectKind[ItemSystem.MaxObjects];
                for (int i = 0; i < _objects.Length; i++)
                {
                    var go = new GameObject($"Item {i}", typeof(MeshFilter), typeof(MeshRenderer));
                    go.transform.SetParent(transform, false);
                    _objects[i] = go.transform;
                    _objectFilters[i] = go.GetComponent<MeshFilter>();
                    _objectRenderers[i] = go.GetComponent<MeshRenderer>();
                    _objectRenderers[i].sharedMaterial = _material;
                    go.SetActive(false);
                }
            }
            for (int i = 0; i < _shownKind.Length; i++) _shownKind[i] = ItemObjectKind.None;
        }

        private void EnsureMeshes()
        {
            if (_buoyFloat != null) return;
            var sphere = BlockMeshes.SmoothSphere(1f, 10, 14, "ItemSphere");
            _meshes.Add(sphere);
            _ball = sphere;
            _buoyFloat = Keep(BlockMeshes.Merge("BuoyFloat", new List<(Mesh, Matrix4x4)>
            {
                (sphere, Matrix4x4.TRS(new Vector3(0f, 0.55f, 0f), Quaternion.identity, new Vector3(0.55f, 0.62f, 0.55f))),
                (BlockMeshes.Cylinder(0.06f, 1.1f, 8, "BuoyMast"), Matrix4x4.TRS(new Vector3(0f, 0.9f, 0f), Quaternion.identity, Vector3.one)),
                (BlockMeshes.BevelledBox(new Vector3(0.5f, 0.32f, 0.04f), 0.02f, "BuoyFlag"), Matrix4x4.TRS(new Vector3(0.27f, 1.82f, 0f), Quaternion.identity, Vector3.one)),
            }));
            _buoyRing = Keep(BlockMeshes.Cylinder(0.8f, 0.18f, 16, "BuoyRing"));
            // Mine: a dark sphere with six spikes.
            var spikes = new List<(Mesh, Matrix4x4)> { (sphere, Matrix4x4.Scale(new Vector3(0.6f, 0.55f, 0.6f))) };
            var spike = BlockMeshes.Cone(0.14f, 0.45f, 8, "Spike");
            _meshes.Add(spike);
            foreach (var dir in new[] { Vector3.up, Vector3.left, Vector3.right, Vector3.forward, Vector3.back, (Vector3.up + Vector3.forward).normalized, (Vector3.up + Vector3.back).normalized })
                spikes.Add((spike, Matrix4x4.TRS(dir * 0.45f, Quaternion.FromToRotation(Vector3.up, dir), Vector3.one)));
            _mine = Keep(BlockMeshes.Merge("Mine", spikes));
            // Log: a capped cylinder lying along x with a couple of knots.
            var trunk = BlockMeshes.Cylinder(0.32f, 3.2f, 10, "LogTrunk");
            _meshes.Add(trunk);
            _log = Keep(BlockMeshes.Merge("Log", new List<(Mesh, Matrix4x4)>
            {
                (trunk, Matrix4x4.TRS(new Vector3(-1.6f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f), Vector3.one)),
                (sphere, Matrix4x4.TRS(new Vector3(0.6f, 0.2f, 0.1f), Quaternion.identity, Vector3.one * 0.18f)),
                (sphere, Matrix4x4.TRS(new Vector3(-0.9f, 0.15f, -0.15f), Quaternion.identity, Vector3.one * 0.14f)),
            }));
            // Pike: a long body with a tail fin, nose forward (+z).
            var fin = BlockMeshes.Cone(0.3f, 0.5f, 6, "Fin");
            _meshes.Add(fin);
            _pike = Keep(BlockMeshes.Merge("Pike", new List<(Mesh, Matrix4x4)>
            {
                (sphere, Matrix4x4.Scale(new Vector3(0.24f, 0.22f, 1.1f))),
                (fin, Matrix4x4.TRS(new Vector3(0f, 0f, -1.0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(1f, 1f, 0.3f))),
            }));
            // Whirlpool: a flat disc with its own UVs so the swirl texture maps once across it.
            _whirl = Keep(Disc(1f, 24, "Whirl"));
        }

        private static Mesh Disc(float radius, int segments, string name)
        {
            var verts = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var norms = new Vector3[segments + 1];
            var tris = new int[segments * 3];
            verts[0] = Vector3.zero; uvs[0] = new Vector2(0.5f, 0.5f); norms[0] = Vector3.up;
            for (int i = 0; i < segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                uvs[i + 1] = new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a));
                norms[i + 1] = Vector3.up;
                int j = (i + 1) % segments + 1;
                tris[i * 3] = 0; tris[i * 3 + 1] = j; tris[i * 3 + 2] = i + 1;
            }
            var m = new Mesh { name = name };
            m.vertices = verts; m.uv = uvs; m.normals = norms; m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        private Mesh Keep(Mesh m) { _meshes.Add(m); return m; }

        private void OnDestroy()
        {
            foreach (var m in _meshes) if (m != null) Destroy(m);
            if (_whirlInstance != null) Destroy(_whirlInstance);
        }

        private Renderer Make(Transform parent, string name, Mesh mesh, Material material, Color colour)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            Tint(r, colour);
            return r;
        }

        private void Tint(Renderer r, Color colour)
        {
            _block ??= new MaterialPropertyBlock();
            _block.SetColor(BaseColor, colour);
            r.SetPropertyBlock(_block);
        }

        private void LateUpdate()
        {
            if (_items == null) return;

            float spin = Time.time * 90f;
            for (int i = 0; i < _buoys.Count; i++)
            {
                bool ready = _items.Buoys[i].Cooldown <= 0f;
                if (_buoys[i].gameObject.activeSelf != ready) _buoys[i].gameObject.SetActive(ready);
                float bob = Mathf.Sin(Time.time * 2.1f + i * 1.7f);
                _buoys[i].rotation = Quaternion.Euler(bob * 6f, spin * 0.5f, Mathf.Cos(Time.time * 1.7f + i) * 6f);
                var p = _items.Buoys[i].Position.ToUnity();
                _buoys[i].position = p + Vector3.up * (0.2f + 0.08f * bob);
            }

            var objects = _items.Objects;
            for (int i = 0; i < objects.Length; i++)
            {
                var o = objects[i];
                var t = _objects[i];
                bool visible = o.Active && o.Kind != ItemObjectKind.Kingfisher;
                if (t.gameObject.activeSelf != visible) t.gameObject.SetActive(visible);
                if (!visible)
                {
                    _shownKind[i] = ItemObjectKind.None;
                    continue;
                }

                if (_shownKind[i] != o.Kind)
                {
                    _shownKind[i] = o.Kind;
                    Style(o.Kind, t, _objectFilters[i], _objectRenderers[i]);
                }
                t.position = o.Position.ToUnity();
                if (o.Kind == ItemObjectKind.Pike && o.Velocity.Flat.SqrMagnitude > 1e-4f)
                    t.rotation = Quaternion.LookRotation(o.Velocity.Flat.ToUnity(), Vector3.up);
                else if (o.Kind == ItemObjectKind.Log && o.Velocity.Flat.SqrMagnitude > 1e-4f)
                    t.rotation = Quaternion.LookRotation(o.Velocity.Flat.ToUnity(), Vector3.up) * Quaternion.Euler(0f, 70f, 0f);
                else if (o.Kind == ItemObjectKind.Whirlpool)
                    t.rotation = Quaternion.Euler(0f, -spin * 3f, 0f);
                else if (o.Kind == ItemObjectKind.Mine || o.Kind == ItemObjectKind.ThrownMine)
                    t.rotation = Quaternion.Euler(0f, spin * 0.4f, 0f);
            }
        }

        private void Style(ItemObjectKind kind, Transform t, MeshFilter f, Renderer r)
        {
            r.sharedMaterial = _material;
            t.localScale = Vector3.one;
            switch (kind)
            {
                case ItemObjectKind.Mine:
                case ItemObjectKind.ThrownMine:
                    f.sharedMesh = _mine;
                    Tint(r, new Color(0.24f, 0.30f, 0.28f));
                    break;
                case ItemObjectKind.Log:
                    f.sharedMesh = _log;
                    Tint(r, new Color(0.45f, 0.3f, 0.18f));
                    break;
                case ItemObjectKind.Pike:
                    f.sharedMesh = _pike;
                    Tint(r, new Color(0.55f, 0.62f, 0.66f));
                    break;
                case ItemObjectKind.ThrownWhirl:
                    f.sharedMesh = _ball;
                    t.localScale = Vector3.one * 0.45f;
                    Tint(r, new Color(0.35f, 0.62f, 0.9f));
                    break;
                case ItemObjectKind.Whirlpool:
                    float d = ItemRules.WhirlRadius;
                    f.sharedMesh = _whirl;
                    t.localScale = new Vector3(d, 1f, d);
                    if (_whirlMaterial != null)
                    {
                        if (_whirlInstance == null)
                        {
                            _whirlInstance = new Material(_whirlMaterial);
                            _whirlInstance.SetTexture("_BaseMap", WaterTextures.Swirl);
                        }
                        r.sharedMaterial = _whirlInstance;
                    }
                    Tint(r, new Color(1f, 1f, 1f, 0.85f));
                    break;
                default:
                    f.sharedMesh = _ball;
                    Tint(r, Color.white);
                    break;
            }
        }
    }
}
