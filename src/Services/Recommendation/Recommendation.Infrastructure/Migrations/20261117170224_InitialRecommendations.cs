using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recommendation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecommendationDailyUsage",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationDailyUsage", x => new { x.UserId, x.Day });
                });

            migrationBuilder.CreateTable(
                name: "RecommendationSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Constraints = table.Column<string[]>(type: "text[]", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModelUsed = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationSessions", x => x.Id);
                    table.CheckConstraint("CK_Session_Purpose", "length(\"Purpose\") BETWEEN 10 AND 2000");
                });

            migrationBuilder.CreateTable(
                name: "RecommendationItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    UsageNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationItems", x => x.Id);
                    table.CheckConstraint("CK_Item_Rank", "\"Rank\" BETWEEN 1 AND 6");
                    table.CheckConstraint("CK_Item_Text", "length(btrim(\"Reason\")) > 0 AND length(btrim(\"UsageNotes\")) > 0");
                    table.ForeignKey(
                        name: "FK_RecommendationItems_RecommendationSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "RecommendationSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationItems_SessionId",
                table: "RecommendationItems",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationItems_SessionId_EquipmentTypeId",
                table: "RecommendationItems",
                columns: new[] { "SessionId", "EquipmentTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationItems_SessionId_Rank",
                table: "RecommendationItems",
                columns: new[] { "SessionId", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationSessions_UserId_CreatedAt",
                table: "RecommendationSessions",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationDailyUsage");

            migrationBuilder.DropTable(
                name: "RecommendationItems");

            migrationBuilder.DropTable(
                name: "RecommendationSessions");
        }
    }
}
