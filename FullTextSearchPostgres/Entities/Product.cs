using NpgsqlTypes;

namespace FullTextSearchPostgres.Entities;

public class Product
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string Brand { get; set; } = null!;
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }

    // NpgsqlTsVector is the C# type that maps to PostgreSQL's tsvector.
    //
    // A tsvector is NOT the original text — it is a pre-processed, indexed
    // representation of a text document. PostgreSQL converts raw text into
    // a sorted list of "lexemes": normalized, stemmed word roots, each annotated
    // with its position(s) in the document and an optional weight (A/B/C/D).
    //
    // Example:
    //   to_tsvector('english', 'The quick brown foxes are jumping')
    //   → 'brown':3 'fox':4 'jump':6 'quick':2
    //
    //   - "The" and "are" are stop words → removed entirely
    //   - "foxes"  → stemmed to "fox"
    //   - "jumping" → stemmed to "jump"
    //   - Numbers indicate word positions (used for phrase queries)
    //
    // Because this is a GENERATED ALWAYS AS ... STORED column in Postgres,
    // we never write to it — Postgres recomputes it automatically whenever
    // Name or Description changes. EF Core marks it as read-only via ValueGeneratedOnAddOrUpdate.
    public NpgsqlTsVector SearchVector { get; set; } = null!;
}
