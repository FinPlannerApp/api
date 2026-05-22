using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentVotesAndReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Value",
                schema: "issue",
                table: "IssueVotes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ParentCommentId",
                schema: "issue",
                table: "IssueComments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Score",
                schema: "issue",
                table: "IssueComments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CommentVotes",
                schema: "issue",
                columns: table => new
                {
                    CommentId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentVotes", x => new { x.CommentId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CommentVotes_IssueComments_CommentId",
                        column: x => x.CommentId,
                        principalSchema: "issue",
                        principalTable: "IssueComments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssueComments_ParentCommentId",
                schema: "issue",
                table: "IssueComments",
                column: "ParentCommentId");

            migrationBuilder.AddForeignKey(
                name: "FK_IssueComments_IssueComments_ParentCommentId",
                schema: "issue",
                table: "IssueComments",
                column: "ParentCommentId",
                principalSchema: "issue",
                principalTable: "IssueComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IssueComments_IssueComments_ParentCommentId",
                schema: "issue",
                table: "IssueComments");

            migrationBuilder.DropTable(
                name: "CommentVotes",
                schema: "issue");

            migrationBuilder.DropIndex(
                name: "IX_IssueComments_ParentCommentId",
                schema: "issue",
                table: "IssueComments");

            migrationBuilder.DropColumn(
                name: "Value",
                schema: "issue",
                table: "IssueVotes");

            migrationBuilder.DropColumn(
                name: "ParentCommentId",
                schema: "issue",
                table: "IssueComments");

            migrationBuilder.DropColumn(
                name: "Score",
                schema: "issue",
                table: "IssueComments");
        }
    }
}
