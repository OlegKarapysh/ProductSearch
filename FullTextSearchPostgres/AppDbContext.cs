using FullTextSearchPostgres.Configurations;
using FullTextSearchPostgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullTextSearchPostgres;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
    }
}
