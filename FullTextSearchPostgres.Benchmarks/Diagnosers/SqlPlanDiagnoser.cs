using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;

namespace FullTextSearchPostgres.Benchmarks.Diagnosers;

// Captures the SQL EF Core emits for each (method, query) case, then runs
// EXPLAIN (ANALYZE, BUFFERS, VERBOSE) against PostgreSQL and prints both to
// the BenchmarkDotNet logger after all benchmark runs complete.
public sealed class SqlPlanDiagnoser : IDiagnoser
{
    public IEnumerable<string> Ids        => new[] { nameof(SqlPlanDiagnoser) };
    public string ShortName               => "sql+plan";
    public IEnumerable<IExporter> Exporters => Array.Empty<IExporter>();
    public IEnumerable<IAnalyser> Analysers => Array.Empty<IAnalyser>();

    public RunMode GetRunMode(BenchmarkCase _) => RunMode.NoOverhead;

    public IEnumerable<ValidationError> Validate(ValidationParameters _) =>
        Array.Empty<ValidationError>();

    public void Handle(HostSignal signal, DiagnoserActionParameters parameters)
    {
        if (signal == HostSignal.BeforeAnythingElse)
            BenchmarkInspector.For(parameters.BenchmarkCase);
    }

    public IEnumerable<Metric> ProcessResults(DiagnoserResults _) =>
        Array.Empty<Metric>();

    public void DisplayResults(ILogger logger)
    {
        logger.WriteLine();
        logger.WriteLineHeader("// *** SQL & Query Plan ***");
        foreach (var info in BenchmarkInspector.All
                     .OrderBy(i => i.Method).ThenBy(i => i.Query))
        {
            logger.WriteLine();
            logger.WriteLineHeader($"// === {info.Method}  /  Query = \"{info.Query}\"  /  Rows = {info.RowCount:N0} ===");
            logger.WriteLineInfo("SQL:");
            logger.WriteLine(info.Sql);
            logger.WriteLine();
            logger.WriteLineInfo("PLAN (EXPLAIN ANALYZE, BUFFERS, VERBOSE):");
            logger.WriteLine(info.Plan);
        }
    }

    public IColumnProvider GetColumnProvider() => EmptyColumnProvider.Instance;

    private sealed class EmptyColumnProvider : IColumnProvider
    {
        public static readonly EmptyColumnProvider Instance = new();
        public IEnumerable<IColumn> GetColumns(Summary summary) => Array.Empty<IColumn>();
    }
}
