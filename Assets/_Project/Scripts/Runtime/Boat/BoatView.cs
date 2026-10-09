using Downstream.Core.Boat;
using UnityEngine;

namespace Downstream.Boat
{
    /// <summary>
    /// Presents one simulated boat. Holds no gameplay state: every frame it is handed an
    /// interpolated <see cref="BoatState"/> and places itself there. Audio, wake VFX and the
    /// pilot animation hang off the events it receives.
    /// </summary>
    public sealed class BoatView : MonoBehaviour
    {
        [SerializeField] private Transform _hull;
        [SerializeField] private Renderer[] _livery;
        [SerializeField] private BoatEffects _effects;

        /// <summary>Hull colours by boat index: a shipped-game livery set, distinct at a glance.</summary>
        private static readonly Color[] Liveries =
        {
            new Color(0.86f, 0.27f, 0.20f), // tomato
            new Color(0.20f, 0.46f, 0.86f), // cobalt
            new Color(0.98f, 0.74f, 0.18f), // sunflower
            new Color(0.22f, 0.70f, 0.52f), // jade
            new Color(0.62f, 0.36f, 0.80f), // violet
            new Color(0.96f, 0.52f, 0.20f), // tangerine
            new Color(0.18f, 0.66f, 0.72f), // teal
            new Color(0.92f, 0.44f, 0.62f), // rose
        };
        private Material _liveryMaterial;
        private int _boatIndex;

        public BoatState State { get; private set; }
        /// <summary>The river the boat is on, and the interpolated race time it is drawn at (set by the director).</summary>
        public Core.Water.RiverWater Water { get; set; }
        public float RaceTime { get; set; }
        public BoatEvents LastEvents { get; private set; }
        public int BoatIndex
        {
            get => _boatIndex;
            set { _boatIndex = value; ApplyLivery(); }
        }

        /// <summary>Hull and helmet take the boat's livery colour: one material instance per boat.</summary>
        private void ApplyLivery()
        {
            if (_livery == null || _livery.Length == 0 || _livery[0] == null) return;
            var side = Liveries[_boatIndex % Liveries.Length];
            if (_liveryMaterial == null) _liveryMaterial = new Material(_livery[0].sharedMaterial);
            _liveryMaterial.SetColor("_BaseColor", side);
            if (_liveryMaterial.HasProperty("_TopColor"))
            {
                _liveryMaterial.SetColor("_TopColor", Color.Lerp(side, Color.white, 0.32f));
                _liveryMaterial.SetColor("_ShadeColor", side * 0.55f);
            }
            foreach (var r in _livery) if (r != null) r.sharedMaterial = _liveryMaterial;
        }

        private void OnDestroy()
        {
            if (_liveryMaterial != null) Destroy(_liveryMaterial);
        }

        public void Present(in BoatState state)
        {
            State = state;
            transform.SetPositionAndRotation(state.Position.ToUnity(), UnityConversions.BoatRotation(state));
            if (_effects != null) _effects.Apply(state, Water, RaceTime);
        }

        public void OnSimEvents(BoatEvents events)
        {
            LastEvents = events;
            // Hook point for audio, VFX and HUD: landing slaps, drift sparks by tier, boost trails.
        }

        private void Reset()
        {
            _hull = transform;
            _effects = GetComponent<BoatEffects>();
        }
    }
}
