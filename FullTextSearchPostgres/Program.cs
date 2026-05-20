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

app.UseHttpsRedirection();

app.MapGet("/products", async (string query, AppDbContext db) =>
{
    var products = await db.Products
        .Where(x => x.Name.Contains(query) || x.Description.Contains(query))
        .Take(50)
        .ToListAsync();

    return Results.Ok(products);
});

app.Run();
