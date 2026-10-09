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

        /// <summary>World position of a boulder's centre on the water surface.</summary>
        public Vector3 BoulderPosition(in RiverBoulder b)
        {
            float d = b.Distance;
            float cx = ProceduralRiver.CentreX(_greybox, d);
            // The builder measures across the channel as (x - centre) * tangentZ, so undo that here.
            float slope = (ProceduralRiver.CentreX(_greybox, d + 0.5f) - ProceduralRiver.CentreX(_greybox, d - 0.5f));
            float tZ = 1f / Mathf.Sqrt(1f + slope * slope);
            return new Vector3(cx + b.Lateral / tZ, ProceduralRiver.SurfaceAt(_greybox, d), d);
        }

        /// <summary>A 1.5 km test river with a 6 m falls, floodable banks, two eddy rocks, a ledge hole and a wave train.</summary>
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
            _greybox.Boulders = new[]
            {
                new RiverBoulder { Distance = 220f, Lateral = 6f, Radius = 2f, EddyLength = 12f, EddyFlow = 1.5f },
                new RiverBoulder { Distance = 1150f, Lateral = -7f, Radius = 2.5f, EddyLength = 14f, EddyFlow = 1.5f },
            };
            // The hole sits on river left; the tongue down the right is the clean line.
            _greybox.Ledges = new[] { new RiverLedge { Distance = 450f, Drop = 0.4f, HoleLength = 4f, Lateral = -8f, Width = 10f } };
            _greybox.WaveTrains = new[] { new StandingWaveTrain { Distance = 950f, Count = 5, Wavelength = 12f, Height = 0.7f } };
        }
    }
}
