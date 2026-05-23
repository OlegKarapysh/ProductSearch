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

// Exposes the number of rows each benchmark case returned as a metric column.
// The actual row-count is captured by BenchmarkInspector (which executes the
// query once in the host process before BDN starts the measurement runs).
public sealed class RowCountDiagnoser : IDiagnoser
{
    public IEnumerable<string> Ids        => new[] { nameof(RowCountDiagnoser) };
    public string ShortName               => "rows";
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

    public IEnumerable<Metric> ProcessResults(DiagnoserResults results)
    {
        var info = BenchmarkInspector.For(results.BenchmarkCase);
        yield return new Metric(RowCountMetricDescriptor.Instance, info.RowCount);
    }

    public void DisplayResults(ILogger logger) { }

    public IColumnProvider GetColumnProvider() => EmptyColumnProvider.Instance;

    private sealed class EmptyColumnProvider : IColumnProvider
    {
        public static readonly EmptyColumnProvider Instance = new();
        public IEnumerable<IColumn> GetColumns(Summary summary) => Array.Empty<IColumn>();
    }
}
