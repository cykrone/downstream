# Architecture

## One water, three readers

The design rule is "what the player sees is what pushes the boat". A per-track **River Field**
(sparse 64 m tiles at 0.5 m per texel: flow, surface height, bed height, river distance, feature
flags) plus analytic **WaterLayers** (Gerstner waves, flood pulses, tide) are the only water there
is. `IWaterQuery.Sample(x, z, raceTime)` is a pure function, read by:

1. the boat sim (8 pontoons per boat, every tick),
2. AI and items,
3. the water shader (planned: the same tiles in a `Texture2DArray`, manual 4-texel bilinear so the
   GPU matches the CPU exactly, and an HLSL port of the analytic layers).

Floods work because the field stores the bed, not a baked depth: depth is level minus bed, so a
rising pulse wets the floodable banks.

`ProceduralRiver` generates greybox fields until the editor baker (spline authoring, a compute-shader
shallow-water solve, hand combing) exists.

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

## What is next

1. Handling prototype and the month-4 fun gate: tune `BoatTuning` with playtesters.
2. Month-1 split-screen performance spike on a GTX 1660 Super (4 URP cameras).
3. River Field baker and the flow-mapped water shader.
4. Items, then the netcode spike (Steam relay, snapshots, reconciliation).
