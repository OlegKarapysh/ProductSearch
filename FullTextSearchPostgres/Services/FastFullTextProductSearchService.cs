using FullTextSearchPostgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullTextSearchPostgres.Services;

// Strategy 4: PostgreSQL full-text search WITHOUT ts_rank ordering.
//
// Same filter as FullTextProductSearchService, but drops the
// ORDER BY ts_rank(...). With no ranking, Postgres can stream rows
// straight from the GIN-driven bitmap heap scan and stop as soon as
// the LIMIT is satisfied — it never has to fetch and score every match.
//
// Trade-off: results are not ordered by relevance. Use this when "any 50
// products that match" is acceptable (autocomplete, faceted filtering,
// existence checks). For ranked results, see FullTextProductSearchService.
public class FastFullTextProductSearchService(AppDbContext db) : IProductSearchService
{
    public Task<List<Product>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        return db.Products
            .Where(p => p.SearchVector.Matches(EF.Functions.PlainToTsQuery("english", query)))
            .Take(50)
            .ToListAsync(cancellationToken);
    }
}
