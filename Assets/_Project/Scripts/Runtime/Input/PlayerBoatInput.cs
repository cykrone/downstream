using Downstream.Core.Boat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Downstream.Input
{
    /// <summary>Anything that can drive a boat for one tick: a local player, an AI, a network peer or a replay.</summary>
    public interface IBoatInputSource
    {
        BoatInput ReadInput();
    }

    /// <summary>One local player's input, read from their own copy of the actions, paired to their device(s).</summary>
    public sealed class PlayerBoatInput : IBoatInputSource, System.IDisposable
    {
        private readonly InputActionAsset _actions;
        private readonly InputAction _throttle, _brake, _steer, _pitch, _hop, _item;

        public int PlayerIndex { get; }
        public InputActionAsset Actions => _actions;

        public PlayerBoatInput(int playerIndex, params InputDevice[] devices)
        {
            PlayerIndex = playerIndex;
            _actions = DownstreamControls.Create();
            _actions.devices = devices;
            var map = _actions.FindActionMap(DownstreamControls.RaceMap, throwIfNotFound: true);
            _throttle = map.FindAction("Throttle", true);
            _brake = map.FindAction("Brake", true);
            _steer = map.FindAction("Steer", true);
            _pitch = map.FindAction("Pitch", true);
            _hop = map.FindAction("HopDrift", true);
            _item = map.FindAction("UseItem", true);
            map.Enable();
        }

        public BoatInput ReadInput() => new BoatInput
        {
            Throttle = Mathf.Clamp(_throttle.ReadValue<float>() - _brake.ReadValue<float>(), -1f, 1f),
            Steer = Mathf.Clamp(_steer.ReadValue<float>(), -1f, 1f),
            Pitch = Mathf.Clamp(_pitch.ReadValue<float>(), -1f, 1f),
            HopDrift = _hop.IsPressed(),
            UseItem = _item.IsPressed(),
        };

        public void Dispose()
        {
            _actions.Disable();
            Object.Destroy(_actions);
        }
    }
}
