# Downstream

An arcade boat racer where every race runs one river from its mountain source to the sea, and the
water is both the main opponent and the main tool. PC (Windows, Steam), 1-4 players local split-screen
and online.

- Design document: [Downstream — River Racing Game](https://claude.ai/code/artifact/ce5532e9-f828-442d-834c-6a21c1508a25)
- Unity technical research: [Downstream in Unity: Technical Research](https://claude.ai/code/artifact/247022d7-c8d7-4b47-b8b9-a3e601b44991)

## Stack

| Area | Choice |
| --- | --- |
| Engine | Unity 6.3 LTS (`6000.3.26f1`), upgrade to 6.7 LTS once it has settled |
| Rendering | URP, Forward+ |
| Gameplay sim | Engine-independent C# core at a fixed 120 Hz (`Downstream.Core`) |
| Water | Custom River Field shared by physics, AI and the water shader (`Shaders/RiverWater.shader`, parity-tested against the CPU) |
| Input | Input System 1.20 |
| Netcode | Custom on Steamworks.NET sockets (or Fish-Networking 4) — not started |
| Audio | FMOD — not started |
| Content | Addressables, one group per track |

## Getting started

1. Install Git LFS (`git lfs install`), then clone.
2. Open the folder in Unity Hub with editor **6000.3.26f1** (any newer 6000.3 patch is fine).
3. First open only: Unity generates `.meta` files for anything that lacks one and resolves
   `Packages/manifest.json`. Commit any new `.meta` files it creates.
4. Run **Downstream > Create Greybox Race Scene**. It creates and configures the URP assets, the
   lighting and post-processing from the design doc's rendering rules, a 1.5 km test river with a 6 m
   falls and a flood pulse, terraced block-kit banks, a boat and a chase camera, and saves
   `Assets/Downstream/Scenes/GreyboxRace.unity`. Run it again any time to rebuild the scene with the
   current look.
5. Press Play. After a 3 s countdown, the keyboard and the first gamepad drive boat 1 (RT/W throttle,
   LT/S brake, stick or A/D steer, RB/Space hop and drift, LB/E item: tap to use, hold to trail it
   behind as a shield); each further connected gamepad adds a split-screen player. The other boats
   are the 8 named AI rivals; set their difficulty on the RaceDirector (AI Level). Drive through the yellow buoys for items. At the results, Enter or A rematches.
6. Optional: `Tools/setup-unityyamlmerge.sh <path to UnityYAMLMerge>` to merge scenes and prefabs.

## Layout

```
Assets/Downstream/
  Scripts/
    Core/       Downstream.Core     pure C#, no UnityEngine: water, boat sim, race, items, AI, prediction
    Runtime/    Downstream.Runtime  MonoBehaviours: race director, views, cameras, input, greybox water, block kit and set dressing
    Editor/     Downstream.Editor   greybox scene builder, asset provenance database and build gate
  Shaders/                          River Field HLSL sampler, the river water shader, the GPU parity kernel
  Tests/EditMode/                   NUnit tests for the core (run in Unity and with dotnet)
  Tests/EditMode/Runtime/           Unity-only tests: the GPU water sampler against the CPU one
Tools/DotnetTests/                  builds the core as netstandard2.1 and runs its tests outside Unity
docs/ARCHITECTURE.md                how the pieces fit together
```

## Tests

The sim core has no Unity dependency, so its tests run anywhere:

```
dotnet test Tools/DotnetTests/Tests/Downstream.Core.Tests.csproj
```

They encode the design doc's acceptance criteria where a machine can check them: the boat settles
within 0.4 s after a 6 m drop, current lanes add 15% ±1%, the wake slot adds 8%, drift tiers fire
within one tick of 0.7 / 1.4 / 2.2 s, the landing rule, no capsizing under random input, spin-out
and immunity timings, item tables summing to 100%, deterministic replays and own-boat reconciliation.
They also cover the river features
(eddy pivots, holes, crests, wake edges, bumps), the race loop (countdown, finish, respawns, cup
points) and every item rule, the racing lines and AI (every difficulty finishes without stalling,
Expert beats Easy, drifts stay within the tier, the mistake budget matches the table), and race 8 AI
rivals with items headless down a greybox river.

CI runs these on every push (`.github/workflows/core-tests.yml`). The Unity EditMode run
(`.github/workflows/unity.yml`) starts once `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD`
secrets are added.

## Asset provenance

Every content asset (textures, models, audio, fonts, materials, prefabs) needs an approved record in
`Assets/Downstream/Settings/ProvenanceDatabase.asset` (create it with
*Create > Downstream > Provenance Database*). New imports are stamped *Pending* automatically.
Release builds fail if any asset in a built scene is a placeholder or unapproved; development builds
only warn.
