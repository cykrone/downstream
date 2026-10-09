using System.Collections.Generic;
using Downstream.Core.Items;
using UnityEngine;

namespace Downstream.Items
{
    /// <summary>
    /// Greybox presentation of items: buoys, floating mines and logs, pikes, thrown items and whirlpools
    /// as coloured primitives, pooled and placed from the sim every frame. Holds no gameplay state.
    /// Placeholder until the art pass; every shape here is a provenance-exempt Unity primitive.
    /// </summary>
    public sealed class ItemWorldView : MonoBehaviour
    {
        [SerializeField] private Material _material;

        private ItemSystem _items;
        private readonly List<Transform> _buoys = new List<Transform>();
        private readonly List<Renderer> _buoyRenderers = new List<Renderer>();
        private Transform[] _objects;
        private Renderer[] _objectRenderers;
        private ItemObjectKind[] _shownKind;
        private MaterialPropertyBlock _block;

        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public void Bind(ItemSystem items)
        {
            _items = items;
            _block ??= new MaterialPropertyBlock();
            foreach (var b in _buoys) Destroy(b.gameObject);
            _buoys.Clear();
            _buoyRenderers.Clear();
            for (int i = 0; i < items.Buoys.Length; i++)
            {
                var t = Make(PrimitiveType.Cube, $"Item Buoy {i}", new Vector3(1.2f, 1.2f, 1.2f), new Color(1f, 0.78f, 0.2f));
                t.position = items.Buoys[i].Position.ToUnity() + Vector3.up * 0.9f;
                _buoys.Add(t);
                _buoyRenderers.Add(t.GetComponent<Renderer>());
            }

            if (_objects == null)
            {
                _objects = new Transform[ItemSystem.MaxObjects];
                _objectRenderers = new Renderer[ItemSystem.MaxObjects];
                _shownKind = new ItemObjectKind[ItemSystem.MaxObjects];
                for (int i = 0; i < _objects.Length; i++)
                {
                    _objects[i] = Make(PrimitiveType.Sphere, $"Item {i}", Vector3.one, Color.white);
                    _objectRenderers[i] = _objects[i].GetComponent<Renderer>();
                    _objects[i].gameObject.SetActive(false);
                }
            }
            for (int i = 0; i < _shownKind.Length; i++) _shownKind[i] = ItemObjectKind.None;
        }

        private Transform Make(PrimitiveType type, string name, Vector3 scale, Color colour)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            DestroyImmediate(go.GetComponent<Collider>()); // the sim owns all item collision; gone before any query runs
            go.transform.SetParent(transform, false);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (_material != null) r.sharedMaterial = _material;
            Tint(r, colour);
            return go.transform;
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
                _buoys[i].rotation = Quaternion.Euler(20f, spin, 20f);
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
                    Style(o.Kind, t, _objectRenderers[i]);
                }
                t.position = o.Position.ToUnity();
                if (o.Kind == ItemObjectKind.Pike && o.Velocity.Flat.SqrMagnitude > 1e-4f)
                    t.rotation = Quaternion.LookRotation(o.Velocity.Flat.ToUnity(), Vector3.up);
                else if (o.Kind == ItemObjectKind.Whirlpool)
                    t.rotation = Quaternion.Euler(0f, -spin * 3f, 0f);
            }
        }

        private void Style(ItemObjectKind kind, Transform t, Renderer r)
        {
            switch (kind)
            {
                case ItemObjectKind.Mine:
                case ItemObjectKind.ThrownMine:
                    t.localScale = new Vector3(1.4f, 0.5f, 1.4f);
                    Tint(r, new Color(0.35f, 0.7f, 0.3f));
                    break;
                case ItemObjectKind.Log:
                    t.localScale = new Vector3(3.2f, 0.6f, 0.6f);
                    Tint(r, new Color(0.45f, 0.3f, 0.18f));
                    break;
                case ItemObjectKind.Pike:
                    t.localScale = new Vector3(0.5f, 0.5f, 2.2f);
                    Tint(r, new Color(0.6f, 0.65f, 0.7f));
                    break;
                case ItemObjectKind.ThrownWhirl:
                    t.localScale = Vector3.one;
                    Tint(r, new Color(0.2f, 0.45f, 0.75f));
                    break;
                case ItemObjectKind.Whirlpool:
                    float d = ItemRules.WhirlRadius * 2f;
                    t.localScale = new Vector3(d, 0.1f, d);
                    Tint(r, new Color(0.2f, 0.45f, 0.75f));
                    break;
            }
        }
    }
}
