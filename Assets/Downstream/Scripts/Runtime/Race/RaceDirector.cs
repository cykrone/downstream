using System.Collections.Generic;
using Downstream.Boat;
using Downstream.Cameras;
using Downstream.Core.AI;
using Downstream.Core.Boat;
using Downstream.Core.Items;
using Downstream.Core.Race;
using Downstream.Core.Sim;
using Downstream.Input;
using Downstream.Items;
using Downstream.Water;
using UnityEngine;

namespace Downstream.Race
{
    /// <summary>
    /// Owns one race. Builds the water, the <see cref="RaceSession"/> and its items, spawns 8 boats
    /// (local players first, AI for the rest), runs the sim at a fixed 120 Hz from its own accumulator
    /// in Update, and hands each view a state interpolated between the last two ticks. PhysX never
    /// steps the boats; it only answers collision queries. Finished players are driven by AI.
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
        [SerializeField] private ItemWorldView _itemView;
        [SerializeField] private SpeedClass _speedClass = SpeedClass.Rapid;
        [SerializeField] private HullType _playerHull = HullType.Runabout;
        [SerializeField] private ItemRuleset _ruleset = ItemRuleset.Standard;
        [SerializeField] private AILevel _aiLevel = AILevel.Normal;
        [Tooltip("Distance between item buoy rows, metres (design: every 20-25 s of racing).")]
        [SerializeField] private float _buoyRowSpacing = 400f;
        [SerializeField] private LayerMask _worldCollision = ~0;
        [Tooltip("Start the race as soon as the scene loads, joining connected devices if nobody has joined.")]
        [SerializeField] private bool _autoStart = true;

        private RaceSession _race;
        private RaceSnapshot _previous;
        private readonly FixedStepClock _clock = new FixedStepClock(BoatSimulator.TickDelta);
        private readonly BoatInput[] _inputs = new BoatInput[BoatsPerRace];
        private readonly IBoatInputSource[] _humanSources = new IBoatInputSource[BoatsPerRace];
        private readonly RacerAI[] _drivers = new RacerAI[BoatsPerRace];
        private readonly ItemAI[] _gunners = new ItemAI[BoatsPerRace];
        private readonly RaceEvents[] _frameRaceEvents = new RaceEvents[BoatsPerRace];
        private readonly ItemEvents[] _frameItemEvents = new ItemEvents[BoatsPerRace];
        private readonly List<BoatView> _views = new List<BoatView>();
        private readonly List<ChaseCamera> _cameras = new List<ChaseCamera>();
        private RiverWaterCache _water;
        private CapsuleCollider _proxy;
        private int _humans;
        private uint _raceNumber;

        public RaceSession Race => _race;
        public RaceSimulation Simulation => _race?.Sim;
        public IReadOnlyList<BoatView> Views => _views;
        public IReadOnlyList<ChaseCamera> Cameras => _cameras;
        public int HumanCount => _humans;

        /// <summary>Race events gathered over the last rendered frame (several ticks), for the HUD.</summary>
        public RaceEvents FrameRaceEvents(int boat) => _frameRaceEvents[boat];
        public ItemEvents FrameItemEvents(int boat) => _frameItemEvents[boat];

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

            ClearRace();

            // The water and its mesh only change with the river asset, so a rematch reuses them.
            if (_water.Source != _river)
            {
                _water = new RiverWaterCache { Source = _river, Water = _river.CreateWater() };
                if (_waterMesh != null) _waterMesh.Build(_water.Water, _river);
            }
            var water = _water.Water;
            if (_water.Lines == null)
            {
                // Racing lines depend only on the river, so they are generated once per river, like the water.
                _water.Lines = RacingLineSet.Build(new RaceTrack(_river.SampleCentreline()), water);
            }
            var lines = _water.Lines;
            var track = lines.Track;
            _raceNumber++;
            var difficulty = AIDifficulty.For(_aiLevel);
            // Finished players hand over to a mistake-free driver on their own boat.
            var autopilot = AIDifficulty.For(AILevel.Expert);
            autopilot.MistakesMin = autopilot.MistakesMax = 0;

            if (_players != null) _players.JoinConnectedDevices();
            _humans = _players != null ? Mathf.Min(_players.Players.Count, BoatsPerRace) : 0;

            var tunings = new BoatTuning[BoatsPerRace];
            var starts = new BoatState[BoatsPerRace];
            for (int i = 0; i < BoatsPerRace; i++)
            {
                // Humans start at the back of the grid, as in a kart racer's first race.
                int slot = BoatsPerRace - 1 - i;
                bool human = i < _humans;
                var rival = human ? new RivalProfile { Name = $"P{i + 1}", Hull = _playerHull } : RivalRoster.Get(i - _humans);
                var hull = rival.Hull;
                tunings[i] = BoatTuning.Create(HullStats.For(hull), _speedClass);
                starts[i] = RaceGrid.Slot(track, slot);
                _humanSources[i] = i < _humans ? _players.Players[i] : null;
                _drivers[i] = new RacerAI(lines, tunings[i], rival, human ? autopilot : difficulty, i, _raceNumber);
                _gunners[i] = new ItemAI(i, _raceNumber);
                _gunners[i].Apply(human ? autopilot : difficulty);
            }

            var sim = new RaceSimulation(water, track, tunings, starts);
            _race = new RaceSession(sim);
            _race.Items = new ItemSystem(BoatsPerRace, ItemSystem.Layout(track, water, _buoyRowSpacing), 0x5EED0000u + _raceNumber, _ruleset);
            _previous = new RaceSnapshot(BoatsPerRace);
            _previous.CopyFrom(sim.State);

            for (int i = 0; i < BoatsPerRace; i++)
            {
                var view = Instantiate(_boatPrefab, starts[i].Position.ToUnity(), UnityConversions.BoatRotation(starts[i]));
                view.name = i < _humans ? $"Boat P{i + 1}" : $"Boat {_drivers[i].Profile.Name}";
                view.BoatIndex = i;
                view.Present(starts[i]);
                _views.Add(view);
            }

            if (_proxy == null) _proxy = CreateCollisionProxy(tunings[0]);
            sim.Collider = new PhysicsBoatCollider(_proxy, _worldCollision);

            if (_itemView != null) _itemView.Bind(_race.Items);
            SpawnCameras(Mathf.Max(1, _humans));
            _clock.Reset();
        }

        /// <summary>Runs the same river again with the same players (design: rematch in one press).</summary>
        public void Rematch() => StartRace();

        private void ClearRace()
        {
            foreach (var v in _views) if (v != null) Destroy(v.gameObject);
            foreach (var c in _cameras) if (c != null) Destroy(c.gameObject);
            _views.Clear();
            _cameras.Clear();
            _race = null;
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

        private void SpawnCameras(int players)
        {
            if (_cameraPrefab == null) return;
            for (int i = 0; i < players; i++)
            {
                var cam = Instantiate(_cameraPrefab);
                cam.name = $"Camera P{i + 1}";
                cam.Target = _views[i];
                cam.Water = _water.Water;
                cam.GetComponent<Camera>().rect = SplitScreenLayout.ViewportFor(i, players);
                _cameras.Add(cam);
            }
        }

        private void Update()
        {
            if (_race == null) return;
            var sim = _race.Sim;

            for (int i = 0; i < BoatsPerRace; i++)
            {
                _frameRaceEvents[i] = RaceEvents.None;
                _frameItemEvents[i] = ItemEvents.None;
            }

            int steps = _clock.Advance(Time.deltaTime);
            for (int k = 0; k < steps; k++)
            {
                _previous.CopyFrom(sim.State);
                for (int i = 0; i < BoatsPerRace; i++)
                {
                    bool human = _humanSources[i] != null && !_race.Status[i].Finished;
                    if (human)
                    {
                        _inputs[i] = _humanSources[i].ReadInput();
                    }
                    else
                    {
                        _inputs[i] = _drivers[i].Think(sim, _race.Items.Boats[i].Held);
                        _inputs[i].UseItem = _gunners[i].Think(_race, _race.Items);
                    }
                }
                _race.Step(_inputs);
                for (int i = 0; i < BoatsPerRace; i++)
                {
                    _views[i].OnSimEvents(sim.LastEvents[i]);
                    _frameRaceEvents[i] |= _race.LastEvents[i];
                    _frameItemEvents[i] |= _race.Items.LastEvents[i];
                    // A respawn is a teleport: do not interpolate across it.
                    if ((_race.LastEvents[i] & RaceEvents.Respawned) != 0) _previous.Boats[i] = sim.State.Boats[i];
                }
            }

            foreach (var cam in _cameras)
                if (cam != null && cam.Target != null) cam.Celebrate = _race.Status[cam.Target.BoatIndex].Finished;

            float alpha = _clock.Alpha;
            // The water is drawn at the same interpolated time as the boats.
            float drawTime = (sim.Tick - 1 + alpha) * BoatSimulator.TickDelta;
            if (_waterMesh != null) _waterMesh.SetRaceTime(drawTime);
            for (int i = 0; i < BoatsPerRace; i++)
            {
                bool away = _race.Status[i].IsRespawning;
                if (_views[i].gameObject.activeSelf == away) _views[i].gameObject.SetActive(!away);
                _views[i].Water = _water.Water;
                _views[i].RaceTime = drawTime;
                if (!away) _views[i].Present(BoatState.Interpolate(_previous.Boats[i], sim.State.Boats[i], alpha));
            }
        }

        private struct RiverWaterCache
        {
            public RiverDefinition Source;
            public Core.Water.RiverWater Water;
            public RacingLineSet Lines;
        }
    }
}
