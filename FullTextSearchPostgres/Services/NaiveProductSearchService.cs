using FullTextSearchPostgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullTextSearchPostgres.Services;

// Strategy 1: case-sensitive substring match.
//
// Contains() translates to:
//   WHERE name LIKE '%query%' OR description LIKE '%query%'
//
// Leading wildcard → cannot use a B-tree index → sequential scan over the whole table.
// Suitable only for small datasets or ad-hoc queries.
public class NaiveProductSearchService(AppDbContext db) : IProductSearchService
{
    public Task<List<Product>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        return db.Products
            .Where(p => p.Name.Contains(query) || p.Description.Contains(query))
            .Take(50)
            .ToListAsync(cancellationToken);
    }
}
