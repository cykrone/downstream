using System.Collections.Generic;
using Downstream.Boat;
using Downstream.Cameras;
using Downstream.Core.AI;
using Downstream.Core.Boat;
using Downstream.Core.Math;
using Downstream.Core.Race;
using Downstream.Core.Sim;
using Downstream.Input;
using Downstream.Water;
using UnityEngine;

namespace Downstream.Race
{
    /// <summary>
    /// Owns one race. Builds the water and the <see cref="RaceSimulation"/>, spawns 8 boats
    /// (local players first, AI for the rest), runs the sim at a fixed 120 Hz from its own
    /// accumulator in Update, and hands each view a state interpolated between the last two ticks.
    /// PhysX never steps the boats; it only answers collision queries.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class RaceDirector : MonoBehaviour
    {
        public const int BoatsPerRace = 8;

        [SerializeField] private RiverDefinition _river;
        [SerializeField] private BoatView _boatPrefab;
        [SerializeField] private ChaseCamera _cameraPrefab;
        [SerializeField] private GreyboxWaterMesh _waterMesh;
        [SerializeField] private LocalPlayerJoin _players;
        [SerializeField] private SpeedClass _speedClass = SpeedClass.Rapid;
        [SerializeField] private HullType _playerHull = HullType.Runabout;
        [SerializeField] private LayerMask _worldCollision = ~0;
        [Tooltip("Start the race as soon as the scene loads, joining connected devices if nobody has joined.")]
        [SerializeField] private bool _autoStart = true;

        private RaceSimulation _sim;
        private RaceSnapshot _previous;
        private readonly FixedStepClock _clock = new FixedStepClock(BoatSimulator.TickDelta);
        private readonly BoatInput[] _inputs = new BoatInput[BoatsPerRace];
        private readonly IBoatInputSource[] _humanSources = new IBoatInputSource[BoatsPerRace];
        private readonly LineFollowerAI[] _ai = new LineFollowerAI[BoatsPerRace];
        private readonly List<BoatView> _views = new List<BoatView>();
        private readonly List<ChaseCamera> _cameras = new List<ChaseCamera>();

        public RaceSimulation Simulation => _sim;
        public IReadOnlyList<BoatView> Views => _views;

        private void Awake()
        {
            // The sim owns time; PhysX is query-only.
            Physics.simulationMode = SimulationMode.Script;
        }

        private void Start()
        {
            if (_autoStart) StartRace();
        }

        public void StartRace()
        {
            if (_river == null || _boatPrefab == null)
            {
                Debug.LogError("RaceDirector needs a RiverDefinition and a boat prefab.", this);
                enabled = false;
                return;
            }

            var water = _river.CreateWater();
            if (_waterMesh != null) _waterMesh.Build(water);
            var track = new RaceTrack(_river.SampleCentreline());

            if (_players != null) _players.JoinConnectedDevices();
            int humans = _players != null ? _players.Players.Count : 0;

            var tunings = new BoatTuning[BoatsPerRace];
            var starts = new BoatState[BoatsPerRace];
            var aiHulls = new[] { HullType.Skiff, HullType.Jetboat, HullType.Runabout, HullType.Hydrofoil, HullType.Tug };
            for (int i = 0; i < BoatsPerRace; i++)
            {
                var hull = i < humans ? _playerHull : aiHulls[i % aiHulls.Length];
                tunings[i] = BoatTuning.Create(HullStats.For(hull), _speedClass);
                starts[i] = GridSlot(track, i);
                if (i < humans) _humanSources[i] = _players.Players[i];
                else _ai[i] = new LineFollowerAI(track) { LateralOffset = ((i % 3) - 1) * 4f, Throttle = 0.92f + 0.01f * i };
            }

            _sim = new RaceSimulation(water, track, tunings, starts);
            _previous = new RaceSnapshot(BoatsPerRace);
            _previous.CopyFrom(_sim.State);

            for (int i = 0; i < BoatsPerRace; i++)
            {
                var view = Instantiate(_boatPrefab, starts[i].Position.ToUnity(), UnityConversions.BoatRotation(starts[i]));
                view.name = i < humans ? $"Boat P{i + 1}" : $"Boat AI{i}";
                view.BoatIndex = i;
                _views.Add(view);
            }

            _sim.Collider = new PhysicsBoatCollider(CreateCollisionProxy(tunings[0]), _worldCollision);

            SpawnCameras(Mathf.Max(1, humans));
            _clock.Reset();
        }

        /// <summary>
        /// One shared query shape for every boat. It is a trigger so overlap queries ignore it;
        /// ComputePenetration is given each boat's pose explicitly, so where the proxy sits does not matter.
        /// </summary>
        private CapsuleCollider CreateCollisionProxy(in BoatTuning tuning)
        {
            var go = new GameObject("Boat Collision Proxy");
            go.transform.SetParent(transform, false);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.direction = 2; // along Z, the boat's length
            capsule.radius = tuning.HullBeam * 0.5f;
            capsule.height = tuning.HullLength;
            return capsule;
        }

        private static BoatState GridSlot(RaceTrack track, int index)
        {
            // Two columns, staggered 8 m apart, facing downstream.
            float distance = 30f - (index / 2) * 8f;
            var p = track.PointAt(distance);
            var ahead = track.PointAt(distance + 2f);
            var dir = (ahead - p).Flat.Normalized;
            var side = new SimVec3(dir.Z, 0f, -dir.X) * ((index % 2 == 0) ? -3f : 3f);
            return BoatState.At(p + side, SimMath.Atan2(dir.X, dir.Z));
        }

        private void SpawnCameras(int players)
        {
            if (_cameraPrefab == null) return;
            for (int i = 0; i < players; i++)
            {
                var cam = Instantiate(_cameraPrefab);
                cam.name = $"Camera P{i + 1}";
                cam.Target = _views[i];
                cam.GetComponent<Camera>().rect = SplitScreenLayout.ViewportFor(i, players);
                _cameras.Add(cam);
            }
        }

        private void Update()
        {
            if (_sim == null) return;

            int steps = _clock.Advance(Time.deltaTime);
            for (int k = 0; k < steps; k++)
            {
                _previous.CopyFrom(_sim.State);
                for (int i = 0; i < BoatsPerRace; i++)
                    _inputs[i] = _humanSources[i] != null ? _humanSources[i].ReadInput() : _ai[i].Think(_sim.State.Boats[i]);
                _sim.Step(_inputs);
                for (int i = 0; i < BoatsPerRace; i++)
                    _views[i].OnSimEvents(_sim.LastEvents[i]);
            }

            float alpha = _clock.Alpha;
            for (int i = 0; i < BoatsPerRace; i++)
                _views[i].Present(BoatState.Interpolate(_previous.Boats[i], _sim.State.Boats[i], alpha));
        }
    }
}
