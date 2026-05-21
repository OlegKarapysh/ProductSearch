using BenchmarkDotNet.Attributes;
using FullTextSearchPostgres.Entities;
using FullTextSearchPostgres.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FullTextSearchPostgres.Benchmarks.Benchmarks;

// MemoryDiagnoser adds Allocated/Gen0/Gen1 columns so we can compare
// not just speed but also managed-heap pressure across the three strategies.
[MemoryDiagnoser]
public class ProductSearchBenchmark
{
    private AppDbContext _db = null!;
    
    private IProductSearchService _naive = null!;
    private IProductSearchService _smart = null!;
    private IProductSearchService _fts   = null!;

    // [Params] runs the entire benchmark suite once per value.
    //   "laptop"          — single common word
    //   "wireless mouse"  — two-word phrase, exposes the ORDER BY ts_rank cost
    [Params("laptop", "wireless mouse")]
    public string Query { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost; Port=5432; Database=TestDB; Username=postgres; Password=admin")
            // Silence EF logging — console writes would add noise to the measurements.
            .UseLoggerFactory(LoggerFactory.Create(_ => { }))
            .Options;

        _db = new AppDbContext(options);

        // Open the connection upfront so connection-acquisition time
        // is not counted inside the measured iterations.
        _db.Database.OpenConnection();
        _naive = new NaiveProductSearchService(_db);
        _smart = new SmartProductSearchService(_db);
        _fts   = new FullTextProductSearchService(_db);
    }

    [GlobalCleanup]
    public void Cleanup() => _db.Dispose();
    
    [Benchmark(Baseline = true)]
    public Task<List<Product>> Naive() => _naive.SearchAsync(Query);

    [Benchmark]
    public Task<List<Product>> Smart() => _smart.SearchAsync(Query);

    [Benchmark]
    public Task<List<Product>> Fts() => _fts.SearchAsync(Query);
}
