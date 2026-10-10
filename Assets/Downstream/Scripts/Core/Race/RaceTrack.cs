using System;
using Downstream.Core.Math;

namespace Downstream.Core.Race
{
    /// <summary>
    /// The river centreline as a polyline with cumulative distance, used for race positions
    /// ("distance along the river spline"), respawn and the HUD river strip. The Unity side builds
    /// it by sampling the authored spline at a fixed spacing when a track loads.
    /// </summary>
    public sealed class RaceTrack
    {
        private readonly SimVec3[] _points;
        private readonly float[] _distance;

        public float Length { get; }
        public int PointCount => _points.Length;

        public RaceTrack(SimVec3[] points)
        {
            if (points == null || points.Length < 2) throw new ArgumentException("A track needs at least two points.", nameof(points));
            _points = (SimVec3[])points.Clone();
            _distance = new float[points.Length];
            for (int i = 1; i < points.Length; i++)
                _distance[i] = _distance[i - 1] + (points[i] - points[i - 1]).Flat.Magnitude;
            Length = _distance[points.Length - 1];
        }

        public SimVec3 Point(int i) => _points[i];

        /// <summary>
        /// Projects a world position onto the centreline and returns the distance along it.
        /// <paramref name="hint"/> is the segment found last time; the search only looks nearby,
        /// so a boat cannot jump to a parallel stretch of a meandering river.
        /// </summary>
        public float Project(SimVec3 position, ref int hint, int searchRadius = 8)
        {
            int lo = SimMath.Max(0, hint - searchRadius);
            int hi = SimMath.Min(_points.Length - 2, hint + searchRadius);
            float best = float.MaxValue, bestDistance = 0f;
            int bestSeg = hint;
            var p = position.Flat;
            for (int i = lo; i <= hi; i++)
            {
                var a = _points[i].Flat;
                var b = _points[i + 1].Flat;
                var ab = b - a;
                float len2 = ab.SqrMagnitude;
                float u = len2 > 1e-6f ? SimMath.Clamp01(SimVec3.Dot(p - a, ab) / len2) : 0f;
                var c = a + ab * u;
                float d2 = (p - c).SqrMagnitude;
                if (d2 < best)
                {
                    best = d2;
                    bestSeg = i;
                    bestDistance = _distance[i] + (_distance[i + 1] - _distance[i]) * u;
                }
            }
            hint = bestSeg;
            return bestDistance;
        }

        /// <summary>Position on the centreline at a distance along it.</summary>
        public SimVec3 PointAt(float distance)
        {
            distance = SimMath.Clamp(distance, 0f, Length);
            int i = Array.BinarySearch(_distance, distance);
            if (i >= 0) return _points[i];
            i = ~i;
            if (i <= 0) return _points[0];
            if (i >= _points.Length) return _points[_points.Length - 1];
            float u = (distance - _distance[i - 1]) / SimMath.Max(_distance[i] - _distance[i - 1], 1e-6f);
            return SimVec3.Lerp(_points[i - 1], _points[i], u);
        }
    }
}
