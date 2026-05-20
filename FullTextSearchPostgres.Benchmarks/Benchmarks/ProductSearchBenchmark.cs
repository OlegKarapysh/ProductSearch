using BenchmarkDotNet.Attributes;
using FullTextSearchPostgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullTextSearchPostgres.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class ProductSearchBenchmark
{
    private AppDbContext _db = null!;

    // [Params] runs the entire benchmark once per value so we can see
    // whether performance differs between short and multi-word queries.
    // "laptop"          — single common word, many matches expected
    // "wireless mouse"  — two-word phrase, narrower result set
    [Params("laptop", "wireless mouse")]
    public string Query { get; set; } = null!;

    // [GlobalSetup] runs once before all iterations of a benchmark method.
    // We create the DbContext here so its construction cost is not included
    // in the measured timings.
    [GlobalSetup]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost; Port=5432; Database=TestDB; Username=postgres; Password=admin")
            // Disable EF Core's logging during benchmarks — console writes
            // would add noise to the timing and allocation numbers.
            .UseLoggerFactory(Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { }))
            .Options;

        _db = new AppDbContext(options);

        // Open a dedicated connection upfront so that connection acquisition
        // time is not counted inside each benchmark iteration.
        // Both benchmarks share this same connection, so neither has an
        // unfair advantage from connection pool differences.
        _db.Database.OpenConnection();
    }

    [GlobalCleanup]
    public void Cleanup() => _db.Dispose();

    // --- BASELINE: naive LIKE search ---
    //
    // Contains() translates to:
    //   WHERE name LIKE '%query%' OR description LIKE '%query%'
    //
    // The leading % wildcard means PostgreSQL cannot use a B-tree index.
    // It performs a sequential scan across all 1 million rows,
    // evaluating the LIKE pattern on every name and description column.
    // Cost grows linearly with table size.
    //
    // Baseline = true tells BenchmarkDotNet to treat this as the reference
    // point and express all other benchmarks as a ratio to this one.
    [Benchmark(Baseline = true)]
    public Task<List<Product>> NaiveContains()
    {
        return _db.Products
            .Where(p => p.Name.Contains(Query) || p.Description.Contains(Query))
            .Take(50)
            .ToListAsync();
    }

    // --- OPTIMIZED: full-text search ---
    //
    // Translates to:
    //   WHERE  search_vector @@ plainto_tsquery('english', $1)
    //   ORDER  BY ts_rank(search_vector, plainto_tsquery('english', $1)) DESC
    //   LIMIT  50
    //
    // PostgreSQL resolves @@ via the GIN index on search_vector:
    //   1. Look up each query lexeme in the inverted index (O(log n) per lexeme).
    //   2. Intersect the matching row-ID lists.
    //   3. Fetch only those rows — no full table scan.
    //
    // ts_rank scores results by how well they match, so the top 50 are
    // the most relevant ones rather than an arbitrary 50.
    [Benchmark]
    public Task<List<Product>> FullTextSearch()
    {
        return _db.Products
            .Where(p => p.SearchVector.Matches(EF.Functions.PlainToTsQuery("english", Query)))
            .OrderByDescending(p => p.SearchVector.Rank(EF.Functions.PlainToTsQuery("english", Query)))
            .Take(50)
            .ToListAsync();
    }
}
