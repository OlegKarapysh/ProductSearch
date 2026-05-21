using BenchmarkDotNet.Attributes;
using FullTextSearchPostgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullTextSearchPostgres.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class ProductSearchBenchmark
{
    private AppDbContext _db = null!;
    
    // "laptop"          — single common word, many matches expected
    // "wireless mouse"  — two-word phrase, narrower result set
    [Params("laptop", "wireless mouse")]
    public string Query { get; set; } = null!;
    
    [GlobalSetup]
    public void Setup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost; Port=5432; Database=TestDB; Username=postgres; Password=admin")
            .UseLoggerFactory(Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { }))
            .Options;

        _db = new AppDbContext(options);

        // Open a dedicated connection upfront so that connection acquisition time is not counted inside each benchmark iteration.
        // Both benchmarks share this same connection, so neither has an unfair advantage from connection pool differences.
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
    
    [Benchmark]
    public Task<List<Product>> SmartContains()
    {
        // Tokenize the query on whitespace, then require every token to appear
        // somewhere in name or description. All tokens must match (AND across
        // tokens), but they can occur in any order and any position.
        //
        // Example: query = "wireless mouse" generates SQL like:
        //   WHERE (name ILIKE '%wireless%' OR description ILIKE '%wireless%')
        //     AND (name ILIKE '%mouse%'    OR description ILIKE '%mouse%')
        //
        // This matches "wireless mouse", "wireless hp mouse",
        // "my wireless hp lp mouse free", etc.
        //
        // Semantically this is now equivalent to plainto_tsquery (implicit AND
        // between words), making the FTS comparison apples-to-apples.
        //
        // Each foreach iteration creates a new local 'pattern' variable, so each
        // Where call captures its own value — EF Core parameterizes them separately.
        var words = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        IQueryable<Product> query = _db.Products;
        foreach (var word in words)
        {
            var pattern = $"%{word}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.Name, pattern) ||
                EF.Functions.ILike(p.Description, pattern));
        }

        return query.Take(50).ToListAsync();
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
