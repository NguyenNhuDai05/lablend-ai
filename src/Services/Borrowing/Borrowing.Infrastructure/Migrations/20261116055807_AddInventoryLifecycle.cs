using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Borrowing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApprovalOperationId",
                table: "LoanRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HandedOverAt",
                table: "LoanRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HandedOverBy",
                table: "LoanRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnCondition",
                table: "LoanItems",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnedAt",
                table: "LoanItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryOperations",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoanId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryOperations", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_InventoryOperations_LoanRequests_LoanId",
                        column: x => x.LoanId,
                        principalTable: "LoanRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOperations_LoanId",
                table: "InventoryOperations",
                column: "LoanId",
                unique: true,
                filter: "\"State\" NOT IN ('COMPLETED','FAILED')");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryOperations_State_NextRetryAt",
                table: "InventoryOperations",
                columns: new[] { "State", "NextRetryAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryOperations");

            migrationBuilder.DropColumn(
                name: "ApprovalOperationId",
                table: "LoanRequests");

            migrationBuilder.DropColumn(
                name: "HandedOverAt",
                table: "LoanRequests");

            migrationBuilder.DropColumn(
                name: "HandedOverBy",
                table: "LoanRequests");

            migrationBuilder.DropColumn(
                name: "ReturnCondition",
                table: "LoanItems");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                table: "LoanItems");
        }
    }
}
