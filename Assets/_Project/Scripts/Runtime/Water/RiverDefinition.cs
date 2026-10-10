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

        /// <summary>A 1.5 km test river on a 15 degree grade with a 6 m falls, floodable banks, two eddy rocks, a ledge hole and a wave train.</summary>
        public void ApplyGreyboxDefaults()
        {
            _layers = new WaterLayers
            {
                Waves = new[] { new GerstnerWave { DirectionX = 0.2f, DirectionZ = 1f, Wavelength = 14f, Amplitude = 0.12f, Speed = 3f } },
                Floods = new[] { new FloodPulse { StartTime = 45f, StartDistance = 0f, Speed = 18f, Rise = 0.9f, Length = 60f, FlowMultiplier = 1.4f } },
            };
            _greybox = ProceduralRiverSettings.Default;
            _greybox.Length = 1500f;
            _greybox.Gradient = 0.268f; // 15 degrees: the river runs steeply downhill (402 m over the course, plus the falls)
            // Two meanders: a 40 m swing every 300 m plus a 10 m wobble every 200 m give S-bends and chicanes
            // (heading swings of about 50 degrees, no bend tighter than about 36 m).
            _greybox.MeanderAmplitude = 90f;
            _greybox.MeanderWavelength = 500f;
            _greybox.MeanderAmplitude2 = 15f;
            _greybox.MeanderWavelength2 = 230f;
            _greybox.MeanderPhase2 = 0.9f;
            // The channel breathes: 30 m narrows run fast, 54 m pools run slow (continuity scales the flow).
            _greybox.WidthVariation = 0.3f;
            _greybox.WidthWavelength = 420f;
            _greybox.WidthPhase = 2.0f;
            _greybox.Width = 42f;
            _greybox.WaterfallDistance = 700f;
            _greybox.WaterfallDrop = 6f;
            _greybox.FloodableBank = 12f;
            // A current you can feel: narrows run near 5 m/s, pools under 3, the lane adds 3.5 on top.
            _greybox.BaseFlow = 3.5f;
            _greybox.LaneExtraFlow = 3.5f;
            // Obstacles read as a course: slalom gardens, bridge piers, an island choke before the falls,
            // a wide step and a rapid below it, a boulder gate near the finish. Eddies behind every rock.
            _greybox.Boulders = new[]
            {
                new RiverBoulder { Distance = 220f, Lateral = 6f, Radius = 2f, EddyLength = 12f, EddyFlow = 1.5f },
                // First rock garden: three rocks staggered across the channel.
                new RiverBoulder { Distance = 300f, Lateral = -9f, Radius = 1.6f, EddyLength = 9f, EddyFlow = 1.3f },
                new RiverBoulder { Distance = 322f, Lateral = 3f, Radius = 1.4f, EddyLength = 8f, EddyFlow = 1.3f },
                new RiverBoulder { Distance = 346f, Lateral = 11f, Radius = 1.8f, EddyLength = 10f, EddyFlow = 1.4f },
                new RiverBoulder { Distance = 520f, Lateral = -5f, Radius = 2.2f, EddyLength = 12f, EddyFlow = 1.4f },
                // Bridge piers either side of the arch at 560 m.
                new RiverBoulder { Distance = 557f, Lateral = -9f, Radius = 1.3f, EddyLength = 8f, EddyFlow = 1.3f },
                new RiverBoulder { Distance = 557f, Lateral = 9f, Radius = 1.3f, EddyLength = 8f, EddyFlow = 1.3f },
                // The island: a big rock in the run-in to the falls, go left or right.
                new RiverBoulder { Distance = 640f, Lateral = -2f, Radius = 3.5f, EddyLength = 20f, EddyFlow = 1.7f },
                new RiverBoulder { Distance = 860f, Lateral = 8f, Radius = 2.8f, EddyLength = 16f, EddyFlow = 1.6f },
                // Second rock garden on the far side of the ledge.
                new RiverBoulder { Distance = 1080f, Lateral = -10f, Radius = 1.5f, EddyLength = 9f, EddyFlow = 1.3f },
                new RiverBoulder { Distance = 1100f, Lateral = 2f, Radius = 2f, EddyLength = 11f, EddyFlow = 1.4f },
                new RiverBoulder { Distance = 1122f, Lateral = 12f, Radius = 1.6f, EddyLength = 9f, EddyFlow = 1.3f },
                new RiverBoulder { Distance = 1150f, Lateral = -7f, Radius = 2.5f, EddyLength = 14f, EddyFlow = 1.5f },
                // The gate: two rocks with one clean line between them.
                new RiverBoulder { Distance = 1300f, Lateral = -7f, Radius = 2f, EddyLength = 11f, EddyFlow = 1.4f },
                new RiverBoulder { Distance = 1300f, Lateral = 7f, Radius = 2f, EddyLength = 11f, EddyFlow = 1.4f },
                new RiverBoulder { Distance = 1380f, Lateral = 4f, Radius = 1.8f, EddyLength = 10f, EddyFlow = 1.3f },
                new RiverBoulder { Distance = 1450f, Lateral = -3f, Radius = 2.2f, EddyLength = 12f, EddyFlow = 1.4f },
            };
            // The holes sit to one side; the tongue past each is the clean line. The step below the falls is wide.
            _greybox.Ledges = new[]
            {
                new RiverLedge { Distance = 450f, Drop = 0.4f, HoleLength = 4f, Lateral = -8f, Width = 10f },
                new RiverLedge { Distance = 800f, Drop = 0.35f, HoleLength = 4f, Lateral = 0f, Width = 14f },
                new RiverLedge { Distance = 1020f, Drop = 0.5f, HoleLength = 5f, Lateral = 7f, Width = 12f },
            };
            _greybox.WaveTrains = new[]
            {
                new StandingWaveTrain { Distance = 740f, Count = 4, Wavelength = 10f, Height = 0.6f },
                new StandingWaveTrain { Distance = 950f, Count = 5, Wavelength = 12f, Height = 0.7f },
                new StandingWaveTrain { Distance = 1250f, Count = 4, Wavelength = 14f, Height = 0.9f },
            };
        }
    }
}
