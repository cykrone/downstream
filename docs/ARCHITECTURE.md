# Architecture

## One water, three readers

The design rule is "what the player sees is what pushes the boat". A per-track **River Field**
(sparse 64 m tiles at 0.5 m per texel: flow, surface height, bed height, river distance, feature
flags) plus analytic **WaterLayers** (Gerstner waves, flood pulses, tide) are the only water there
is. `IWaterQuery.Sample(x, z, raceTime)` is a pure function, read by:

1. the boat sim (8 pontoons per boat, every tick),
2. AI and items,
3. the water shader: `RiverFieldGpu` uploads the same tiles as two `Texture2DArray`s (32-bit float,
   one slice per existing tile, plus a tile-index texture) and the analytic layers as shader globals;
   `Shaders/RiverField.hlsl` is a line-for-line port of `SampleStatic`, `WaterLayers` and
   `RiverWater.Sample` that fetches the same four texels and blends them in float, so the surface
   drawn is the surface that pushes the boat. `RiverFieldGpuParityTests` dispatches
   `RiverFieldParity.compute` at thousands of points and times and compares it with the CPU.

Floods work because the field stores the bed, not a baked depth: depth is level minus bed, so a
rising pulse wets the floodable banks.

`ProceduralRiver` generates greybox fields until the editor baker (spline authoring, a compute-shader
shallow-water solve, hand combing) exists. Its floodable banks ramp up from the channel floor over
2.5 m to a shelf 0.5 m above the water: a vertical wall at the channel edge grounds boats on a step
and draws as a 0.5 m sawtooth at grazing angles.

## Simulation

- `BoatSimulator.Step` is the whole handling model as one function of
  (state, input, tuning, water, race time). No engine calls, no allocation, no hidden state.
- `BoatState` is a plain struct, so a race snapshot is an array copy. That makes 32-tick own-boat
  rollback (`OwnBoatPredictor`) and 20x headless AI races cheap.
- `RaceSimulation` steps 8 boats at 120 Hz; race time is `tick / 120`, never accumulated.
- All maths goes through `SimMath`, so the time-trial path can later switch to Unity.Mathematics in a
  Burst job with `FloatMode.Deterministic` without touching callers.
- PhysX only answers collision queries (`PhysicsBoatCollider`); `Physics.simulationMode` is Script.

## Unity side

`RaceDirector` builds the water and the race, spawns views, runs the fixed-step accumulator in
`Update`, and gives each `BoatView` a state interpolated between the last two ticks. Humans and AI
both produce `BoatInput`, so AI can never do what a controller cannot. `LocalPlayerJoin` pairs each
device to its own copy of the actions for split-screen; `SplitScreenLayout` gives the viewports.

## River features

The River Field's feature mask drives the water mechanics in `BoatSimulator.Step`, so the same flag
the shader draws is the one that moves the boat:

- **Eddy**: upstream flow plus a turn-rate multiplier, for the 120 degree pivot.
- **Hydraulic hole**: holds the hull for up to 1.5 s (a hop breaks out; boofing the ledge clears it).
- **Crest**: the downslope face holds speed without throttle and launches hops higher.
- **Current lane**: faster flow, faster drift charge, and the wake slot that drafting needs.

Wakes and bumps are boat-to-boat, so they live in `RaceSimulation`: wake slot and rough edges,
then capsule contacts with mass from the Weight stat.

## Race and items

`RaceSession` wraps the sim with the race flow (grid countdown, finish, places, respawns, results);
`CupStandings` scores cups. `ItemSystem` is host-authoritative state stepped by the session: buoys,
two item slots per boat, and a fixed array of world objects (mines and logs advected by the field,
surface-following pikes, thrown items, whirlpools, Kingfisher timers). `ItemAI` presses the item
button like a player would.

On the Unity side `RaceDirector` owns the session, `ItemWorldView` draws items as greybox primitives,
and `GreyboxRaceHud` is an IMGUI stand-in for the UI Toolkit HUD.

## Rendering

`Shaders/RiverWater.shader` (URP, Forward+) draws the water from the River Field alone: the vertex
stage lifts a flat 1 m grid to the sampled surface (so floods and waves move the mesh), and the
fragment stage re-samples the field per pixel for depth, flow and the feature mask. Ripple normals
and foam strokes are advected by the flow vector with Valve's two-phase flow map; the current lane
stretches them into long streaks. Depth tint is Beer-Lambert absorption of the refracted opaque
texture (the stone bed under the channel), lit by one warm key with cool sky fill and a stylized sky
fresnel. Foam follows the readability grammar: eddy lines are a seam wherever the four blended texels
disagree on the Eddy bit, holes are a counter-scrolling boil, crests and the waterfall lip break
white, whitewater appears above a flow speed or a surface slope, and shores get a thin lace.
`RaceDirector` hands the shader the same interpolated race time the boats are drawn at.

The greybox world is a block kit: `BlockMeshes` builds bevelled boxes (every edge chamfered so it
catches a highlight, flat-shaded so each block reads as a block) and terraced bank strips extruded
along the centreline. `WaterTextures` generates tileable ripple normals, foam strokes and pebbles at
runtime so the repository ships no binary placeholders; painted textures replace them with no shader
change. `GreyboxSceneBuilder` applies the design doc's rendering rules (warm key from the upper left,
cool trilight ambient so shadows read teal, light linear haze, a small-sun procedural sky, ACES
tonemapping with a touch of bloom) and configures URP for the water (depth and opaque textures,
HDR, 4x MSAA, Forward+).

## What is next

1. Handling prototype and the month-4 fun gate: tune `BoatTuning` with playtesters.
2. Month-1 split-screen performance spike on a GTX 1660 Super (4 URP cameras).
3. River Field baker (spline authoring, shallow-water solve, combing); reflections (probes, SSR) and VFX spray on the water.
4. Racing lines, AI difficulty levels and the seeded mistake budget.
5. The netcode spike (Steam relay, snapshots, reconciliation), including item events.
