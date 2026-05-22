using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase2Features : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                schema: "issue",
                table: "Issues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedByUserId",
                schema: "issue",
                table: "Issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GitHubIssueUrl",
                schema: "issue",
                table: "Issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsClosed",
                schema: "issue",
                table: "Issues",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MilestoneId",
                schema: "issue",
                table: "Issues",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommentReactions",
                schema: "issue",
                columns: table => new
                {
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Emoji = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentReactions", x => new { x.CommentId, x.UserId, x.Emoji });
                    table.ForeignKey(
                        name: "FK_CommentReactions_IssueComments_CommentId",
                        column: x => x.CommentId,
                        principalSchema: "issue",
                        principalTable: "IssueComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueAssignees",
                schema: "issue",
                columns: table => new
                {
                    IssueId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueAssignees", x => new { x.IssueId, x.UserId });
                    table.ForeignKey(
                        name: "FK_IssueAssignees_Issues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: "issue",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IssueLabels",
                schema: "issue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Color = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueLabels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IssueMilestones",
                schema: "issue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueMilestones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IssueLabelAssignments",
                schema: "issue",
                columns: table => new
                {
                    IssueId = table.Column<int>(type: "integer", nullable: false),
                    LabelId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueLabelAssignments", x => new { x.IssueId, x.LabelId });
                    table.ForeignKey(
                        name: "FK_IssueLabelAssignments_IssueLabels_LabelId",
                        column: x => x.LabelId,
                        principalSchema: "issue",
                        principalTable: "IssueLabels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IssueLabelAssignments_Issues_IssueId",
                        column: x => x.IssueId,
                        principalSchema: "issue",
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_MilestoneId",
                schema: "issue",
                table: "Issues",
                column: "MilestoneId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueLabelAssignments_LabelId",
                schema: "issue",
                table: "IssueLabelAssignments",
                column: "LabelId");

            migrationBuilder.CreateIndex(
                name: "IX_IssueLabels_Name",
                schema: "issue",
                table: "IssueLabels",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Issues_IssueMilestones_MilestoneId",
                schema: "issue",
                table: "Issues",
                column: "MilestoneId",
                principalSchema: "issue",
                principalTable: "IssueMilestones",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Issues_IssueMilestones_MilestoneId",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropTable(
                name: "CommentReactions",
                schema: "issue");

            migrationBuilder.DropTable(
                name: "IssueAssignees",
                schema: "issue");

            migrationBuilder.DropTable(
                name: "IssueLabelAssignments",
                schema: "issue");

            migrationBuilder.DropTable(
                name: "IssueMilestones",
                schema: "issue");

            migrationBuilder.DropTable(
                name: "IssueLabels",
                schema: "issue");

            migrationBuilder.DropIndex(
                name: "IX_Issues_MilestoneId",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "GitHubIssueUrl",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "IsClosed",
                schema: "issue",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "MilestoneId",
                schema: "issue",
                table: "Issues");
        }
    }
}
