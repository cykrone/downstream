#ifndef DOWNSTREAM_RIVER_FIELD_INCLUDED
#define DOWNSTREAM_RIVER_FIELD_INCLUDED

// GPU port of Downstream.Core.Water: RiverField.SampleStatic, WaterLayers and RiverWater.Sample.
// It reads the same tiles the physics reads (uploaded by RiverFieldGpu) and does its own 4-texel
// blend in float, so the surface the player sees is the surface that pushes the boat.
// Keep every formula identical to the C# version; RiverFieldGpuParityTests checks it.

#define RIVER_TILE_TEXELS 128
#define RIVER_UNWRITTEN_BED 9999.0
#define RIVER_MAX_WAVES 4
#define RIVER_MAX_FLOODS 4

#define RIVER_FEATURE_CURRENT_LANE 1
#define RIVER_FEATURE_EDDY 2
#define RIVER_FEATURE_HOLE 4
#define RIVER_FEATURE_CREST 8
#define RIVER_FEATURE_WATERFALL_LIP 16
#define RIVER_FEATURE_FLOODABLE 32
#define RIVER_FEATURE_SHALLOWS 64

// Tile channels. A: flowX, flowZ, surface height, bed height. B: river distance, feature bits.
Texture2DArray<float4> _RiverTilesA;
Texture2DArray<float2> _RiverTilesB;
// Tile slot (tx, tz) -> slice index, or a negative value where the field has no tile.
Texture2D<float> _RiverTileIndex;

float4 _RiverFieldParams; // originX, originZ, cellSize, 1/cellSize
float4 _RiverFieldTiles;  // tilesX, tilesZ, tileCount, 0

float4 _RiverWaveA[RIVER_MAX_WAVES];  // directionX, directionZ, wavelength, amplitude
float4 _RiverWaveB[RIVER_MAX_WAVES];  // speed, 0, 0, 0
float4 _RiverFloodA[RIVER_MAX_FLOODS]; // startTime, startDistance, speed, rise
float4 _RiverFloodB[RIVER_MAX_FLOODS]; // length, flowMultiplier, 0, 0
float4 _RiverTideA; // enabled, turnTime, transitionSeconds, lowLevel
float4 _RiverTideB; // highLevel, ebbFlowScale, floodFlowScale, 0
int _RiverWaveCount;
int _RiverFloodCount;
float _RiverRaceTime;
// Mean downhill grade of the river (rise per metre); whitewater is judged on slope in excess of it.
float _RiverBaseSlope;

struct RiverStaticSample
{
    bool hasData;
    float surfaceHeight;
    float bedHeight;
    float2 flow;
    float riverDistance;
    int features;    // nearest texel, as the CPU returns it
    int featuresAny; // OR of every contributing texel: seams between features (eddy lines) live here
    int featuresAll; // AND of every contributing texel
    float eddyWeight; // bilinear share of eddy texels: 0.5 on the eddy line, so seams draw smooth
};

struct RiverWaterSample
{
    bool isWet;
    float surfaceHeight;
    float depth;
    float2 flow;
    float3 normal;
    float riverDistance;
    int features;
    int featuresAny;
    int featuresAll;
    float eddyWeight;
    float2 staticSlope; // slope of the baked surface alone (waterfalls, rapids), without waves
};

// Fetches one texel. Returns false outside the field, outside any tile, or on an unwritten texel.
bool RiverFetchTexel(int ix, int iz, out float4 a, out float2 b)
{
    a = 0;
    b = 0;
    if (ix < 0 || iz < 0) return false;
    // Non-negative here, so unsigned division (cheaper on GPUs) gives the CPU's result.
    int tx = (int)((uint)ix / (uint)RIVER_TILE_TEXELS);
    int tz = (int)((uint)iz / (uint)RIVER_TILE_TEXELS);
    if (tx >= (int)_RiverFieldTiles.x || tz >= (int)_RiverFieldTiles.y) return false;
    float slice = _RiverTileIndex.Load(int3(tx, tz, 0));
    if (slice < 0) return false;
    int2 local = int2(ix - tx * RIVER_TILE_TEXELS, iz - tz * RIVER_TILE_TEXELS);
    a = _RiverTilesA.Load(int4(local, (int)slice, 0));
    if (a.w >= RIVER_UNWRITTEN_BED) return false;
    b = _RiverTilesB.Load(int4(local, (int)slice, 0));
    return true;
}

// RiverField.SampleStatic: bilinear over the texels that exist, flags from the heaviest texel,
// no data unless texels carrying at least half the weight exist.
RiverStaticSample SampleRiverStatic(float2 xz)
{
    float fx = (xz.x - _RiverFieldParams.x) / _RiverFieldParams.z - 0.5;
    float fz = (xz.y - _RiverFieldParams.y) / _RiverFieldParams.z - 0.5;
    int ix = (int)floor(fx);
    int iz = (int)floor(fz);
    float tx = fx - ix;
    float tz = fz - iz;

    RiverStaticSample s = (RiverStaticSample)0;
    s.featuresAll = 0xFF;
    float wSum = 0.0;
    float bestW = -1.0;
    [unroll]
    for (int c = 0; c < 4; c++)
    {
        int dx = c & 1;
        int dz = c >> 1;
        float w = (dx == 0 ? 1.0 - tx : tx) * (dz == 0 ? 1.0 - tz : tz);
        float4 a;
        float2 b;
        if (!RiverFetchTexel(ix + dx, iz + dz, a, b)) continue;
        s.surfaceHeight += a.z * w;
        s.bedHeight += a.w * w;
        s.flow += a.xy * w;
        s.riverDistance += b.x * w;
        int f = (int)round(b.y);
        s.featuresAny |= f;
        s.featuresAll &= f;
        if ((f & RIVER_FEATURE_EDDY) != 0) s.eddyWeight += w;
        if (w > bestW)
        {
            bestW = w;
            s.features = f;
        }
        wSum += w;
    }

    if (wSum < 0.5)
    {
        RiverStaticSample none = (RiverStaticSample)0;
        return none;
    }
    float inv = 1.0 / wSum;
    s.surfaceHeight *= inv;
    s.bedHeight *= inv;
    s.flow *= inv;
    s.riverDistance *= inv;
    s.eddyWeight *= inv;
    s.hasData = true;
    return s;
}

float RiverStaticSurfaceOr(float2 xz, float fallback)
{
    RiverStaticSample s = SampleRiverStatic(xz);
    return s.hasData ? s.surfaceHeight : fallback;
}

// FloodPulse.Strength
float RiverFloodStrength(float4 a, float4 b, float riverDistance, float t)
{
    float startTime = a.x, startDistance = a.y, speed = a.z;
    float len = b.x;
    if (t < startTime || len <= 0.0) return 0.0;
    float front = startDistance + speed * (t - startTime);
    float behind = front - riverDistance;
    if (behind < 0.0) return 0.0;
    if (behind > len) return 1.0;
    float u = behind / len;
    return u * u * (3.0 - 2.0 * u);
}

// TideClock.FloodFraction
float RiverTideFloodFraction(float t)
{
    if (_RiverTideA.x <= 0.0) return 0.0;
    float half_ = max(_RiverTideA.z, 0.001) * 0.5;
    float u = saturate((t - (_RiverTideA.y - half_)) / (2.0 * half_));
    return u * u * (3.0 - 2.0 * u);
}

// The time-dependent level rise and flow scale at a river distance (floods and tide).
void RiverLayersAt(float riverDistance, float t, out float rise, out float flowScale)
{
    rise = 0.0;
    flowScale = 1.0;
    for (int i = 0; i < _RiverFloodCount; i++)
    {
        float k = RiverFloodStrength(_RiverFloodA[i], _RiverFloodB[i], riverDistance, t);
        if (k <= 0.0) continue;
        rise += _RiverFloodA[i].w * k;
        flowScale *= lerp(1.0, _RiverFloodB[i].y, k);
    }
    if (_RiverTideA.x > 0.0)
    {
        float f = RiverTideFloodFraction(t);
        rise += lerp(_RiverTideA.w, _RiverTideB.x, f);
        flowScale *= lerp(_RiverTideB.y, _RiverTideB.z, f);
    }
}

// GerstnerWave.Evaluate summed over the layer's waves: height and slope only, as gameplay uses.
void RiverWavesAt(float2 xz, float t, out float height, out float2 slope)
{
    height = 0.0;
    slope = 0.0;
    for (int i = 0; i < _RiverWaveCount; i++)
    {
        float4 w = _RiverWaveA[i];
        float len = sqrt(w.x * w.x + w.y * w.y);
        if (len < 1e-6 || w.z <= 0.0) continue;
        float dx = w.x / len, dz = w.y / len;
        float k = 2.0 * PI / w.z;
        float phase = k * (dx * xz.x + dz * xz.y - _RiverWaveB[i].x * t);
        height += w.w * sin(phase);
        float d = w.w * k * cos(phase);
        slope += float2(d * dx, d * dz);
    }
}

// RiverWater.Sample
RiverWaterSample SampleRiverWater(float2 xz, float t)
{
    RiverWaterSample r = (RiverWaterSample)0;
    r.normal = float3(0, 1, 0);
    RiverStaticSample s = SampleRiverStatic(xz);
    if (!s.hasData) return r;

    float rise, flowScale;
    RiverLayersAt(s.riverDistance, t, rise, flowScale);
    float waveH;
    float2 waveSlope;
    RiverWavesAt(xz, t, waveH, waveSlope);

    float surface = s.surfaceHeight + rise;
    float depth = surface - s.bedHeight;
    r.riverDistance = s.riverDistance;
    r.features = s.features;
    r.featuresAny = s.featuresAny;
    r.featuresAll = s.featuresAll;
    r.eddyWeight = s.eddyWeight;
    if (depth <= 0.0)
    {
        // Dry: report the level so a mesh can still sit at it.
        r.surfaceHeight = surface;
        return r;
    }

    float step = _RiverFieldParams.z;
    float hx0 = RiverStaticSurfaceOr(xz + float2(-step, 0), s.surfaceHeight);
    float hx1 = RiverStaticSurfaceOr(xz + float2(step, 0), s.surfaceHeight);
    float hz0 = RiverStaticSurfaceOr(xz + float2(0, -step), s.surfaceHeight);
    float hz1 = RiverStaticSurfaceOr(xz + float2(0, step), s.surfaceHeight);
    r.staticSlope = float2((hx1 - hx0) / (2.0 * step), (hz1 - hz0) / (2.0 * step));
    float2 slope = r.staticSlope + waveSlope;

    r.isWet = true;
    r.surfaceHeight = surface + waveH;
    r.depth = depth + waveH;
    r.flow = s.flow * flowScale;
    r.normal = normalize(float3(-slope.x, 1.0, -slope.y));
    return r;
}

#endif
