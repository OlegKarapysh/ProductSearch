using FullTextSearchPostgres.Entities;

namespace FullTextSearchPostgres.Services;

public interface IProductSearchService
{
    Task<List<Product>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
