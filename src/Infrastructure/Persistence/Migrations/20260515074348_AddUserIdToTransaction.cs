using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdToTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='transactions' AND table_name='Transactions' AND column_name='UserId') THEN
                        ALTER TABLE transactions.""Transactions"" ADD COLUMN ""UserId"" text;
                    END IF;
                END $$;");

            // Production Fix: Assign existing transactions to a default 'system' user.
            // This prevents null column violations when applying the NOT NULL constraint to existing data.
            migrationBuilder.Sql("UPDATE transactions.\"Transactions\" SET \"UserId\" = 'system' WHERE \"UserId\" IS NULL;");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    ALTER TABLE transactions.""Transactions"" ALTER COLUMN ""UserId"" SET NOT NULL;
                    ALTER TABLE transactions.""Transactions"" ALTER COLUMN ""UserId"" SET DEFAULT '';
                EXCEPTION WHEN OTHERS THEN
                    -- Ignore if already configured
                END $$;");

            migrationBuilder.CreateTable(
                name: "IssueTaxonomies",
                schema: null,
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueTaxonomies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueTaxonomies_IssueTaxonomies_ParentId",
                        column: x => x.ParentId,
                        principalSchema: null,
                        principalTable: "IssueTaxonomies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Subscriptions",
                schema: "transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Tag = table.Column<string>(type: "text", nullable: true),
                    CancellationUrl = table.Column<string>(type: "text", nullable: true),
                    RecurringTransactionId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subscriptions_RecurringTransactions_RecurringTransactionId",
                        column: x => x.RecurringTransactionId,
                        principalSchema: "transactions",
                        principalTable: "RecurringTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Issues",
                schema: null,
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Priority = table.Column<string>(type: "text", nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    SubcategoryId = table.Column<int>(type: "integer", nullable: true),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    ImpactsMoney = table.Column<bool>(type: "boolean", nullable: false),
                    FinancialImpactAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    Frequency = table.Column<string>(type: "text", nullable: false),
                    TrustPenalty = table.Column<int>(type: "integer", nullable: false),
                    Votes = table.Column<int>(type: "integer", nullable: false),
                    PainScore = table.Column<double>(type: "double precision", nullable: false),
                    CreatorUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Issues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Issues_IssueTaxonomies_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: null,
                        principalTable: "IssueTaxonomies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Issues_IssueTaxonomies_SubcategoryId",
                        column: x => x.SubcategoryId,
                        principalSchema: null,
                        principalTable: "IssueTaxonomies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IssueComments",
                schema: null,
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IssueId = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatorUserId = table.Column<string>(type: "text", nullable: true),
                    ExpectedBehavior = table.Column<string>(type: "text", nullable: true),
                    ActualBehavior = table.Column<string>(type: "text", nullable: true),
                    HasWorkaround = table.Column<bool>(type: "boolean", nullable: false),
                    StructuredMetadata = table.Column<string>(type: "text", nullable: true),
                    IsHelpful = table.Column<bool>(type: "boolean", nullable: false),
                    IsRootCause = table.Column<bool>(type: "boolean", nullable: false),
                    IsReproConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueComments_Issues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: null,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ""IX_Transactions_UserId"" ON transactions.""Transactions"" (""UserId"");");

            migrationBuilder.CreateIndex(
                name: "IX_IssueComments_IssueId",
                schema: null,
                table: "IssueComments",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_CategoryId",
                schema: null,
                table: "Issues",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_SubcategoryId",
                schema: null,
                table: "Issues",
                column: "SubcategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueTaxonomies_ParentId",
                schema: null,
                table: "IssueTaxonomies",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_RecurringTransactionId",
                schema: "transactions",
                table: "Subscriptions",
                column: "RecurringTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_UserId_Name",
                schema: "transactions",
                table: "Subscriptions",
                columns: new[] { "UserId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueComments",
                schema: null);

            migrationBuilder.DropTable(
                name: "Subscriptions",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "Issues",
                schema: null);

            migrationBuilder.DropTable(
                name: "IssueTaxonomies",
                schema: null);

            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId",
                schema: "transactions",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "UserId",
                schema: "transactions",
                table: "Transactions");
        }
    }
}
