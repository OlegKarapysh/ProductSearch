using FullTextSearchPostgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullTextSearchPostgres.Services;

// Strategy 2: tokenized case-insensitive substring match.
//
// Splits the query on whitespace and requires every token to appear somewhere
// in name or description (AND across tokens, OR within each token across columns).
// Words may appear in any order and any position; substring matches are allowed
// ("lap" matches "laptop").
//
// Example SQL for query = "wireless mouse":
//   WHERE (name ILIKE '%wireless%' OR description ILIKE '%wireless%')
//     AND (name ILIKE '%mouse%'    OR description ILIKE '%mouse%')
//
// Still a sequential scan — no index can serve a leading-wildcard ILIKE —
// but each foreach iteration creates a new local 'pattern', so EF Core
// parameterizes each Where call independently.
public class SmartProductSearchService(AppDbContext db) : IProductSearchService
{
    public Task<List<Product>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        IQueryable<Product> queryable = db.Products;
        foreach (var word in words)
        {
            var pattern = $"%{word}%";
            queryable = queryable.Where(p =>
                EF.Functions.ILike(p.Name, pattern) ||
                EF.Functions.ILike(p.Description, pattern));
        }

        return queryable.Take(50).ToListAsync(cancellationToken);
    }
}
