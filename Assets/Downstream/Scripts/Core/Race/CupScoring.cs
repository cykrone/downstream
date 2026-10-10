using System;

namespace Downstream.Core.Race
{
    /// <summary>
    /// Cup points (design: 4 rivers per cup, 15-12-10-8-6-4-2-1 per finish) and the running table.
    /// Ties go to the boat that placed better in the most recent race.
    /// </summary>
    public sealed class CupStandings
    {
        private static readonly int[] PointsByPlace = { 15, 12, 10, 8, 6, 4, 2, 1 };

        public static int PointsFor(int place) => place >= 1 && place <= PointsByPlace.Length ? PointsByPlace[place - 1] : 0;

        private readonly int[] _points;
        private readonly int[] _lastPlace;

        public int RacesRun { get; private set; }

        public CupStandings(int boats)
        {
            _points = new int[boats];
            _lastPlace = new int[boats];
        }

        public int PointsOf(int boat) => _points[boat];

        public void AddRace(RaceResult[] results)
        {
            foreach (var r in results)
            {
                _points[r.Boat] += PointsFor(r.Place);
                _lastPlace[r.Boat] = r.Place;
            }
            RacesRun++;
        }

        /// <summary>Boat indices, cup leader first.</summary>
        public int[] Order()
        {
            var order = new int[_points.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = _points[b].CompareTo(_points[a]);
                if (c != 0) return c;
                c = _lastPlace[a].CompareTo(_lastPlace[b]);
                return c != 0 ? c : a.CompareTo(b);
            });
            return order;
        }
    }
}
