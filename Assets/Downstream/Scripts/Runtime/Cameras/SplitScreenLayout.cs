using UnityEngine;

namespace Downstream.Cameras
{
    /// <summary>
    /// Viewport rectangles for local split-screen, from the design doc: horizontal halves for
    /// 2 players, quadrants for 3-4. One URP base camera per player, never overlapping.
    /// </summary>
    public static class SplitScreenLayout
    {
        public static Rect ViewportFor(int playerIndex, int playerCount)
        {
            switch (playerCount)
            {
                case 1:
                    return new Rect(0f, 0f, 1f, 1f);
                case 2:
                    return playerIndex == 0 ? new Rect(0f, 0.5f, 1f, 0.5f) : new Rect(0f, 0f, 1f, 0.5f);
                default:
                    float x = (playerIndex % 2) * 0.5f;
                    float y = playerIndex < 2 ? 0.5f : 0f;
                    return new Rect(x, y, 0.5f, 0.5f);
            }
        }

        /// <summary>HUD scale per player count (design: 85% in 4-player split-screen).</summary>
        public static float HudScale(int playerCount) => playerCount >= 3 ? 0.85f : 1f;
    }
}
