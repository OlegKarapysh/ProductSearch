using Bogus;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace FullTextSearchPostgres;

public static class DataSeeder
{
    private const int TargetCount = 1_000_000;

    public static async Task SeedAsync(AppDbContext db, ILogger logger)
    {
        var existing = await db.Products.CountAsync();
        if (existing >= TargetCount)
        {
            logger.LogInformation("Products table already has {Count:N0} rows, skipping seed", existing);
            return;
        }

        var toInsert = TargetCount - existing;
        logger.LogInformation("Seeding {Count:N0} products via binary COPY...", toInsert);

        var faker = new Faker();

        await db.Database.OpenConnectionAsync();
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();

            await using var importer = await connection.BeginBinaryImportAsync(
                "COPY products (name, description, category, brand, price, created_at) FROM STDIN (FORMAT BINARY)");

            for (var i = 0; i < toInsert; i++)
            {
                await importer.StartRowAsync();
                await importer.WriteAsync(faker.Commerce.ProductName(), NpgsqlDbType.Text);
                await importer.WriteAsync(faker.Commerce.ProductDescription(), NpgsqlDbType.Text);
                await importer.WriteAsync(faker.Commerce.Department(), NpgsqlDbType.Text);
                await importer.WriteAsync(faker.Company.CompanyName(), NpgsqlDbType.Text);
                await importer.WriteAsync(faker.Finance.Amount(1, 10_000), NpgsqlDbType.Numeric);
                await importer.WriteAsync(
                    DateTime.SpecifyKind(faker.Date.Past(3), DateTimeKind.Unspecified),
                    NpgsqlDbType.Timestamp);

                if ((i + 1) % 100_000 == 0)
                    logger.LogInformation("  {Done:N0} / {Total:N0} rows written...", i + 1, toInsert);
            }

            await importer.CompleteAsync();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        logger.LogInformation("Seeding complete — {Count:N0} products inserted", toInsert);
    }
}
