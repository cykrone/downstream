using Downstream.Boat;
using NUnit.Framework;
using UnityEngine;

namespace Downstream.Tests.EditMode.Runtime
{
    public sealed class BoatAnimatorTests
    {
        /// <summary>
        /// Steps a whole stroke cycle on both sides, at rest, in the air and in between, and checks every
        /// point of the shaft and both blades against the hull section at its height and station: the paddle
        /// never enters the hull and keeps most of the clearance margin.
        /// </summary>
        [Test]
        public void PaddleStaysOutsideTheHullThroughTheWholeStroke()
        {
            float worst = float.MaxValue; string where = "";
            foreach (float raise in new[] { 0f, 0.3f, 0.7f, 1f })
            for (int step = 0; step <= 360; step++)
            {
                float a = step * Mathf.Deg2Rad;
                var pose = BoatAnimator.SolvePaddle(Mathf.Sin(a), Mathf.Cos(a), raise);
                var right = pose.Rotation * Vector3.right;
                var up = pose.Rotation * Vector3.up;
                var fwd = pose.Rotation * Vector3.forward;
                for (int i = -80; i <= 80; i++)
                {
                    float along = BoatAnimator.ShaftHalfLength * i / 80f;
                    bool blade = Mathf.Abs(along) >= BoatAnimator.BladeStart;
                    float r = blade ? BoatAnimator.BladeHalfWidth : BoatAnimator.ShaftRadius;
                    for (int q = 0; q < 8; q++)
                    {
                        float ang = q * Mathf.PI / 4f;
                        var p = pose.Position + right * along + (up * Mathf.Cos(ang) + fwd * Mathf.Sin(ang)) * r + BlockBoat.Hips;
                        float section = BlockBoat.HalfWidthAt(p.y, p.z);
                        if (section <= 0f) continue;
                        float gap = Mathf.Abs(p.x) - section;
                        if (gap < worst) { worst = gap; where = $"phase {step} raise {raise} along {along:F2}"; }
                    }
                }
            }
            Assert.That(worst, Is.GreaterThan(BoatAnimator.HullClearance * 0.5f), $"paddle too close to the hull at {where}");
        }

        [Test]
        public void StrokeIsMirroredLeftAndRight()
        {
            for (int step = 0; step < 360; step += 15)
            {
                float a = (step + 7f) * Mathf.Deg2Rad; // off the dip = 0 instant, where "side" is a convention
                var r = BoatAnimator.SolvePaddle(Mathf.Sin(a), Mathf.Cos(a), 0f);
                // The left stroke is half a cycle on: both the dip and the sweep flip.
                var l = BoatAnimator.SolvePaddle(-Mathf.Sin(a), -Mathf.Cos(a), 0f);
                Assert.That(l.Position.x, Is.EqualTo(-r.Position.x).Within(1e-4f));
                Assert.That(l.Position.y, Is.EqualTo(r.Position.y).Within(1e-4f));
                Assert.That(l.Position.z, Is.EqualTo(r.Position.z).Within(1e-4f));
                var tipR = r.Position + r.Rotation * Vector3.right * BoatAnimator.ShaftHalfLength;
                var tipL = l.Position + l.Rotation * Vector3.left * BoatAnimator.ShaftHalfLength;
                Assert.That(tipL.x, Is.EqualTo(-tipR.x).Within(1e-3f));
                Assert.That(tipL.y, Is.EqualTo(tipR.y).Within(1e-3f));
                Assert.That(tipL.z, Is.EqualTo(tipR.z).Within(1e-3f));
            }
        }

        [Test]
        public void DippedBladeReachesTheWater()
        {
            // Water sits 0.1 m below the hull origin (the hull parts are raised 0.1 m above the boat root).
            var pose = BoatAnimator.SolvePaddle(1f, 0f, 0f);
            var tip = pose.Position + pose.Rotation * Vector3.right * BoatAnimator.ShaftHalfLength + BlockBoat.Hips;
            Assert.That(tip.y, Is.LessThan(-0.25f), "blade tip should be well under the waterline");
            Assert.That(tip.x, Is.GreaterThan(1.15f), "blade dips outboard of the gunwale");
        }
    }
}
