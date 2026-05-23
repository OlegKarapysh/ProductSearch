using System.Collections.Concurrent;
using System.Text;
using BenchmarkDotNet.Running;
using FullTextSearchPostgres;
using FullTextSearchPostgres.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FullTextSearchPostgres.Benchmarks.Diagnosers;

internal static class BenchmarkInspector
{
    public const string ConnectionString =
        "Host=localhost; Port=5433; Database=TestDB; Username=postgres; Password=admin";

    public sealed record Inspection(string Method, string Query, string Sql, string Plan, int RowCount);

    private static readonly ConcurrentDictionary<string, Inspection> Cache = new();

    public static string KeyFor(BenchmarkCase benchmarkCase)
    {
        var method = benchmarkCase.Descriptor.WorkloadMethod.Name;
        var query  = (string)benchmarkCase.Parameters.Items.First(p => p.Name == "Query").Value!;
        return $"{method}|{query}";
    }

    public static Inspection For(BenchmarkCase benchmarkCase) =>
        Cache.GetOrAdd(KeyFor(benchmarkCase), _ =>
        {
            var method = benchmarkCase.Descriptor.WorkloadMethod.Name;
            var query  = (string)benchmarkCase.Parameters.Items.First(p => p.Name == "Query").Value!;
            return Compute(method, query);
        });

    public static IReadOnlyCollection<Inspection> All => Cache.Values.ToArray();

    private static Inspection Compute(string method, string query)
    {
        var interceptor = new CaptureCommandInterceptor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(interceptor)
            .UseLoggerFactory(LoggerFactory.Create(_ => { }))
            .Options;

        using var db = new AppDbContext(options);

        IProductSearchService service = method switch
        {
            "Naive"     => new NaiveProductSearchService(db),
            "Smart"     => new SmartProductSearchService(db),
            "Fts"       => new FullTextProductSearchService(db),
            "FtsFast"   => new FastFullTextProductSearchService(db),
            "FtsRanked" => new RankedFastFullTextProductSearchService(db),
            _ => throw new ArgumentException($"Unknown benchmark method: {method}"),
        };

        // Execute the same code path the benchmark exercises so the interceptor
        // captures the exact SQL + parameters EF Core emits.
        var rows = service.SearchAsync(query).GetAwaiter().GetResult();

        var sql  = interceptor.LastSql ?? "(no SQL captured)";
        var plan = ExplainAnalyze(sql, interceptor.LastParameters);

        return new Inspection(method, query, sql, plan, rows.Count);
    }

    private static string ExplainAnalyze(string sql, IReadOnlyList<NpgsqlParameter> parameters)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "(no SQL captured)";

        using var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXPLAIN (ANALYZE, BUFFERS, VERBOSE) " + sql;
        foreach (var p in parameters)
        {
            cmd.Parameters.Add(new NpgsqlParameter
            {
                ParameterName = p.ParameterName,
                NpgsqlDbType  = p.NpgsqlDbType,
                Value         = p.Value,
            });
        }

        var sb = new StringBuilder();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            sb.AppendLine(reader.GetString(0));

        return sb.ToString();
    }
}
