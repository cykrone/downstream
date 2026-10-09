using Downstream.Core.Boat;
using UnityEngine;

namespace Downstream.Boat
{
    /// <summary>
    /// Presents one simulated boat. Holds no gameplay state: every frame it is handed an
    /// interpolated <see cref="BoatState"/> and places itself there. Audio, wake VFX and the
    /// pilot animation hang off the events it receives.
    /// </summary>
    public sealed class BoatView : MonoBehaviour
    {
        [SerializeField] private Transform _hull;

        public BoatState State { get; private set; }
        public BoatEvents LastEvents { get; private set; }
        public int BoatIndex { get; set; }

        public void Present(in BoatState state)
        {
            State = state;
            transform.SetPositionAndRotation(state.Position.ToUnity(), UnityConversions.BoatRotation(state));
        }

        public void OnSimEvents(BoatEvents events)
        {
            LastEvents = events;
            // Hook point for audio, VFX and HUD: landing slaps, drift sparks by tier, boost trails.
        }

        private void Reset()
        {
            _hull = transform;
        }
    }
}
