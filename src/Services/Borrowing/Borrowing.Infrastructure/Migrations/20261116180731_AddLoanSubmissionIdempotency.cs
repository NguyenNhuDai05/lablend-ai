using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Borrowing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanSubmissionIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoanSubmissions",
                columns: table => new
                {
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LoanId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanSubmissions", x => new { x.StudentId, x.Key });
                    table.ForeignKey(
                        name: "FK_LoanSubmissions_LoanRequests_LoanId",
                        column: x => x.LoanId,
                        principalTable: "LoanRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoanSubmissions_LoanId",
                table: "LoanSubmissions",
                column: "LoanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoanSubmissions");
        }
    }
}
