using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOverspendAlertsEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OverspendAlertsEnabled",
                schema: "identity",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MerchantId",
                schema: "transactions",
                table: "Transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsObligation",
                schema: "transactions",
                table: "RecurringTransactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "AnnualFee",
                schema: "accounts",
                table: "CreditCardDetails",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InterestRate",
                schema: "accounts",
                table: "CreditCardDetails",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinimumBalance",
                schema: "accounts",
                table: "BankAccountDetails",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                schema: "accounts",
                table: "Accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                schema: "accounts",
                table: "Accounts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DecisionJournalEntries",
                schema: "transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Reasoning = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    DecisionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "text", nullable: true),
                    OutcomeRecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DecisionJournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Merchants",
                schema: "transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Merchants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SavingsBuckets",
                schema: "accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AccountId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TargetAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsBuckets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavingsBuckets_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounts",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MerchantAliases",
                schema: "transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MerchantId = table.Column<int>(type: "integer", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantAliases_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalSchema: "transactions",
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Goals",
                schema: "accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TargetAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TargetDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsAchieved = table.Column<bool>(type: "boolean", nullable: false),
                    SavingsBucketId = table.Column<int>(type: "integer", nullable: true),
                    ManualCurrentAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Goals_SavingsBuckets_SavingsBucketId",
                        column: x => x.SavingsBucketId,
                        principalSchema: "accounts",
                        principalTable: "SavingsBuckets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_MerchantId",
                schema: "transactions",
                table: "Transactions",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_DecisionJournalEntries_UserId_DecisionDate",
                schema: "transactions",
                table: "DecisionJournalEntries",
                columns: new[] { "UserId", "DecisionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Goals_SavingsBucketId",
                schema: "accounts",
                table: "Goals",
                column: "SavingsBucketId");

            migrationBuilder.CreateIndex(
                name: "IX_Goals_UserId",
                schema: "accounts",
                table: "Goals",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantAliases_MerchantId",
                schema: "transactions",
                table: "MerchantAliases",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_UserId_Name",
                schema: "transactions",
                table: "Merchants",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavingsBuckets_AccountId",
                schema: "accounts",
                table: "SavingsBuckets",
                column: "AccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Merchants_MerchantId",
                schema: "transactions",
                table: "Transactions",
                column: "MerchantId",
                principalSchema: "transactions",
                principalTable: "Merchants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Merchants_MerchantId",
                schema: "transactions",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "DecisionJournalEntries",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "Goals",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "MerchantAliases",
                schema: "transactions");

            migrationBuilder.DropTable(
                name: "SavingsBuckets",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "Merchants",
                schema: "transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_MerchantId",
                schema: "transactions",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "OverspendAlertsEnabled",
                schema: "identity",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MerchantId",
                schema: "transactions",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsObligation",
                schema: "transactions",
                table: "RecurringTransactions");

            migrationBuilder.DropColumn(
                name: "AnnualFee",
                schema: "accounts",
                table: "CreditCardDetails");

            migrationBuilder.DropColumn(
                name: "InterestRate",
                schema: "accounts",
                table: "CreditCardDetails");

            migrationBuilder.DropColumn(
                name: "MinimumBalance",
                schema: "accounts",
                table: "BankAccountDetails");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                schema: "accounts",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "Purpose",
                schema: "accounts",
                table: "Accounts");
        }
    }
}
