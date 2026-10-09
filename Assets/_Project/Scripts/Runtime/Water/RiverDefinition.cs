using Downstream.Core.Water;
using UnityEngine;

namespace Downstream.Water
{
    /// <summary>
    /// One track's water: today a procedural greybox river plus its analytic layers. When the
    /// River Field baker lands, this asset will also reference the baked tiles, and
    /// <see cref="CreateWater"/> will load those instead of generating a channel.
    /// </summary>
    [CreateAssetMenu(menuName = "Downstream/River Definition", fileName = "RiverDefinition")]
    public sealed class RiverDefinition : ScriptableObject
    {
        [SerializeField] private ProceduralRiverSettings _greybox = ProceduralRiverSettings.Default;
        [SerializeField] private WaterLayers _layers = new WaterLayers();
        [Tooltip("Spacing of the race centreline samples, metres.")]
        [SerializeField] private float _centrelineSpacing = 10f;

        public ProceduralRiverSettings Greybox => _greybox;
        public WaterLayers Layers => _layers;
        public float CentrelineSpacing => _centrelineSpacing;

        public RiverWater CreateWater() => new RiverWater(ProceduralRiver.Build(_greybox), _layers);

        /// <summary>World-space centreline used for race progress, respawn and AI.</summary>
        public Core.Math.SimVec3[] SampleCentreline()
        {
            int count = Mathf.Max(2, Mathf.CeilToInt(_greybox.Length / _centrelineSpacing) + 1);
            var points = new Core.Math.SimVec3[count];
            for (int i = 0; i < count; i++)
            {
                float d = Mathf.Min(i * _centrelineSpacing, _greybox.Length);
                points[i] = new Core.Math.SimVec3(ProceduralRiver.CentreX(_greybox, d), ProceduralRiver.SurfaceAt(_greybox, d), d);
            }
            return points;
        }

        private void Reset() => ApplyGreyboxDefaults();

        /// <summary>A 1.5 km test river with a 6 m falls and floodable banks.</summary>
        public void ApplyGreyboxDefaults()
        {
            _layers = new WaterLayers
            {
                Waves = new[] { new GerstnerWave { DirectionX = 0.2f, DirectionZ = 1f, Wavelength = 14f, Amplitude = 0.12f, Speed = 3f } },
                Floods = new[] { new FloodPulse { StartTime = 45f, StartDistance = 0f, Speed = 18f, Rise = 0.9f, Length = 60f, FlowMultiplier = 1.4f } },
            };
            _greybox = ProceduralRiverSettings.Default;
            _greybox.Length = 1500f;
            _greybox.WaterfallDistance = 700f;
            _greybox.WaterfallDrop = 6f;
            _greybox.FloodableBank = 12f;
        }
    }
}
