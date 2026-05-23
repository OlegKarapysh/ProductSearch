using FullTextSearchPostgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullTextSearchPostgres.Services;

// Strategy 5: Two-stage FTS — fast filtering via GIN index, then rank only a small candidate pool.
//
// The problem with FullTextProductSearchService is that ORDER BY ts_rank(...)
// forces Postgres to compute the rank for every matching row before LIMIT can apply.
// With 100k+ matches that means a full bitmap heap fetch (~250 ms).
//
// This service uses a CTE to:
//   1. Pull a capped candidate pool (CandidatePoolSize) via the GIN index — bitmap scan
//      can stop as soon as that many rows are found.
//   2. Rank only that small subset and return the top N.
//
// Trade-off: ranking is approximate — if there are 100k matches, the "best" 50 may not
// be inside the first 500 candidates. But in practice, picking any 500 matches and
// ranking them produces results indistinguishable from a full ranking for most queries,
// at 100–300× the speed.
public class RankedFastFullTextProductSearchService(AppDbContext db) : IProductSearchService
{
    private const int CandidatePoolSize = 500;
    private const int ResultLimit = 50;

    public async Task<List<Product>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        const string sql = """
            WITH candidates AS MATERIALIZED (
                SELECT p.id, p.brand, p.category, p.created_at, p.description,
                       p.name, p.price, p.search_vector
                FROM products AS p
                WHERE p.search_vector @@ plainto_tsquery('english', {0})
                LIMIT {1}
            )
            SELECT id, brand, category, created_at, description, name, price, search_vector
            FROM candidates
            ORDER BY ts_rank(search_vector, plainto_tsquery('english', {0})) DESC
            LIMIT {2}
            """;

        return await db.Products
            .FromSqlRaw(sql, query, CandidatePoolSize, ResultLimit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
