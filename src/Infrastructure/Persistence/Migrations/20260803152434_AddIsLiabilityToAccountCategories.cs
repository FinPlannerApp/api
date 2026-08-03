using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIsLiabilityToAccountCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLiability",
                schema: "accounts",
                table: "AccountCategories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // One-time backfill: flag existing categories that are almost certainly
            // liabilities based on common naming patterns, so existing users' net worth
            // doesn't silently change the moment this migration runs.
            // Users can still correct any mis-tagged category afterward via the UI.
            migrationBuilder.Sql(@"
                UPDATE ""accounts"".""AccountCategories""
                SET ""IsLiability"" = TRUE
                WHERE ""Name"" ILIKE '%credit%'
                   OR ""Name"" ILIKE '%loan%'
                   OR ""Name"" ILIKE '%emi%'
                   OR ""Name"" ILIKE '%debt%'
                   OR ""Name"" ILIKE '%card%';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsLiability",
                schema: "accounts",
                table: "AccountCategories");
        }
    }
}
