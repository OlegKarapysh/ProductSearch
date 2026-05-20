using Microsoft.EntityFrameworkCore;
using FullTextSearchPostgres;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DataSeeder");
    await db.Database.MigrateAsync();
    await DataSeeder.SeedAsync(db, logger);
}

app.UseHttpsRedirection();

app.MapGet("/products", async (string query, AppDbContext db) =>
{
    // Convert the user's search string into a tsquery.
    //
    // A tsquery is the search-term counterpart to tsvector.
    // It holds normalised lexemes connected by boolean operators,
    // and is what PostgreSQL uses to probe the GIN index.
    //
    // There are three conversion functions with different behaviors:
    //
    //   plainto_tsquery('english', 'quick brown fox')
    //     → 'quick' & 'brown' & 'fox'       (all words must appear — implicit AND)
    //     Safe for raw user input; never throws on unusual characters.
    //
    //   websearch_to_tsquery('english', 'quick OR fox -jump')
    //     Understands web-style syntax: OR, -, "phrase".
    //     Good for a power-user search box.
    //
    //   to_tsquery('english', 'quick & (brown | red)')
    //     Full tsquery syntax — AND (&), OR (|), NOT (!), prefix (:*).
    //     Requires sanitized input; throws on bad syntax.
    //
    // We use PlainToTsQuery because it accepts arbitrary user input without risk.
    // The 'english' config must match the one used when building the tsvector,
    // so stemming is applied the same way in both directions:
    //   user types "running" → tsquery lexeme "run"
    //   tsvector also stored "running" as "run" → they match.
    //
    // Note: EF.Functions methods must be inlined inside the expression tree —
    // they cannot be captured in a local variable because they are only meaningful
    // as SQL translations and throw if called directly in C#.
    var products = await db.Products
        // Filter with the @@ operator.
        //
        // .Matches(...) translates to:   search_vector @@ plainto_tsquery('english', ...)
        //
        // The @@ operator asks: "does this tsvector satisfy this tsquery?"
        // PostgreSQL evaluates it using the GIN index — it looks up each
        // lexeme in the index and intersects the result sets (AND logic for
        // plainto_tsquery). This is an index seek, not a table scan.
        // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
        .Where(p => p.SearchVector.Matches(EF.Functions.PlainToTsQuery("english", query)))
        // Rank results by relevance using ts_rank.
        //
        // .Rank(tsquery) is an extension on NpgsqlTsVector and translates to
        // ts_rank(search_vector, plainto_tsquery('english', ...)).
        //
        // ts_rank returns a float score representing how well the document matches:
        //   - How many query lexemes appear in the document
        //   - Their weight labels (A/B/C/D set during indexing):
        //     a hit in Name (weight A) scores higher than one in Description (weight B)
        //
        // Without OrderBy, Take(50) returns an arbitrary 50 rows.
        // With it, the 50 most relevant results float to the top.
        // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
        .OrderByDescending(p => p.SearchVector.Rank(EF.Functions.PlainToTsQuery("english", query)))
        .Take(50)
        .ToListAsync();

    return Results.Ok(products);
});

app.Run();
