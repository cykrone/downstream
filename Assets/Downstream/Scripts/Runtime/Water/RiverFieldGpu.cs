using System;
using Downstream.Core.Water;
using UnityEngine;

namespace Downstream.Water
{
    /// <summary>
    /// Uploads a <see cref="RiverWater"/> to the GPU for the water shader: the River Field's tiles as
    /// two <see cref="Texture2DArray"/>s (one slice per existing tile) plus a tile-index texture, and
    /// the analytic layers as shader globals. The shader side is Assets/Downstream/Shaders/RiverField.hlsl,
    /// which blends the same four texels in float so it returns the physics' numbers.
    /// Textures are 32-bit float: no half rounding, no filtering, no mipmaps.
    /// </summary>
    public sealed class RiverFieldGpu : IDisposable
    {
        public const int MaxWaves = 4;
        public const int MaxFloods = 4;

        private static readonly int TilesAId = Shader.PropertyToID("_RiverTilesA");
        private static readonly int TilesBId = Shader.PropertyToID("_RiverTilesB");
        private static readonly int TileIndexId = Shader.PropertyToID("_RiverTileIndex");
        private static readonly int FieldParamsId = Shader.PropertyToID("_RiverFieldParams");
        private static readonly int FieldTilesId = Shader.PropertyToID("_RiverFieldTiles");
        private static readonly int WaveAId = Shader.PropertyToID("_RiverWaveA");
        private static readonly int WaveBId = Shader.PropertyToID("_RiverWaveB");
        private static readonly int FloodAId = Shader.PropertyToID("_RiverFloodA");
        private static readonly int FloodBId = Shader.PropertyToID("_RiverFloodB");
        private static readonly int TideAId = Shader.PropertyToID("_RiverTideA");
        private static readonly int TideBId = Shader.PropertyToID("_RiverTideB");
        private static readonly int WaveCountId = Shader.PropertyToID("_RiverWaveCount");
        private static readonly int FloodCountId = Shader.PropertyToID("_RiverFloodCount");
        private static readonly int RaceTimeId = Shader.PropertyToID("_RiverRaceTime");

        private readonly Vector4[] _waveA = new Vector4[MaxWaves];
        private readonly Vector4[] _waveB = new Vector4[MaxWaves];
        private readonly Vector4[] _floodA = new Vector4[MaxFloods];
        private readonly Vector4[] _floodB = new Vector4[MaxFloods];

        public Texture2DArray TilesA { get; private set; }
        public Texture2DArray TilesB { get; private set; }
        public Texture2D TileIndex { get; private set; }
        public RiverWater Water { get; private set; }
        public int TileCount { get; private set; }

        /// <summary>Builds the textures for <paramref name="water"/> and binds everything as shader globals.</summary>
        public void Upload(RiverWater water)
        {
            Release();
            Water = water ?? throw new ArgumentNullException(nameof(water));
            var field = water.Field;
            int texels = RiverField.TileTexels;

            // Count and number the tiles that exist, in slot order.
            var slice = new float[field.TilesX * field.TilesZ];
            int count = 0;
            for (int tz = 0; tz < field.TilesZ; tz++)
            for (int tx = 0; tx < field.TilesX; tx++)
                slice[tz * field.TilesX + tx] = field.TryGetTile(tx, tz, out _) ? count++ : -1f;
            TileCount = count;

            TileIndex = new Texture2D(field.TilesX, field.TilesZ, TextureFormat.RFloat, false, true)
            {
                name = "RiverTileIndex", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            TileIndex.SetPixelData(slice, 0);
            TileIndex.Apply(false, true);

            int slices = Mathf.Max(count, 1);
            TilesA = new Texture2DArray(texels, texels, slices, TextureFormat.RGBAFloat, false, true)
            {
                name = "RiverTilesA", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            TilesB = new Texture2DArray(texels, texels, slices, TextureFormat.RGBAFloat, false, true)
            {
                name = "RiverTilesB", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };

            int perTile = texels * texels;
            var a = new float[perTile * 4];
            var b = new float[perTile * 4];
            int s = 0;
            for (int tz = 0; tz < field.TilesZ; tz++)
            for (int tx = 0; tx < field.TilesX; tx++)
            {
                if (!field.TryGetTile(tx, tz, out var tile)) continue;
                for (int i = 0; i < perTile; i++)
                {
                    a[i * 4 + 0] = tile.FlowX[i];
                    a[i * 4 + 1] = tile.FlowZ[i];
                    a[i * 4 + 2] = tile.SurfaceHeight[i];
                    a[i * 4 + 3] = tile.BedHeight[i];
                    b[i * 4 + 0] = tile.RiverDistance[i];
                    b[i * 4 + 1] = tile.Features[i];
                    b[i * 4 + 2] = tile.LaneOffset[i];
                    b[i * 4 + 3] = 0f;
                }
                TilesA.SetPixelData(a, 0, s);
                TilesB.SetPixelData(b, 0, s);
                s++;
            }
            if (count == 0)
            {
                // An empty field still needs valid textures bound.
                Array.Clear(a, 0, a.Length);
                Array.Clear(b, 0, b.Length);
                for (int i = 0; i < perTile; i++) a[i * 4 + 3] = RiverField.UnwrittenBed;
                TilesA.SetPixelData(a, 0, 0);
                TilesB.SetPixelData(b, 0, 0);
            }
            TilesA.Apply(false, true);
            TilesB.Apply(false, true);

            Bind();
        }

        /// <summary>Re-sends every global. Called by <see cref="Upload"/>; call again after a shader reload.</summary>
        public void Bind()
        {
            if (Water == null) return;
            var field = Water.Field;
            Shader.SetGlobalTexture(TilesAId, TilesA);
            Shader.SetGlobalTexture(TilesBId, TilesB);
            Shader.SetGlobalTexture(TileIndexId, TileIndex);
            Shader.SetGlobalVector(FieldParamsId, new Vector4(field.OriginX, field.OriginZ, field.CellSize, 1f / field.CellSize));
            Shader.SetGlobalVector(FieldTilesId, new Vector4(field.TilesX, field.TilesZ, TileCount, 0f));

            var layers = Water.Layers;
            int waves = Mathf.Min(layers.Waves.Length, MaxWaves);
            for (int i = 0; i < MaxWaves; i++)
            {
                if (i < waves)
                {
                    var w = layers.Waves[i];
                    _waveA[i] = new Vector4(w.DirectionX, w.DirectionZ, w.Wavelength, w.Amplitude);
                    _waveB[i] = new Vector4(w.Speed, 0f, 0f, 0f);
                }
                else
                {
                    _waveA[i] = Vector4.zero;
                    _waveB[i] = Vector4.zero;
                }
            }
            int floods = Mathf.Min(layers.Floods.Length, MaxFloods);
            for (int i = 0; i < MaxFloods; i++)
            {
                if (i < floods)
                {
                    var f = layers.Floods[i];
                    _floodA[i] = new Vector4(f.StartTime, f.StartDistance, f.Speed, f.Rise);
                    _floodB[i] = new Vector4(f.Length, f.FlowMultiplier, 0f, 0f);
                }
                else
                {
                    _floodA[i] = Vector4.zero;
                    _floodB[i] = Vector4.zero;
                }
            }
            var tide = layers.Tide;
            Shader.SetGlobalVectorArray(WaveAId, _waveA);
            Shader.SetGlobalVectorArray(WaveBId, _waveB);
            Shader.SetGlobalVectorArray(FloodAId, _floodA);
            Shader.SetGlobalVectorArray(FloodBId, _floodB);
            Shader.SetGlobalVector(TideAId, new Vector4(tide.Enabled ? 1f : 0f, tide.TurnTime, tide.TransitionSeconds, tide.LowLevel));
            Shader.SetGlobalVector(TideBId, new Vector4(tide.HighLevel, tide.EbbFlowScale, tide.FloodFlowScale, 0f));
            Shader.SetGlobalInteger(WaveCountId, waves);
            Shader.SetGlobalInteger(FloodCountId, floods);
            if (waves < layers.Waves.Length || floods < layers.Floods.Length)
                Debug.LogWarning($"RiverFieldGpu: the shader reads at most {MaxWaves} waves and {MaxFloods} floods; extra layers are ignored on the GPU.");
        }

        /// <summary>Race time the shader evaluates the layers at. Set every rendered frame.</summary>
        public static void SetRaceTime(float raceTime) => Shader.SetGlobalFloat(RaceTimeId, raceTime);

        public void Release()
        {
            Destroy(TilesA);
            Destroy(TilesB);
            Destroy(TileIndex);
            TilesA = null;
            TilesB = null;
            TileIndex = null;
            Water = null;
            TileCount = 0;
        }

        public void Dispose() => Release();

        private static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
