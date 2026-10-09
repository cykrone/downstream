using Downstream.Core.Water;
using UnityEngine;

namespace Downstream.Water
{
    /// <summary>Draws flow arrows and feature flags of the gameplay water in the Scene view.</summary>
    public sealed class RiverFieldGizmos : MonoBehaviour
    {
        [SerializeField] private RiverDefinition _river;
        [SerializeField] private float _spacing = 6f;
        [SerializeField] private float _arrowScale = 0.5f;
        [SerializeField] private float _raceTime;

        private RiverWater _water;
        private RiverDefinition _builtFrom;

        private void OnDrawGizmosSelected()
        {
            if (_river == null) return;
            if (_water == null || _builtFrom != _river)
            {
                _water = _river.CreateWater();
                _builtFrom = _river;
            }

            var f = _water.Field;
            float maxX = f.OriginX + f.TexelCountX * f.CellSize;
            float maxZ = f.OriginZ + f.TexelCountZ * f.CellSize;
            for (float z = f.OriginZ; z < maxZ; z += _spacing)
            for (float x = f.OriginX; x < maxX; x += _spacing)
            {
                var s = _water.Sample(x, z, _raceTime);
                if (!s.IsWet) continue;
                var p = new Vector3(x, s.SurfaceHeight + 0.1f, z);
                Gizmos.color = (s.Features & WaterFeature.CurrentLane) != 0 ? Color.white
                    : (s.Features & WaterFeature.WaterfallLip) != 0 ? Color.red
                    : (s.Features & WaterFeature.Floodable) != 0 ? new Color(0.6f, 0.4f, 0.2f)
                    : Color.cyan;
                Gizmos.DrawLine(p, p + s.Flow.ToUnity() * _arrowScale);
            }
        }

        private void OnValidate() => _water = null;
    }
}
