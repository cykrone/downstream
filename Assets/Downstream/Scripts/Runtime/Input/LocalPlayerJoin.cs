using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace Downstream.Input
{
    /// <summary>
    /// Drop-in local joining for 1-4 players: a button pressed on an unclaimed gamepad or keyboard
    /// joins a new player bound to that device only (in a lobby, before the race is built). This replaces PlayerInputManager
    /// because the skeleton builds its actions in code; the shared-keyboard (left/right half) case
    /// from the research doc is a later addition.
    /// </summary>
    public sealed class LocalPlayerJoin : MonoBehaviour
    {
        public const int MaxPlayers = 4;

        [SerializeField] private bool _joinOnAnyButton;

        private readonly List<PlayerBoatInput> _players = new List<PlayerBoatInput>();
        private readonly HashSet<int> _claimedDevices = new HashSet<int>();
        private IDisposable _listener;

        public event Action<PlayerBoatInput> PlayerJoined;

        public IReadOnlyList<PlayerBoatInput> Players => _players;

        private void OnEnable()
        {
            if (_joinOnAnyButton) _listener = InputSystem.onAnyButtonPress.Call(OnButton);
        }

        private void OnDisable()
        {
            _listener?.Dispose();
            _listener = null;
        }

        private void OnDestroy()
        {
            foreach (var p in _players) p.Dispose();
            _players.Clear();
        }

        /// <summary>Joins a player on one or more devices (used by menus, tests and the greybox scene).</summary>
        public PlayerBoatInput Join(params InputDevice[] devices)
        {
            if (devices == null || devices.Length == 0 || _players.Count >= MaxPlayers) return null;
            foreach (var d in devices)
                if (d == null || _claimedDevices.Contains(d.deviceId)) return null;
            foreach (var d in devices) _claimedDevices.Add(d.deviceId);
            var player = new PlayerBoatInput(_players.Count, devices);
            _players.Add(player);
            PlayerJoined?.Invoke(player);
            return player;
        }

        /// <summary>
        /// Quick start for greybox testing: player 1 gets the keyboard plus the first gamepad,
        /// and every other connected gamepad becomes another split-screen player.
        /// </summary>
        public void JoinConnectedDevices()
        {
            if (_players.Count > 0) return;
            var pads = Gamepad.all;
            var keyboard = Keyboard.current;
            if (keyboard != null && pads.Count > 0) Join(keyboard, pads[0]);
            else if (keyboard != null) Join(keyboard);
            else if (pads.Count > 0) Join(pads[0]);
            for (int i = 1; i < pads.Count; i++) Join(pads[i]);
        }

        private void OnButton(InputControl control)
        {
            var device = control.device;
            if (device is Gamepad || device is Keyboard) Join(device);
        }
    }
}
