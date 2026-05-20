using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace FullTextSearchPostgres.Migrations
{
    /// <inheritdoc />
    public partial class AddFtsSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "products",
                type: "tsvector",
                nullable: false,
                computedColumnSql: "setweight(to_tsvector('english', name), 'A') || setweight(to_tsvector('english', description), 'B')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_search_vector",
                table: "products",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_search_vector",
                table: "products");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "products");
        }
    }
}
