using Microsoft.EntityFrameworkCore;
using FullTextSearchPostgres;
using FullTextSearchPostgres.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddKeyedScoped<IProductSearchService, NaiveProductSearchService>("naive");
builder.Services.AddKeyedScoped<IProductSearchService, SmartProductSearchService>("smart");
builder.Services.AddKeyedScoped<IProductSearchService, FullTextProductSearchService>("fts");
builder.Services.AddKeyedScoped<IProductSearchService, FastFullTextProductSearchService>("fts-fast");
builder.Services.AddKeyedScoped<IProductSearchService, RankedFastFullTextProductSearchService>("fts-ranked");

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

app.MapGet("/products/naive",
    async ([FromKeyedServices("naive")] IProductSearchService search, string query, CancellationToken ct) =>
        Results.Ok(await search.SearchAsync(query, ct)));

app.MapGet("/products/smart",
    async ([FromKeyedServices("smart")] IProductSearchService search, string query, CancellationToken ct) =>
        Results.Ok(await search.SearchAsync(query, ct)));

app.MapGet("/products/fts",
    async ([FromKeyedServices("fts")] IProductSearchService search, string query, CancellationToken ct) =>
        Results.Ok(await search.SearchAsync(query, ct)));

app.MapGet("/products/fts-fast",
    async ([FromKeyedServices("fts-fast")] IProductSearchService search, string query, CancellationToken ct) =>
        Results.Ok(await search.SearchAsync(query, ct)));

app.MapGet("/products/fts-ranked",
    async ([FromKeyedServices("fts-ranked")] IProductSearchService search, string query, CancellationToken ct) =>
        Results.Ok(await search.SearchAsync(query, ct)));

app.Run();
