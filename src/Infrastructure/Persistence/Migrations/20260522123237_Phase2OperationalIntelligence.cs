using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2OperationalIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "OldStatus",
                schema: "issue",
                table: "IssueStatusHistories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "NewStatus",
                schema: "issue",
                table: "IssueStatusHistories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                schema: "issue",
                table: "Issues",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "issue",
                table: "Issues",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Severity",
                schema: "issue",
                table: "Issues",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Priority",
                schema: "issue",
                table: "Issues",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Frequency",
                schema: "issue",
                table: "Issues",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<double>(
                name: "PainVelocity",
                schema: "issue",
                table: "Issues",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                schema: "issue",
                table: "IssueComments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "IssueActivities",
                schema: "issue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IssueId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ActivityType = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueActivities_Issues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: "issue",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueRelations",
                schema: "issue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IssueId = table.Column<int>(type: "integer", nullable: false),
                    TargetIssueId = table.Column<int>(type: "integer", nullable: false),
                    RelationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssueRelations_Issues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: "issue",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IssueRelations_Issues_TargetIssueId",
                        column: x => x.TargetIssueId,
                        principalSchema: "issue",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_CreatedAt",
                schema: "issue",
                table: "Issues",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_CreatorUserId",
                schema: "issue",
                table: "Issues",
                column: "CreatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_PainScore",
                schema: "issue",
                table: "Issues",
                column: "PainScore");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_PainVelocity",
                schema: "issue",
                table: "Issues",
                column: "PainVelocity");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Status",
                schema: "issue",
                table: "Issues",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Status_PainScore",
                schema: "issue",
                table: "Issues",
                columns: new[] { "Status", "PainScore" });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Status_PainVelocity",
                schema: "issue",
                table: "Issues",
                columns: new[] { "Status", "PainVelocity" });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_Type",
                schema: "issue",
                table: "Issues",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_IssueActivities_CreatedAt",
                schema: "issue",
                table: "IssueActivities",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IssueActivities_IssueId",
                schema: "issue",
                table: "IssueActivities",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueRelations_IssueId_TargetIssueId_RelationType",
                schema: "issue",
                table: "IssueRelations",
                columns: new[] { "IssueId", "TargetIssueId", "RelationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IssueRelations_TargetIssueId",
                schema: "issue",
                table: "IssueRelations",
                column: "TargetIssueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueActivities",
                schema: "issue");

            migrationBuilder.DropTable(
                name: "IssueRelations",
                schema: "issue");

            migrationBuilder.DropIndex(
                name: "IX_Issues_CreatedAt",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Issues_CreatorUserId",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Issues_PainScore",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Issues_PainVelocity",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Issues_Status",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Issues_Status_PainScore",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Issues_Status_PainVelocity",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Issues_Type",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "PainVelocity",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "Type",
                schema: "issue",
                table: "IssueComments");

            migrationBuilder.AlterColumn<string>(
                name: "OldStatus",
                schema: "issue",
                table: "IssueStatusHistories",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "NewStatus",
                schema: "issue",
                table: "IssueStatusHistories",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<int>(
                name: "Type",
                schema: "issue",
                table: "Issues",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "issue",
                table: "Issues",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "Severity",
                schema: "issue",
                table: "Issues",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "Priority",
                schema: "issue",
                table: "Issues",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "Frequency",
                schema: "issue",
                table: "Issues",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);
        }
    }
}
