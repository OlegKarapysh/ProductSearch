using FullTextSearchPostgres.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FullTextSearchPostgres.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(p => p.Description)
            .HasColumnName("description")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(p => p.Category)
            .HasColumnName("category")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(p => p.Brand)
            .HasColumnName("brand")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(p => p.Price)
            .HasColumnName("price")
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp without time zone")
            .IsRequired();

        builder.HasIndex(p => p.Category).HasDatabaseName("ix_products_category");
        builder.HasIndex(p => p.Brand).HasDatabaseName("ix_products_brand");
        builder.HasIndex(p => p.CreatedAt).HasDatabaseName("ix_products_created_at");
    }
}
