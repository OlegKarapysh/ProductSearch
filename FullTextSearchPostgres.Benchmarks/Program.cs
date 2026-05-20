using BenchmarkDotNet.Running;
using FullTextSearchPostgres.Benchmarks.Benchmarks;

BenchmarkRunner.Run<ProductSearchBenchmark>();
