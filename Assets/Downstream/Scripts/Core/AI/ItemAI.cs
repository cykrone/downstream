using Downstream.Core.Boat;
using Downstream.Core.Items;
using Downstream.Core.Math;
using Downstream.Core.Race;

namespace Downstream.Core.AI
{
    /// <summary>
    /// When an AI boat presses the item button (design: "fires at targets in a 25 degree cone and holds
    /// shields behind when a threat is behind it"). Like the driving AI it only produces button presses,
    /// so it can do nothing a player cannot. One instance per AI boat.
    /// </summary>
    public sealed class ItemAI
    {
        public const float ConeDegrees = 25f;
        public const float AttackRange = 60f;
        public const float ThreatRange = 20f;
        public const float DropRange = 8f;

        /// <summary>Seconds to sit on a new item before using it. Easy AI uses items late (design).</summary>
        public float Patience { get; set; } = 0.6f;

        /// <summary>Chance of using each item at all; Easy AI sometimes sits on one for good (design).</summary>
        public float UseChance { get; set; } = 1f;

        private readonly int _boat;
        private float _heldFor;
        private bool _holding;
        private bool _pressedLastTick;
        private bool _keepThisOne;
        private SimRandom _rng;

        public ItemAI(int boat, ulong seed = 0)
        {
            _boat = boat;
            _rng = new SimRandom(seed + (ulong)boat, 0x17E3u);
        }

        /// <summary>Sets patience and willingness from a difficulty level.</summary>
        public void Apply(in AIDifficulty difficulty)
        {
            Patience = difficulty.ItemPatience;
            UseChance = difficulty.ItemUseChance;
        }

        /// <summary>Returns the state of the item button for this tick.</summary>
        public bool Think(RaceSession race, ItemSystem items)
        {
            var sim = race.Sim;
            var mine = items.Boats[_boat];
            if (race.Phase != RacePhase.Racing || race.Status[_boat].IsRespawning)
                return Release();

            // Trailing something behind: keep it there while a rival is close behind, drop it when they are on us.
            if (mine.Behind != ItemType.None)
            {
                float behind = NearestBehind(sim, out _);
                if (behind < DropRange || behind > ThreatRange * 1.5f) return Release();
                return Hold();
            }

            if (mine.Held == ItemType.None)
            {
                _heldFor = 0f;
                return Release();
            }

            // Each new item gets one roll on whether this boat will use it at all.
            if (_heldFor == 0f) _keepThisOne = UseChance < 1f && _rng.NextFloat() >= UseChance;
            _heldFor += BoatSimulator.TickDelta;
            if (_heldFor < Patience || _keepThisOne) return Release();

            var held = mine.Held;
            if (ItemRules.CanHoldBehind(held) && NearestBehind(sim, out _) < ThreatRange)
                return Hold();

            bool fire;
            switch (held)
            {
                case ItemType.Pike:
                case ItemType.TriplePike:
                case ItemType.Whirl:
                case ItemType.LilyMine:
                    fire = TargetAhead(sim);
                    break;
                case ItemType.LogJam:
                    fire = NearestBehind(sim, out _) < ThreatRange * 2f;
                    break;
                default:
                    fire = true; // boosts, shield, Wake Blaster, Kingfisher, Dam Burst: use them
                    break;
            }
            return fire ? Tap() : Release();
        }

        private bool Tap()
        {
            // A tap is one tick down, one tick up.
            bool press = !_pressedLastTick;
            _pressedLastTick = press;
            _holding = false;
            return press;
        }

        private bool Hold()
        {
            _holding = true;
            _pressedLastTick = true;
            return true;
        }

        private bool Release()
        {
            _holding = false;
            _pressedLastTick = false;
            return false;
        }

        public bool IsHolding => _holding;

        private bool TargetAhead(Sim.RaceSimulation sim)
        {
            var me = sim.State.Boats[_boat];
            var fwd = me.FlatForward;
            float cos = SimMath.Cos(ConeDegrees * 0.5f * SimMath.Deg2Rad);
            for (int j = 0; j < sim.BoatCount; j++)
            {
                if (j == _boat || sim.Parked[j]) continue;
                var d = (sim.State.Boats[j].Position - me.Position).Flat;
                float dist = d.Magnitude;
                if (dist < 1e-3f || dist > AttackRange) continue;
                if (SimVec3.Dot(d / dist, fwd) >= cos) return true;
            }
            return false;
        }

        private float NearestBehind(Sim.RaceSimulation sim, out int boat)
        {
            var me = sim.State.Boats[_boat];
            var fwd = me.FlatForward;
            float best = float.MaxValue;
            boat = -1;
            for (int j = 0; j < sim.BoatCount; j++)
            {
                if (j == _boat || sim.Parked[j]) continue;
                var d = (sim.State.Boats[j].Position - me.Position).Flat;
                if (SimVec3.Dot(d, fwd) >= 0f) continue;
                float dist = d.Magnitude;
                if (dist < best)
                {
                    best = dist;
                    boat = j;
                }
            }
            return best;
        }
    }
}
