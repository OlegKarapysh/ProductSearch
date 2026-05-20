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

        // --- FULL-TEXT SEARCH SETUP ---
        //
        // Step 1: define the generated tsvector column.
        //
        // HasComputedColumnSql tells EF Core to emit a GENERATED ALWAYS AS column in SQL.
        // "stored: true" is required — PostgreSQL only supports STORED generated columns
        // (it computes and saves the value on every INSERT/UPDATE). Virtual generated
        // columns do not exist in PostgreSQL.
        //
        // The SQL expression combines two calls:
        //
        //   to_tsvector('english', name)
        //     Converts the name text to a tsvector using the 'english' text-search
        //     configuration. The configuration controls:
        //       - which stop words to discard ("the", "a", "is", ...)
        //       - which stemming algorithm to apply ("running" → "run")
        //     Other built-in configs: 'simple' (no stemming), 'french', 'german', etc.
        //
        //   setweight(..., 'A') / setweight(..., 'B')
        //     Assigns a weight label to every lexeme in the vector.
        //     There are four labels: A (highest) > B > C > D (lowest).
        //     Here we say: a word found in Name (weight A) is more important
        //     than the same word found in Description (weight B).
        //     ts_rank uses these weights when calculating the relevance score,
        //     so a query hit in the product name ranks higher than one buried in
        //     the description.
        //
        //   ... || ...
        //     The || operator concatenates two tsvectors into one.
        //     The result is a single tsvector covering both columns,
        //     with their positions and weights preserved.
        //
        // Full generated column SQL:
        //   search_vector tsvector GENERATED ALWAYS AS (
        //       setweight(to_tsvector('english', name), 'A') ||
        //       setweight(to_tsvector('english', description), 'B')
        //   ) STORED
        builder.Property(p => p.SearchVector)
            .HasColumnName("search_vector")
            .HasComputedColumnSql(
                "setweight(to_tsvector('english', name), 'A') || " +
                "setweight(to_tsvector('english', description), 'B')",
                stored: true);

        // Step 2: create a GIN index on the tsvector column.
        //
        // GIN = Generalized Inverted Index.
        // It works like a book's back-index: for every unique lexeme across all rows
        // it stores a list of row IDs (and positions) that contain that lexeme.
        //
        //   Query: "laptop"
        //   GIN lookup: "laptop" → [row 4, row 91, row 450, ...]
        //   PostgreSQL fetches those rows directly — no sequential scan needed.
        //
        // Why GIN and not B-tree?
        //   B-tree indexes work on scalar comparisons (<, =, >).
        //   GIN indexes work on containment: "does this document contain this token?"
        //   That's exactly what the @@ operator (full-text match) asks.
        //
        // Why not GIST?
        //   GIST is another index type that supports tsvector. It is smaller and
        //   faster to update, but slower to query than GIN. GIN is the right choice
        //   when reads dominate, which is true for a search endpoint.
        builder.HasIndex(p => p.SearchVector)
            .HasDatabaseName("ix_products_search_vector")
            .HasMethod("GIN");
    }
}
