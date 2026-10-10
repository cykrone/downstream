using System;
using System.Collections.Generic;
using Downstream.Core.Math;

namespace Downstream.Core.Water
{
    /// <summary>
    /// One 64 m square of baked river data at 0.5 m per texel, stored as separate channels.
    /// The same tiles are uploaded to the GPU (as a Texture2DArray plus an indirection texture)
    /// so the water shader and the physics read identical numbers.
    /// </summary>
    [Serializable]
    public sealed class RiverFieldTile
    {
        public readonly float[] FlowX;
        public readonly float[] FlowZ;
        public readonly float[] SurfaceHeight;
        public readonly float[] BedHeight;
        public readonly float[] RiverDistance;
        public readonly byte[] Features;

        public RiverFieldTile(int texelCount)
        {
            FlowX = new float[texelCount];
            FlowZ = new float[texelCount];
            SurfaceHeight = new float[texelCount];
            BedHeight = new float[texelCount];
            RiverDistance = new float[texelCount];
            Features = new byte[texelCount];
            // Untouched texels are dry land: the bed sits far above any surface.
            for (int i = 0; i < texelCount; i++)
            {
                SurfaceHeight[i] = -10000f;
                BedHeight[i] = 10000f;
            }
        }
    }

    /// <summary>Static (time-independent) data at one point, before the analytic layers are added.</summary>
    public struct StaticWaterSample
    {
        public bool HasData;
        public float SurfaceHeight;
        public float BedHeight;
        public float FlowX;
        public float FlowZ;
        public float RiverDistance;
        public WaterFeature Features;
    }

    /// <summary>
    /// The baked River Field for one track: sparse tiles covering only water and floodable land.
    /// Sampling is bilinear and allocation-free. Texels outside any tile count as dry land.
    /// </summary>
    public sealed class RiverField
    {
        public const int TileTexels = 128;
        public const float DefaultCellSize = 0.5f;
        private const int TexelsPerTile = TileTexels * TileTexels;
        public const float UnwrittenBed = 9999f;

        public float OriginX { get; }
        public float OriginZ { get; }
        public float CellSize { get; }
        public int TilesX { get; }
        public int TilesZ { get; }
        public float TileSize => CellSize * TileTexels;

        private readonly int[] _tileIndex;
        private readonly List<RiverFieldTile> _tiles = new List<RiverFieldTile>();

        public int TileCount => _tiles.Count;

        public RiverField(float originX, float originZ, int tilesX, int tilesZ, float cellSize = DefaultCellSize)
        {
            if (tilesX <= 0 || tilesZ <= 0) throw new ArgumentOutOfRangeException(nameof(tilesX));
            OriginX = originX;
            OriginZ = originZ;
            TilesX = tilesX;
            TilesZ = tilesZ;
            CellSize = cellSize;
            _tileIndex = new int[tilesX * tilesZ];
            for (int i = 0; i < _tileIndex.Length; i++) _tileIndex[i] = -1;
        }

        /// <summary>Approximate CPU memory held by tiles, in bytes.</summary>
        public long MemoryBytes => (long)_tiles.Count * TexelsPerTile * (5 * sizeof(float) + 1);

        public bool TryGetTile(int tx, int tz, out RiverFieldTile tile)
        {
            tile = null;
            if (tx < 0 || tz < 0 || tx >= TilesX || tz >= TilesZ) return false;
            int idx = _tileIndex[tz * TilesX + tx];
            if (idx < 0) return false;
            tile = _tiles[idx];
            return true;
        }

        public RiverFieldTile GetOrCreateTile(int tx, int tz)
        {
            if (tx < 0 || tz < 0 || tx >= TilesX || tz >= TilesZ) throw new ArgumentOutOfRangeException(nameof(tx));
            int slot = tz * TilesX + tx;
            if (_tileIndex[slot] >= 0) return _tiles[_tileIndex[slot]];
            var tile = new RiverFieldTile(TexelsPerTile);
            _tileIndex[slot] = _tiles.Count;
            _tiles.Add(tile);
            return tile;
        }

        /// <summary>Writes one texel, creating its tile if needed. Used by the baker and by tests.</summary>
        public void SetTexel(int ix, int iz, float surfaceHeight, float bedHeight, float flowX, float flowZ,
            float riverDistance, WaterFeature features)
        {
            int tx = ix / TileTexels, tz = iz / TileTexels;
            var tile = GetOrCreateTile(tx, tz);
            int i = (iz - tz * TileTexels) * TileTexels + (ix - tx * TileTexels);
            tile.SurfaceHeight[i] = surfaceHeight;
            tile.BedHeight[i] = bedHeight;
            tile.FlowX[i] = flowX;
            tile.FlowZ[i] = flowZ;
            tile.RiverDistance[i] = riverDistance;
            tile.Features[i] = (byte)features;
        }

        /// <summary>World position of a texel centre.</summary>
        public void TexelCentre(int ix, int iz, out float x, out float z)
        {
            x = OriginX + (ix + 0.5f) * CellSize;
            z = OriginZ + (iz + 0.5f) * CellSize;
        }

        public int TexelCountX => TilesX * TileTexels;
        public int TexelCountZ => TilesZ * TileTexels;

        private bool TryTexel(int ix, int iz, out RiverFieldTile tile, out int index)
        {
            index = 0;
            tile = null;
            if (ix < 0 || iz < 0) return false;
            int tx = ix / TileTexels, tz = iz / TileTexels;
            if (!TryGetTile(tx, tz, out tile)) return false;
            index = (iz - tz * TileTexels) * TileTexels + (ix - tx * TileTexels);
            return true;
        }

        /// <summary>
        /// Bilinear sample of the static channels. Missing texels are excluded from the blend;
        /// the point has data only if texels carrying at least half the weight exist.
        /// Feature flags come from the nearest texel (flags do not blend).
        /// </summary>
        public StaticWaterSample SampleStatic(float x, float z)
        {
            float fx = (x - OriginX) / CellSize - 0.5f;
            float fz = (z - OriginZ) / CellSize - 0.5f;
            int ix = SimMath.FloorToInt(fx);
            int iz = SimMath.FloorToInt(fz);
            float tx = fx - ix;
            float tz = fz - iz;

            var s = new StaticWaterSample();
            float wSum = 0f;
            float bestW = -1f;
            for (int c = 0; c < 4; c++)
            {
                int dx = c & 1, dz = c >> 1;
                float w = (dx == 0 ? 1f - tx : tx) * (dz == 0 ? 1f - tz : tz);
                if (!TryTexel(ix + dx, iz + dz, out var tile, out int i)) continue;
                if (tile.BedHeight[i] >= UnwrittenBed) continue;
                s.SurfaceHeight += tile.SurfaceHeight[i] * w;
                s.BedHeight += tile.BedHeight[i] * w;
                s.FlowX += tile.FlowX[i] * w;
                s.FlowZ += tile.FlowZ[i] * w;
                s.RiverDistance += tile.RiverDistance[i] * w;
                if (w > bestW)
                {
                    bestW = w;
                    s.Features = (WaterFeature)tile.Features[i];
                }
                wSum += w;
            }

            if (wSum < 0.5f) return new StaticWaterSample { HasData = false };
            float inv = 1f / wSum;
            s.SurfaceHeight *= inv;
            s.BedHeight *= inv;
            s.FlowX *= inv;
            s.FlowZ *= inv;
            s.RiverDistance *= inv;
            s.HasData = true;
            return s;
        }
    }
}
