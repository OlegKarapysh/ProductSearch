using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;

namespace FullTextSearchPostgres.Benchmarks.Diagnosers;

internal sealed class RowCountMetricDescriptor : IMetricDescriptor
{
    public static readonly RowCountMetricDescriptor Instance = new();

    public string Id            => "RowCount";
    public string DisplayName   => "Rows";
    public string Legend        => "Number of rows returned by the search query";
    public string NumberFormat  => "N0";
    public UnitType UnitType    => UnitType.Dimensionless;
    public string Unit          => "rows";
    public bool TheGreaterTheBetter => false;
    public int PriorityInCategory   => 0;

    public bool GetIsAvailable(Metric metric) => true;
}
