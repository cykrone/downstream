using Downstream.Core.Water;
using Downstream.Water;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Downstream.Tests
{
    /// <summary>
    /// The design rule "what the player sees is what pushes the boat" holds only if the shader's
    /// River Field sampler returns the physics' numbers. This evaluates RiverField.hlsl on the GPU
    /// at random points and times and compares it with RiverWater.Sample on the CPU.
    /// </summary>
    public sealed class RiverFieldGpuParityTests
    {
        private const string ComputePath = "Assets/_Project/Shaders/RiverFieldParity.compute";

        [Test]
        public void GpuSamplerMatchesCpuSampler()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("No compute shader support on this device.");
            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            Assert.IsNotNull(compute, "Missing " + ComputePath);

            var river = ScriptableObject.CreateInstance<RiverDefinition>();
            river.ApplyGreyboxDefaults();
            var water = river.CreateWater();
            var field = water.Field;

            const int count = 4096;
            var rng = new System.Random(1234);
            var points = new Vector4[count];
            var times = new[] { 0f, 3.7f, 30f, 60f, 95f, 140f, 200f };
            float width = field.TexelCountX * field.CellSize, length = field.TexelCountZ * field.CellSize;
            for (int i = 0; i < count; i++)
            {
                // Mostly inside the field, some just outside, some on exact texel centres and corners.
                float x = field.OriginX + (float)(rng.NextDouble() * 1.1 - 0.05) * width;
                float z = field.OriginZ + (float)(rng.NextDouble() * 1.1 - 0.05) * length;
                if (i % 7 == 0) { x = Mathf.Round(x / field.CellSize) * field.CellSize; z = Mathf.Round(z / field.CellSize) * field.CellSize; }
                if (i % 11 == 0) { x = (Mathf.Floor(x / field.CellSize) + 0.5f) * field.CellSize; }
                points[i] = new Vector4(x, z, times[i % times.Length], 0f);
            }

            var gpu = new RiverFieldGpu();
            var pointsBuffer = new ComputeBuffer(count, 16);
            var levelBuffer = new ComputeBuffer(count, 16);
            var normalBuffer = new ComputeBuffer(count, 16);
            var flagsBuffer = new ComputeBuffer(count, 16);
            try
            {
                gpu.Upload(water);
                Assert.Greater(gpu.TileCount, 0, "The greybox river should upload at least one tile.");
                pointsBuffer.SetData(points);
                int kernel = compute.FindKernel("Sample");
                compute.SetBuffer(kernel, "_Points", pointsBuffer);
                compute.SetBuffer(kernel, "_OutLevel", levelBuffer);
                compute.SetBuffer(kernel, "_OutNormal", normalBuffer);
                compute.SetBuffer(kernel, "_OutFlags", flagsBuffer);
                compute.SetInt("_Count", count);
                compute.Dispatch(kernel, (count + 63) / 64, 1, 1);

                var level = new Vector4[count];
                var normal = new Vector4[count];
                var flags = new Vector4[count];
                levelBuffer.GetData(level);
                normalBuffer.GetData(normal);
                flagsBuffer.GetData(flags);

                int wet = 0;
                for (int i = 0; i < count; i++)
                {
                    var cpu = water.Sample(points[i].x, points[i].y, points[i].z);
                    var stat = field.SampleStatic(points[i].x, points[i].y);
                    string at = $"at ({points[i].x:F3}, {points[i].y:F3}) t={points[i].z}";
                    Assert.AreEqual(stat.HasData, flags[i].z > 0.5f, "static HasData " + at);
                    Assert.AreEqual(cpu.IsWet, flags[i].y > 0.5f, "IsWet " + at);
                    if (!cpu.IsWet) continue;
                    wet++;
                    Assert.AreEqual(cpu.SurfaceHeight, level[i].x, 2e-3f, "SurfaceHeight " + at);
                    Assert.AreEqual(cpu.Depth, level[i].y, 2e-3f, "Depth " + at);
                    Assert.AreEqual(cpu.Flow.X, level[i].z, 1e-3f, "FlowX " + at);
                    Assert.AreEqual(cpu.Flow.Z, level[i].w, 1e-3f, "FlowZ " + at);
                    Assert.AreEqual(cpu.RiverDistance, normal[i].w, 1e-3f, "RiverDistance " + at);
                    Assert.AreEqual((int)cpu.Features, Mathf.RoundToInt(flags[i].x), "Features " + at);
                    Assert.AreEqual(cpu.Normal.X, normal[i].x, 2e-3f, "NormalX " + at);
                    Assert.AreEqual(cpu.Normal.Y, normal[i].y, 2e-3f, "NormalY " + at);
                    Assert.AreEqual(cpu.Normal.Z, normal[i].z, 2e-3f, "NormalZ " + at);
                }
                Assert.Greater(wet, count / 20, "Expected a fair share of sample points on the water.");
            }
            finally
            {
                pointsBuffer.Release();
                levelBuffer.Release();
                normalBuffer.Release();
                flagsBuffer.Release();
                gpu.Release();
                Object.DestroyImmediate(river);
            }
        }
    }
}
