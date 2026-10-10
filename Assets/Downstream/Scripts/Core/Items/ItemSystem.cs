using System;
using Downstream.Core.Boat;
using Downstream.Core.Math;
using Downstream.Core.Race;
using Downstream.Core.Sim;
using Downstream.Core.Water;

namespace Downstream.Core.Items
{
    /// <summary>Item-related things that happened to a boat during one tick, for HUD, audio and telemetry.</summary>
    [Flags]
    public enum ItemEvents : ushort
    {
        None = 0,
        PickedUp = 1 << 0,
        Used = 1 << 1,
        HeldBehind = 1 << 2,
        Dropped = 1 << 3,
        Hit = 1 << 4,
        Blocked = 1 << 5,
        Dodged = 1 << 6,
        KingfisherWarning = 1 << 7,
        Bounced = 1 << 8,
        Slung = 1 << 9,
        Swamped = 1 << 10,
    }

    /// <summary>What is floating on, flying over or circling in the river.</summary>
    public enum ItemObjectKind : byte
    {
        None,
        Mine,
        ThrownMine,
        Log,
        Pike,
        ThrownWhirl,
        Whirlpool,
        Kingfisher,
    }

    /// <summary>One live item in the world. A plain struct in a fixed array, so the item state copies like boat state.</summary>
    [Serializable]
    public struct ItemObject
    {
        public ItemObjectKind Kind;
        public sbyte Owner;
        /// <summary>For a Kingfisher: the boat it is diving on.</summary>
        public sbyte Target;
        public SimVec3 Position;
        public SimVec3 Velocity;
        public float Age;
        public float Life;
        /// <summary>Boats already affected (whirlpools act once per boat), one bit per boat.</summary>
        public byte Touched;

        public bool Active => Kind != ItemObjectKind.None;
        public bool Armed => Age >= ItemRules.ArmSeconds;
    }

    /// <summary>One boat's item slots and running item effects.</summary>
    [Serializable]
    public struct BoatItemState
    {
        /// <summary>The item slot (design: each boat holds one item).</summary>
        public ItemType Held;
        /// <summary>Uses left for Turbine x3 and Triple Pike.</summary>
        public byte Uses;
        /// <summary>The held-behind slot: a mine, logs or a pike trailing as a shield.</summary>
        public ItemType Behind;

        public float ButtonHeld;
        public bool PrevButton;

        public float ShieldTime;
        public float WakeBlastTime;
        public float SurgeTime;
        public float DamBurstTime;
        /// <summary>Seconds until a Kingfisher strikes this boat, 0 if none is coming.</summary>
        public float KingfisherIncoming;
        /// <summary>Boats a running Surge has already bounced, one bit per boat.</summary>
        public byte SurgeTouched;
    }

    /// <summary>Item buoy: drive through it with an empty slot to draw an item for your place.</summary>
    [Serializable]
    public struct ItemBuoy
    {
        public SimVec3 Position;
        public float Distance;
        public float Cooldown;
    }

    /// <summary>Item numbers. Values in the design doc are marked; the rest are starting points to tune.</summary>
    public static class ItemRules
    {
        /// <summary>Attacks cannot hit for this long after launch, so every attack is seen coming (design: warning of at least 0.5 s).</summary>
        public const float ArmSeconds = 0.5f;
        public const float HoldBehindSeconds = 0.25f;

        public const float TurbineBoost = 1.0f;           // design
        public const float TurbineLaneScale = 1.3f;       // design: stronger inside a current lane
        public const float ShieldSeconds = 6f;            // design
        public const float WakeBlastSeconds = 3f;         // design
        public const float WakeBlastScale = 3f;           // design
        public const float SurgeSeconds = 3f;             // design
        public const float WhirlSeconds = 5f;             // design
        public const float WhirlRadius = 6f;
        public const float WhirlSlingAngleDeg = 35f;
        public const float WhirlSlingBoost = 0.8f;
        public const float KingfisherWarning = 3f;        // design
        public const float DamBurstSeconds = 6f;          // design
        public const float DamBurstFlow = 1.2f;           // design: +20% current for places 5-8
        public const int DamBurstFirstPlace = 5;

        public const float PikeSpeed = 50f;
        public const float PikeLife = 4f;
        public const float FloatLife = 30f;
        public const float ThrowForward = 16f;
        public const float ThrowUp = 6f;
        public const float HitRadius = 1.6f;
        public const float LogRadius = 1.8f;
        public const float LogSpread = 3f;
        public const float SurgeReach = 7f;
        public const float SurgeBounce = 6f;

        /// <summary>Item buoys stop this far through the track (design: none in the final 15%).</summary>
        public const float LastBuoyFraction = 0.85f;
        /// <summary>No attack can launch past this (design: final 10%).</summary>
        public const float LastAttackFraction = 0.9f;
        /// <summary>Kingfisher cannot launch past this (design: final 20%).</summary>
        public const float LastKingfisherFraction = 0.8f;
        public const float BuoyRadius = 1.5f;
        public const float BuoyRespawn = 2f;

        public static bool IsAttack(ItemType t) =>
            t == ItemType.LilyMine || t == ItemType.LogJam || t == ItemType.Pike || t == ItemType.TriplePike ||
            t == ItemType.Whirl || t == ItemType.Kingfisher || t == ItemType.WakeBlaster || t == ItemType.Surge;

        /// <summary>Items that can trail behind the boat as a shield and be dropped there.</summary>
        public static bool CanHoldBehind(ItemType t) =>
            t == ItemType.LilyMine || t == ItemType.LogJam || t == ItemType.Pike || t == ItemType.TriplePike;
    }

    /// <summary>
    /// Items for one race, host-authoritative and deterministic: buoys, the two item slots per boat,
    /// thrown and floating objects advected by the River Field, and hits. Dropped items float downstream
    /// with the current; every attack has a dodge (design: "Items and power-ups").
    /// </summary>
    public sealed class ItemSystem
    {
        public const int MaxObjects = 64;

        public ItemRuleset Ruleset { get; }
        public ItemBuoy[] Buoys { get; }
        public ItemObject[] Objects { get; } = new ItemObject[MaxObjects];
        public BoatItemState[] Boats { get; }
        public ItemEvents[] LastEvents { get; }

        private SimRandom _rng;

        public ItemSystem(int boatCount, ItemBuoy[] buoys, ulong seed, ItemRuleset ruleset = ItemRuleset.Standard)
        {
            Ruleset = ruleset;
            Buoys = ruleset == ItemRuleset.None ? Array.Empty<ItemBuoy>() : (ItemBuoy[])buoys.Clone();
            Boats = new BoatItemState[boatCount];
            LastEvents = new ItemEvents[boatCount];
            _rng = new SimRandom(seed, 77u);
        }

        /// <summary>Gives a boat an item directly (tests, Time Trial's three Turbines, debug).</summary>
        public void Give(int boat, ItemType item)
        {
            Boats[boat].Held = item;
            Boats[boat].Uses = (byte)(item == ItemType.TurbineX3 || item == ItemType.TriplePike ? 3 : 1);
        }

        /// <summary>Sets the per-boat modifiers items own, before the boats step.</summary>
        public void PreStep(RaceSession race)
        {
            var sim = race.Sim;
            for (int i = 0; i < sim.BoatCount; i++)
            {
                var m = sim.Modifiers[i];
                m.FlowMultiplier = Boats[i].DamBurstTime > 0f ? ItemRules.DamBurstFlow : 1f;
                sim.Modifiers[i] = m;
                sim.WakeScale[i] = Boats[i].WakeBlastTime > 0f ? ItemRules.WakeBlastScale : 1f;
                if (Boats[i].SurgeTime > 0f && !sim.Parked[i])
                    sim.State.Boats[i].BoostTime = SimMath.Max(sim.State.Boats[i].BoostTime, 2f * BoatSimulator.TickDelta);
            }
        }

        /// <summary>Pickups, item use, moving objects and hits, after the boats have stepped.</summary>
        public void PostStep(RaceSession race, BoatInput[] inputs)
        {
            var sim = race.Sim;
            float dt = BoatSimulator.TickDelta;
            float t = sim.RaceTime;
            for (int i = 0; i < sim.BoatCount; i++)
            {
                LastEvents[i] = ItemEvents.None;
                ref var b = ref Boats[i];
                b.ShieldTime = SimMath.Max(0f, b.ShieldTime - dt);
                b.WakeBlastTime = SimMath.Max(0f, b.WakeBlastTime - dt);
                b.DamBurstTime = SimMath.Max(0f, b.DamBurstTime - dt);
                if (b.SurgeTime > 0f)
                {
                    b.SurgeTime = SimMath.Max(0f, b.SurgeTime - dt);
                    if (b.SurgeTime == 0f) b.SurgeTouched = 0;
                }
                if (sim.State.Boats[i].WetFraction > 0f && (sim.Modifiers[i].GripScale == RaceSimulation.SwampedGripScale))
                    LastEvents[i] |= ItemEvents.Swamped;
            }

            if (race.Phase != RacePhase.Racing)
            {
                for (int i = 0; i < sim.BoatCount; i++) Boats[i].PrevButton = inputs[i].UseItem;
                return;
            }

            UpdateBuoys(race);
            for (int i = 0; i < sim.BoatCount; i++)
            {
                if (race.Status[i].IsRespawning || race.Status[i].Finished)
                {
                    Boats[i].PrevButton = false;
                    Boats[i].ButtonHeld = 0f;
                    continue;
                }
                HandleButton(race, i, inputs[i].UseItem);
            }
            MoveObjects(race, t);
            HitBoats(race);
            UpdateSurges(race);
        }

        // ---- Buoys -------------------------------------------------------------------------------

        private void UpdateBuoys(RaceSession race)
        {
            var sim = race.Sim;
            for (int k = 0; k < Buoys.Length; k++)
            {
                ref var buoy = ref Buoys[k];
                if (buoy.Cooldown > 0f)
                {
                    buoy.Cooldown = SimMath.Max(0f, buoy.Cooldown - BoatSimulator.TickDelta);
                    continue;
                }
                for (int i = 0; i < sim.BoatCount; i++)
                {
                    if (sim.Parked[i] || race.Status[i].Finished) continue;
                    var d = (sim.State.Boats[i].Position - buoy.Position).Flat;
                    float reach = ItemRules.BuoyRadius + sim.Tunings[i].HullBeam * 0.5f;
                    if (d.SqrMagnitude > reach * reach) continue;
                    buoy.Cooldown = ItemRules.BuoyRespawn;
                    if (Boats[i].Held != ItemType.None) break;
                    var item = ItemDistribution.Roll(race.Status[i].Place, ref _rng, Ruleset);
                    if (item == ItemType.None) break;
                    Give(i, item);
                    LastEvents[i] |= ItemEvents.PickedUp;
                    break;
                }
            }
        }

        /// <summary>
        /// Rows of buoys across the river every <paramref name="spacing"/> metres (design: every 20-25 s
        /// of racing, rows of 4-6, none in the final 15%). Buoys sit only on deep, open water.
        /// </summary>
        public static ItemBuoy[] Layout(RaceTrack track, IWaterQuery water, float spacing = 500f, float firstRow = 150f, int perRow = 5)
        {
            var list = new System.Collections.Generic.List<ItemBuoy>();
            float last = track.Length * ItemRules.LastBuoyFraction;
            for (float d = firstRow; d <= last; d += spacing)
            {
                var c = track.PointAt(d);
                var dir = (track.PointAt(d + 2f) - c).Flat.Normalized;
                var side = new SimVec3(dir.Z, 0f, -dir.X);
                // Find the wet span across the river at this point.
                float left = 0f, right = 0f;
                while (left > -200f && water.Sample(c.X + side.X * (left - 1f), c.Z + side.Z * (left - 1f), 0f).IsWet) left -= 1f;
                while (right < 200f && water.Sample(c.X + side.X * (right + 1f), c.Z + side.Z * (right + 1f), 0f).IsWet) right += 1f;
                float margin = 2f;
                float span = right - left - 2f * margin;
                if (span <= 0f) continue;
                for (int k = 0; k < perRow; k++)
                {
                    float u = perRow == 1 ? 0.5f : k / (float)(perRow - 1);
                    var p = c + side * (left + margin + span * u);
                    var w = water.Sample(p.X, p.Z, 0f);
                    if (!w.IsWet || (w.Features & (WaterFeature.HydraulicHole | WaterFeature.WaterfallLip)) != 0) continue;
                    list.Add(new ItemBuoy { Position = new SimVec3(p.X, w.SurfaceHeight, p.Z), Distance = d });
                }
            }
            return list.ToArray();
        }

        // ---- Using items -------------------------------------------------------------------------

        private void HandleButton(RaceSession race, int i, bool button)
        {
            ref var b = ref Boats[i];
            bool pressed = button && !b.PrevButton;
            bool released = !button && b.PrevButton;
            b.PrevButton = button;

            if (button) b.ButtonHeld += BoatSimulator.TickDelta;

            // Holding the button moves a droppable item behind the boat as a shield.
            if (button && b.Behind == ItemType.None && b.ButtonHeld >= ItemRules.HoldBehindSeconds && ItemRules.CanHoldBehind(b.Held))
            {
                b.Behind = b.Held == ItemType.TriplePike ? ItemType.Pike : b.Held;
                ConsumeOne(ref b);
                LastEvents[i] |= ItemEvents.HeldBehind;
            }

            if (released)
            {
                float held = b.ButtonHeld;
                b.ButtonHeld = 0f;
                if (b.Behind != ItemType.None && held >= ItemRules.HoldBehindSeconds)
                {
                    var behind = b.Behind;
                    b.Behind = ItemType.None;
                    // A trailing pike fires forward on release; mines and logs are dropped where they trail.
                    if (behind == ItemType.Pike) Use(race, i, ItemType.Pike, forward: true);
                    else Use(race, i, behind, forward: false);
                    LastEvents[i] |= ItemEvents.Dropped;
                }
                else if (b.Held != ItemType.None && held < ItemRules.HoldBehindSeconds)
                {
                    var item = b.Held == ItemType.TriplePike ? ItemType.Pike : b.Held == ItemType.TurbineX3 ? ItemType.TurbineX1 : b.Held;
                    if (Use(race, i, item, forward: true)) ConsumeOne(ref b);
                }
            }
            else if (pressed)
            {
                b.ButtonHeld = BoatSimulator.TickDelta;
            }
        }

        private static void ConsumeOne(ref BoatItemState b)
        {
            if (b.Uses > 1)
            {
                b.Uses--;
                return;
            }
            b.Held = ItemType.None;
            b.Uses = 0;
        }

        /// <summary>Applies one item. Returns false if the rules refuse it here (late-race attack bans).</summary>
        private bool Use(RaceSession race, int i, ItemType item, bool forward)
        {
            var sim = race.Sim;
            float fraction = sim.State.Progress[i] / sim.Track.Length;
            if (ItemRules.IsAttack(item) && fraction > ItemRules.LastAttackFraction) return false;
            if (item == ItemType.Kingfisher && fraction > ItemRules.LastKingfisherFraction) return false;

            ref var boat = ref sim.State.Boats[i];
            ref var b = ref Boats[i];
            var fwd = boat.FlatForward;
            var tuning = sim.Tunings[i];
            switch (item)
            {
                case ItemType.TurbineX1:
                case ItemType.TurbineX3:
                    boat.BoostTime = SimMath.Max(boat.BoostTime, ItemRules.TurbineBoost * (boat.InCurrentLane ? ItemRules.TurbineLaneScale : 1f));
                    break;
                case ItemType.ReedShield:
                    b.ShieldTime = ItemRules.ShieldSeconds;
                    break;
                case ItemType.WakeBlaster:
                    b.WakeBlastTime = ItemRules.WakeBlastSeconds;
                    break;
                case ItemType.Surge:
                    b.SurgeTime = ItemRules.SurgeSeconds;
                    b.SurgeTouched = 0;
                    break;
                case ItemType.DamBurst:
                    for (int j = 0; j < sim.BoatCount; j++)
                        if (race.Status[j].Place >= ItemRules.DamBurstFirstPlace) Boats[j].DamBurstTime = ItemRules.DamBurstSeconds;
                    break;
                case ItemType.Kingfisher:
                {
                    int leader = -1;
                    for (int j = 0; j < sim.BoatCount; j++)
                        if (race.Status[j].Place == 1) leader = j;
                    if (leader >= 0 && leader != i)
                    {
                        Spawn(new ItemObject { Kind = ItemObjectKind.Kingfisher, Owner = (sbyte)i, Target = (sbyte)leader, Life = ItemRules.KingfisherWarning });
                        Boats[leader].KingfisherIncoming = ItemRules.KingfisherWarning;
                        LastEvents[leader] |= ItemEvents.KingfisherWarning;
                    }
                    break;
                }
                case ItemType.Pike:
                {
                    var p = boat.Position + fwd * (tuning.HullLength * 0.5f + 1f);
                    Spawn(new ItemObject { Kind = ItemObjectKind.Pike, Owner = (sbyte)i, Target = -1, Position = p, Velocity = fwd * ItemRules.PikeSpeed, Life = ItemRules.PikeLife });
                    break;
                }
                case ItemType.LilyMine:
                    if (forward) Throw(i, ItemObjectKind.ThrownMine, boat, fwd);
                    else Spawn(new ItemObject { Kind = ItemObjectKind.Mine, Owner = (sbyte)i, Target = -1, Position = Behind(boat, tuning, fwd, 0f), Life = ItemRules.FloatLife });
                    break;
                case ItemType.LogJam:
                {
                    var side = boat.FlatRight;
                    for (int k = -1; k <= 1; k++)
                    {
                        var p = Behind(boat, tuning, fwd, k * ItemRules.LogSpread * 0.5f);
                        Spawn(new ItemObject { Kind = ItemObjectKind.Log, Owner = (sbyte)i, Target = -1, Position = p, Velocity = side * (k * 0.8f), Life = ItemRules.FloatLife });
                    }
                    break;
                }
                case ItemType.Whirl:
                    Throw(i, ItemObjectKind.ThrownWhirl, boat, fwd);
                    break;
                default:
                    return false;
            }
            LastEvents[i] |= ItemEvents.Used;
            return true;
        }

        private static SimVec3 Behind(in BoatState boat, in BoatTuning tuning, SimVec3 fwd, float lateral) =>
            boat.Position - fwd * (tuning.HullLength * 0.5f + 2.5f) + boat.FlatRight * lateral;

        private void Throw(int owner, ItemObjectKind kind, in BoatState boat, SimVec3 fwd)
        {
            var v = boat.Velocity.Flat + fwd * ItemRules.ThrowForward + new SimVec3(0f, ItemRules.ThrowUp, 0f);
            Spawn(new ItemObject { Kind = kind, Owner = (sbyte)owner, Target = -1, Position = boat.Position + new SimVec3(0f, 1f, 0f), Velocity = v, Life = 6f });
        }

        private void Spawn(in ItemObject o)
        {
            for (int k = 0; k < Objects.Length; k++)
            {
                if (Objects[k].Active) continue;
                Objects[k] = o;
                return;
            }
            // Full: replace the oldest floating object rather than drop an attack.
            int oldest = 0;
            for (int k = 1; k < Objects.Length; k++)
                if (Objects[k].Age > Objects[oldest].Age) oldest = k;
            Objects[oldest] = o;
        }

        // ---- World objects -----------------------------------------------------------------------

        private void MoveObjects(RaceSession race, float t)
        {
            var sim = race.Sim;
            var water = sim.Water;
            float dt = BoatSimulator.TickDelta;
            const float gravity = 9.81f * 1.4f;
            for (int k = 0; k < Objects.Length; k++)
            {
                ref var o = ref Objects[k];
                if (!o.Active) continue;
                o.Age += dt;
                if (o.Age >= o.Life && o.Kind != ItemObjectKind.Kingfisher && o.Kind != ItemObjectKind.ThrownMine && o.Kind != ItemObjectKind.ThrownWhirl)
                {
                    o.Kind = ItemObjectKind.None;
                    continue;
                }
                switch (o.Kind)
                {
                    case ItemObjectKind.Mine:
                    case ItemObjectKind.Log:
                    {
                        // Dropped items ride the current (design: "float downstream at current speed").
                        var w = water.Sample(o.Position.X, o.Position.Z, t);
                        if (!w.IsWet)
                        {
                            o.Kind = ItemObjectKind.None;
                            break;
                        }
                        var spread = o.Velocity.Flat * (1f - 2f * dt);
                        o.Velocity = spread;
                        o.Position += (w.Flow + spread) * dt;
                        o.Position = new SimVec3(o.Position.X, w.SurfaceHeight, o.Position.Z);
                        break;
                    }
                    case ItemObjectKind.Pike:
                    {
                        // Runs on the surface, drops down falls, and stops at walls (dry water ahead).
                        var next = o.Position + o.Velocity * dt;
                        var w = water.Sample(next.X, next.Z, t);
                        if (!w.IsWet)
                        {
                            o.Kind = ItemObjectKind.None;
                            break;
                        }
                        o.Position = new SimVec3(next.X, w.SurfaceHeight, next.Z);
                        ShootFloaters(ref o);
                        break;
                    }
                    case ItemObjectKind.ThrownMine:
                    case ItemObjectKind.ThrownWhirl:
                    {
                        o.Velocity += new SimVec3(0f, -gravity * dt, 0f);
                        o.Position += o.Velocity * dt;
                        var w = water.Sample(o.Position.X, o.Position.Z, t);
                        if (w.IsWet && o.Position.Y <= w.SurfaceHeight)
                        {
                            bool whirl = o.Kind == ItemObjectKind.ThrownWhirl;
                            o.Kind = whirl ? ItemObjectKind.Whirlpool : ItemObjectKind.Mine;
                            o.Position = new SimVec3(o.Position.X, w.SurfaceHeight, o.Position.Z);
                            o.Velocity = SimVec3.Zero;
                            o.Life = o.Age + (whirl ? ItemRules.WhirlSeconds : ItemRules.FloatLife);
                        }
                        else if (o.Age > 6f)
                        {
                            o.Kind = ItemObjectKind.None; // landed on a bank
                        }
                        break;
                    }
                    case ItemObjectKind.Kingfisher:
                    {
                        int target = o.Target;
                        if (target >= 0) Boats[target].KingfisherIncoming = SimMath.Max(0f, o.Life - o.Age);
                        if (o.Age < o.Life) break;
                        if (target >= 0)
                        {
                            Boats[target].KingfisherIncoming = 0f;
                            if (!race.Status[target].IsRespawning && !race.Status[target].Finished)
                            {
                                // Reed Shield and the held-behind slot do not stop it; only a hop does (design).
                                if (Dodging(sim.State.Boats[target])) LastEvents[target] |= ItemEvents.Dodged;
                                else if (BoatSimulator.ApplyHit(ref sim.State.Boats[target], sim.Tunings[target])) LastEvents[target] |= ItemEvents.Hit;
                            }
                        }
                        o.Kind = ItemObjectKind.None;
                        break;
                    }
                }
            }
        }

        /// <summary>A pike that meets a floating mine or log destroys both (design counterplay: "shoot it").</summary>
        private void ShootFloaters(ref ItemObject pike)
        {
            for (int k = 0; k < Objects.Length; k++)
            {
                ref var o = ref Objects[k];
                if (o.Kind != ItemObjectKind.Mine && o.Kind != ItemObjectKind.Log) continue;
                float r = o.Kind == ItemObjectKind.Log ? ItemRules.LogRadius : ItemRules.HitRadius;
                if ((o.Position - pike.Position).Flat.SqrMagnitude > r * r) continue;
                o.Kind = ItemObjectKind.None;
                pike.Kind = ItemObjectKind.None;
                return;
            }
        }

        /// <summary>A boat counts as hopping clear while a hop is in the air or it is airborne.</summary>
        public static bool Dodging(in BoatState s) => s.HopTime > 0f || s.Airborne || s.WetFraction <= 0f;

        private void HitBoats(RaceSession race)
        {
            var sim = race.Sim;
            for (int k = 0; k < Objects.Length; k++)
            {
                ref var o = ref Objects[k];
                if (o.Kind != ItemObjectKind.Mine && o.Kind != ItemObjectKind.Log && o.Kind != ItemObjectKind.Pike && o.Kind != ItemObjectKind.Whirlpool)
                    continue;
                for (int i = 0; i < sim.BoatCount && o.Active; i++)
                {
                    if (sim.Parked[i]) continue;
                    if (o.Kind == ItemObjectKind.Pike && o.Owner == i) continue;
                    ref var boat = ref sim.State.Boats[i];
                    var d = (boat.Position - o.Position).Flat;

                    if (o.Kind == ItemObjectKind.Whirlpool)
                    {
                        WhirlContact(ref o, i, ref boat, sim.Tunings[i], d);
                        continue;
                    }

                    float radius = (o.Kind == ItemObjectKind.Log ? ItemRules.LogRadius : ItemRules.HitRadius) + sim.Tunings[i].HullBeam * 0.5f;
                    if (d.SqrMagnitude > radius * radius || !o.Armed) continue;

                    bool hoppable = o.Kind == ItemObjectKind.Log || o.Kind == ItemObjectKind.Pike;
                    if (hoppable && Dodging(boat))
                    {
                        LastEvents[i] |= ItemEvents.Dodged;
                        continue;
                    }

                    o.Kind = ItemObjectKind.None;
                    ref var bi = ref Boats[i];
                    // From behind, a trailing item takes the hit; otherwise a Reed Shield does.
                    bool fromBehind = SimVec3.Dot(d, boat.FlatForward) > 0f;
                    if (fromBehind && bi.Behind != ItemType.None)
                    {
                        bi.Behind = ItemType.None;
                        LastEvents[i] |= ItemEvents.Blocked;
                    }
                    else if (bi.ShieldTime > 0f)
                    {
                        bi.ShieldTime = 0f;
                        LastEvents[i] |= ItemEvents.Blocked;
                    }
                    else if (BoatSimulator.ApplyHit(ref boat, sim.Tunings[i]))
                    {
                        LastEvents[i] |= ItemEvents.Hit;
                    }
                }
            }
        }

        /// <summary>Whirlpool: enter along its swirl (counter-clockwise seen from above) and it slings you; cut across it and you spin.</summary>
        private void WhirlContact(ref ItemObject o, int i, ref BoatState boat, in BoatTuning tuning, SimVec3 d)
        {
            byte bit = (byte)(1 << (i & 7));
            float r2 = d.SqrMagnitude;
            if (r2 > ItemRules.WhirlRadius * ItemRules.WhirlRadius)
            {
                o.Touched &= (byte)~bit;
                return;
            }
            if ((o.Touched & bit) != 0 || !o.Armed) return;
            o.Touched |= bit;

            var v = boat.Velocity.Flat;
            float speed = v.Magnitude;
            var radial = r2 > 1e-6f ? d / SimMath.Sqrt(r2) : SimVec3.Forward;
            var tangent = new SimVec3(-radial.Z, 0f, radial.X); // counter-clockwise
            float cos = speed > 1e-3f ? SimVec3.Dot(v / speed, tangent) : 0f;
            if (cos >= SimMath.Cos(ItemRules.WhirlSlingAngleDeg * SimMath.Deg2Rad))
            {
                boat.BoostTime = SimMath.Max(boat.BoostTime, ItemRules.WhirlSlingBoost);
                LastEvents[i] |= ItemEvents.Slung;
            }
            else if (BoatSimulator.ApplyHit(ref boat, tuning))
            {
                LastEvents[i] |= ItemEvents.Hit;
            }
        }

        /// <summary>A surging boat rides a moving crest; boats in front of it are bounced aside unless they hop the front edge.</summary>
        private void UpdateSurges(RaceSession race)
        {
            var sim = race.Sim;
            for (int i = 0; i < sim.BoatCount; i++)
            {
                ref var surge = ref Boats[i];
                if (surge.SurgeTime <= 0f || sim.Parked[i]) continue;
                ref var me = ref sim.State.Boats[i];
                var fwd = me.FlatForward;
                for (int j = 0; j < sim.BoatCount; j++)
                {
                    if (j == i || sim.Parked[j]) continue;
                    byte bit = (byte)(1 << (j & 7));
                    if ((surge.SurgeTouched & bit) != 0) continue;
                    ref var other = ref sim.State.Boats[j];
                    var d = (other.Position - me.Position).Flat;
                    float ahead = SimVec3.Dot(d, fwd);
                    float lateral = SimVec3.Dot(d, me.FlatRight);
                    if (ahead < 0f || ahead > ItemRules.SurgeReach || SimMath.Abs(lateral) > ItemRules.SurgeReach * 0.5f) continue;
                    surge.SurgeTouched |= bit;
                    if (Dodging(other))
                    {
                        LastEvents[j] |= ItemEvents.Dodged;
                        continue;
                    }
                    var push = me.FlatRight * (lateral >= 0f ? 1f : -1f) * ItemRules.SurgeBounce;
                    other.Velocity += push + new SimVec3(0f, 3f, 0f);
                    LastEvents[j] |= ItemEvents.Bounced;
                }
            }
        }
    }
}
