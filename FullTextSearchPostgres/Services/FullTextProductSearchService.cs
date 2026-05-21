using FullTextSearchPostgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullTextSearchPostgres.Services;

// Strategy 3: PostgreSQL full-text search via the precomputed tsvector column.
//
// SQL produced:
//   WHERE search_vector @@ plainto_tsquery('english', $1)
//   ORDER BY ts_rank(search_vector, plainto_tsquery('english', $1)) DESC
//   LIMIT 50
//
// - The @@ match is satisfied by the GIN index on search_vector (index seek).
// - plainto_tsquery normalises the user input the same way to_tsvector did
//   during indexing (lowercasing, stemming, stop-word removal), so they align.
// - ts_rank scores matches by weight (Name=A, Description=B) and frequency,
//   surfacing the most relevant 50 rows.
public class FullTextProductSearchService(AppDbContext db) : IProductSearchService
{
    public Task<List<Product>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        return db.Products
            .Where(p => p.SearchVector.Matches(EF.Functions.PlainToTsQuery("english", query)))
            .OrderByDescending(p => p.SearchVector.Rank(EF.Functions.PlainToTsQuery("english", query)))
            .Take(50)
            .ToListAsync(cancellationToken);
    }
}
