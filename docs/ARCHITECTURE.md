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
shallow-water solve, hand combing) exists. Its centreline is the sum of two sines (a 40 m swing every
300 m plus a 10 m wobble every 200 m) for S-bends and chicanes, with the current lane swinging to the
outside of each bend, and its width breathes (plus or minus 30% every 420 m) so narrows run fast and
pools run slow by continuity. Its floodable banks ramp up from the channel floor over
2.5 m to a shelf 0.5 m above the water: a vertical wall at the channel edge grounds boats on a step
and draws as a 0.5 m sawtooth at grazing angles.

## Simulation

- `BoatSimulator.Step` is the whole handling model as one function of
  (state, input, tuning, water, race time). No engine calls, no allocation, no hidden state.
- `BoatState` is a plain struct, so a race snapshot is an array copy. That makes 32-tick own-boat
  rollback (`OwnBoatPredictor`) and 20x headless AI races cheap.
- `RaceSimulation` steps 8 boats at 120 Hz; race time is `tick / 120`, never accumulated.
- Past the line a boat's race is over: `RaceSession` replaces its input with a brake and then nothing, and
  the river field carries 80 m of slack run-out water past the finish (`ProceduralRiver.RunOut`), so
  finished boats stop on the water instead of running off the field.
- All maths goes through `SimMath`, so the time-trial path can later switch to Unity.Mathematics in a
  Burst job with `FloatMode.Deterministic` without touching callers.
- PhysX only answers collision queries (`PhysicsBoatCollider`); `Physics.simulationMode` is Script.
  The collider reports the closing speed into the hardest contact; past `WallHitSpeed` the sim takes
  speed off, wallows the hull for a moment and drops any drift (`HitWall`), while glancing contact
  still slides along the bank with a small rebound (`WallRestitution`).
- Handling trade-offs that make the course a course: keel turning scrubs speed (`TurnScrub`) and
  loosens with speed (`TurnSpeedFalloff`), so the drift, with its charge tiers and boost, is the fast
  way round a bend; eddies add forward drag on top of their reversed flow; the greybox river runs a
  current worth reading (3.5 m/s base, 3.5 more in the lane, narrows faster still by continuity).

## Unity side

`RaceDirector` builds the water and the race, spawns views, runs the fixed-step accumulator in
`Update`, and gives each `BoatView` a state interpolated between the last two ticks. Humans and AI
both produce `BoatInput`, so AI can never do what a controller cannot. `LocalPlayerJoin` pairs each
device to its own copy of the actions for split-screen; `SplitScreenLayout` gives the viewports. `ChaseCamera` reads the water: it looks at the surface 12 m
ahead rather than at the boat's level (so a graded river stays in frame), measures its height from the
surface under it, and lifts where the water ahead drops more than the local grade predicts, which is
the design's "lifts over falls to show the pool below".

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

## AI

`RacingLineSet.Build` generates each track's racing lines from the centreline and the gameplay water,
the offline tool the design describes. Every 5 m it scans a cross-section of the river, finds the
navigable channels (deep enough, 3 m clear of banks and rocks, 2 m clear of holes) and picks an
offset per family: **safe** (most room, no hazards), **current lane** (fastest water), **aggressive**
(inside of the coming bend, fast water, crests) and, where a rock or island splits the channel,
a **shortcut** through the other side. Offsets are smoothed and rate-limited so a boat can follow
them, and each line is timed at a nominal speed so lines can be compared. Designers can overwrite
offsets and call `Retime`.

`RacerAI` drives one boat. Every 0.5 s it scores the lines over the next 150 m (estimated time,
rivals in the lane, items held, personality) and switches after its reaction delay; a look-ahead
controller follows the chosen line, reads the water at 3 points ahead to steer round rocks and boof
holes, drifts bends that ask for more turn than the keel gives, and aims for clean landings.
`AIDifficulty` is the design's table (reaction delay, line noise, max drift tier, shortcut use,
mistakes per race, item patience); nothing scales boat speed. `MistakeBudget` fixes each AI's
mistakes (missed drift tier, slapped landing, wrong channel, throttle lift, over-correction) from
the seed at race start, never by position. `RivalRoster` holds the 8 named rivals and their traits
(Line Thief, Shortcut Hunter, Drafter, Bully, Steady, Current Reader, Daredevil); the names are
placeholders. `AdaptiveDifficulty` moves the level between races only. Finished players hand their
boat to a mistake-free Expert driver.

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
catches a highlight, flat-shaded so each block reads as a block), the low grass-lipped block that
edges the channel, smooth spheres and cones for the organic props, and seeded rounded rocks shared by
the river boulders. The river bed and the floodable meadow are one mesh under
`Shaders/GreyboxGround.shader`, which blends stones into grass by vertex colour with a pale sand rim
at the waterline (the bank treatment of the Animal Crossing reference), so the shoreline is never
cut along the mesh grid; steep faces show earth under grass and rock under stones, so terrace risers
and the waterfall face read as cut ground. Behind the meadow one continuous terrain heightfield per
side (in river coordinates, so it follows the course) climbs a short earth riser to the grass
terrace and on into tiered hills; there are no extruded bank strips to fold on bends or clip the
hills. The greybox river runs on a 15 degree grade so it visibly flows downhill; the water shader
judges whitewater on slope in excess of that grade (`_RiverBaseSlope`), so only rapids, ledges and
the falls break white. Obstacles read as a course: slalom rock gardens, bridge piers, an island choke in the run-in to the falls, a wide step and a rapid below it, a boulder gate near the finish, each rock with its eddy. The course is laid out like a race track: two meander components (90 m over
500 m and 15 m over 230 m) swing the heading through about 55 degrees in each sweeper. Because the
centreline is a sheared function x(z), the field's half-span and the dressing's lateral offsets are
scaled by the slope so the channel keeps its width across the sharp bends, and river distance is
measured along the flow (`ProceduralRiver.DistanceAt`: z + slope * (x - centre), constant on the line
square to the current), so the surface is level across the channel and the falls, ledges, wave trains
and holes cross it perpendicular to the current instead of along a line of constant z. The water
surface and bed grids are built in those river coordinates (rows of equal distance, columns across
the channel, with extra rows either side of every drop), so a 6 m fall is one clean strip.

Boats are lofted hulls (`BlockBoat` in `BoatMeshes.cs`) on the sim's 4 x 1.4 m footprint, a wide
canoe: a fine bow entry, full midships, a flat transom, a crowned deck with a rub rail and cockpit
coaming, a seated pilot in a vest and helmet (built at unit scale about the hips and scaled 1.1x as a
whole, so an adult fills the cockpit), and a 2.4 m double-bladed paddle. The pilot is articulated (hips, torso, neck, shoulders, grips) with
goggles, a helmet peak and stripe, vest straps and collar, jersey sleeves, gloved hands on the
paddle and knees under the coaming; `BoatAnimator` drives it from the presented state alone: a
stroke cycle whose rate follows speed (blades dip alternately, the shaft sweeps and slides, the arms
reach to the grips), a torso that twists with the stroke and leans forward with speed and into a
drift, a head that stays level and looks into the turn, and a paddle raised clear in the air. The
paddle pose is solved in the pilot-root frame (`BoatAnimator.SolvePaddle`), so the torso's lean never
tilts it into the hull, and every sample along the shaft and around both blades is tested against
the hull section at its own height and station (`BlockBoat.HalfWidthAt`, rail band included) and the
paddle pushed outboard by the largest shortfall: the blade's arc stays 10 cm clear of the hull on
either side, which the edit-mode tests step through a whole cycle to confirm.
`BoatView` gives each boat a livery from an eight-colour set (hull, helmet, vest trim and sleeves
share one material instance per boat). `BoatEffects` sells speed from the presented state
alone: a stern wake and two bow wakes as trail ribbons that are re-sampled onto the water surface
every frame (so waves do not cut them), bow spray that scales with speed, drift spray off the
outside of a slide coloured by tier, a boost plume and a landing splash, all through
`Shaders/GreyboxSpray.shader` (unlit, alpha-blended, fogged). `ChaseCamera` widens its field of view
with speed as well as boost. `ItemWorldView` shows pickups as flagged floats on a ring, mines as
spiked spheres, logs as capped trunks, pikes as finned bodies and whirlpools as a spinning disc with
spiral foam arms.

Under `Assets/Downstream/Art/Vendor` sit two CC0 packs, tracked with Git LFS: the Stylized Nature
MegaKit (Quaternius; 68 models of trees, pines, bushes, rocks, grass, flowers and mushrooms with
their textures) and a Poly Haven sky HDRI. `VendorAssetPostprocessor` imports them straight into the
look: URP Lit, matte, foliage alpha-clipped and two-sided, the kit's autumn leaf texture swapped for
the summer one, the HDRI readable. When the packs are on disk the scene builder wires them into the
dressing, which scales each model to a target height from its measured bounds so density and
silhouette rules hold whichever pack is used; without them the block props below are the fallback.
The builder also measures the HDRI (brightest patch = sun, the band above the horizon = fog colour),
turns the panorama so the sun sits where the design wants the key light, and sets the directional
light's elevation and the fog to match. `Shaders/GreyboxSkyHdri.shader` draws the panorama at its
full 4096 width with a brightness clamp for the sun, unbroken all the way down (no haze band, nothing
painted below the horizon); it samples with wrapped UV gradients, because the lat-long seam otherwise
drops to the smallest mip along one pixel column once mipmaps are on. Fog is sky-matched
(`Shaders/DownstreamSkyFog.hlsl`): the ground, water and prop shaders fog toward the panorama sampled in
the view direction, so land and water dissolve into exactly the sky behind them in every direction,
above or below the horizon; one global fog colour could not, since the HDRI horizon is paler toward the
sun and darker away from it. `GreyboxDressing` registers the panorama and its rotation, exposure and
clamp as shader globals at runtime; the global fog colour (the sky at the horizon line, at the sky's
exposure) remains for the URP Lit vendor props.

`GreyboxDressing` dresses the valley at runtime from a fixed seed, nothing saved in the scene:
rolling grass hills behind the bank blocks (a heightfield in river coordinates, so it follows the
meander, and one continuous sheet per side: dense across the terrace, sparse on the hills, then coarser
still out to 3.2 km in every direction, past both ends of the course as well, where the dry valley
continues; beyond the built hills the land climbs toward eye level with rolling relief, so from a river
that has dropped far below its start the horizon is still land dissolving into fog and never the sky's
lower half; the ground shader fades its tiling to the palette colour with distance so no grid shows), puffy round trees and drooping pines built from a few merged mesh variants (a tree is two
draws), bushes, rounded rocks, reeds at the waterline, flower clusters in the pop colours, lantern
posts along the water, cairns on the outside of bends, a bridge to race under, docks with crates, a
shrine on the hill, and a camp or bunting every 100 m of bank. `Shaders/GreyboxProp.shader` gives
every prop the design's form shading (lit tops, mid sides, dark undersides) with wrap lighting,
occlusion, shadows and fog; props share a handful of materials each so the SRP batcher keeps the
draw count down. Meadow placement is measured from the channel edge, so it follows the breathing width.
Every scattered prop is seated on the rendered ground, not the land formula: the bed and meadow, both
hill sheets and the far ground get mesh colliders at dressing time and each prop drops by a ray onto
the highest of them, a small sink below the surface (rocks bed in by a seventh of their height), and
rocks, grass and flowers lie with the ground normal while trees lean with it only up to 8 degrees.
The formula and the meshes disagree where the hills are sampled coarsely and where the bed ramps down
to the water, which is what left vegetation hovering; the dressing logs how many moved per category. `WaterTextures` generates tileable ripple normals, foam strokes and pebbles at
runtime so the repository ships no binary placeholders; painted textures replace them with no shader
change. `GreyboxSceneBuilder` applies the design doc's rendering rules (warm key from the upper left, a
sky-driven ambient so shadows take the sky's colour, light linear haze that meets the sky at the
horizon, ACES tonemapping with a touch of bloom, a vignette and warm white balance) and configures
URP for the water and the look (depth and opaque textures, HDR, 4x MSAA plus SMAA, Forward+, four
soft shadow cascades to 320 m, screen-space ambient occlusion added to the renderer by reflection
since URP keeps the feature type internal). `Shaders/GreyboxSky.shader` is the painted sky: a
zenith-to-horizon gradient whose horizon band is the fog colour, so far hills dissolve into the same
haze the sky ends in, a soft sun disc and halo lit from the scene's sun direction, and two layers of
drifting clouds sampled from the generated noise (no cloud props, no texture downloads). `GreyboxDressing` refreshes the ambient probe from the sky at
runtime and renders one realtime reflection probe over the course, which the water blends into its
sky term. Canopies, bushes and pines are smooth-shaded: the design wants chunky, soft-edged forms.

## What is next

1. Handling prototype and the month-4 fun gate: tune `BoatTuning` with playtesters.
2. Month-1 split-screen performance spike on a GTX 1660 Super (4 URP cameras).
3. River Field baker (spline authoring, shallow-water solve, combing); reflections (probes, SSR) and VFX spray on the water.
4. The netcode spike (Steam relay, snapshots, reconciliation), including item events.
