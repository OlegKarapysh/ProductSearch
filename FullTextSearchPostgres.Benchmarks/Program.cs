using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using FullTextSearchPostgres.Benchmarks.Benchmarks;
using FullTextSearchPostgres.Benchmarks.Diagnosers;

var config = DefaultConfig.Instance
    .AddDiagnoser(new RowCountDiagnoser())
    .AddDiagnoser(new SqlPlanDiagnoser());

BenchmarkRunner.Run<ProductSearchBenchmark>(config);
