
using BenchmarkDotNet.Running;

using RockEngine.Benchmarks;

var summary = BenchmarkRunner.Run<BenchmarkPinningObjects>();