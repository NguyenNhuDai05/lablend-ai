using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Location = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetUnits", x => x.Id);
                    table.CheckConstraint("CK_AssetUnits_Status", "\"Status\" IN ('AVAILABLE', 'RESERVED', 'ON_LOAN', 'MAINTENANCE', 'LOST', 'RETIRED')");
                });

            migrationBuilder.CreateTable(
                name: "ReserveOperations",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoanId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReserveOperations", x => x.OperationId);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    OpenedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletionNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceRecords_AssetUnits_AssetUnitId",
                        column: x => x.AssetUnitId,
                        principalTable: "AssetUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoanId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoanItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReturnCondition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                    table.CheckConstraint("CK_Reservations_Status", "\"Status\" IN ('HELD', 'HANDED_OVER', 'RETURNED', 'RELEASED')");
                    table.ForeignKey(
                        name: "FK_Reservations_AssetUnits_AssetUnitId",
                        column: x => x.AssetUnitId,
                        principalTable: "AssetUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AssetUnits",
                columns: new[] { "Id", "CreatedAt", "EquipmentTypeId", "Location", "Notes", "SerialNumber", "Status", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("a0000000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000001"), "Phòng E101 - Tủ A", null, "OSC-001", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000001"), "Phòng E101 - Tủ A", null, "OSC-002", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-0000-0000-000000000003"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000001"), "Phòng E101 - Tủ A", null, "OSC-003", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-0000-0000-000000000004"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000002"), "Phòng E101 - Tủ B", null, "DMM-001", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-0000-0000-000000000005"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000002"), "Phòng E101 - Tủ B", null, "DMM-002", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-0000-0000-000000000006"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000002"), "Phòng E101 - Tủ B", null, "DMM-003", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-0000-0000-000000000007"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000002"), "Phòng E101 - Tủ B", null, "DMM-004", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-0000-0000-000000000008"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000003"), "Phòng E102 - Kệ 1", null, "FG-001", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-0000-0000-000000000009"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000003"), "Phòng E102 - Kệ 1", null, "FG-002", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-0000-0000-000000000010"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000004"), "Phòng B201 - Tủ kính", null, "MIC-001", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-0000-0000-000000000011"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000005"), "Phòng E102 - Kệ 2", null, "PSU-001", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-0000-0000-000000000012"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000005"), "Phòng E102 - Kệ 2", null, "PSU-002", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("a0000000-0000-0000-0000-000000000013"), new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("e0000000-0000-0000-0000-000000000005"), "Phòng E102 - Kệ 2", null, "PSU-003", "AVAILABLE", new DateTimeOffset(new DateTime(2026, 11, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetUnits_EquipmentTypeId_Status",
                table: "AssetUnits",
                columns: new[] { "EquipmentTypeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetUnits_SerialNumber",
                table: "AssetUnits",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_MaintenanceRecords_Open",
                table: "MaintenanceRecords",
                column: "AssetUnitId",
                unique: true,
                filter: "\"CompletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_LoanId",
                table: "Reservations",
                column: "LoanId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_OperationId",
                table: "Reservations",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_ActiveAsset",
                table: "Reservations",
                column: "AssetUnitId",
                unique: true,
                filter: "\"Status\" IN ('HELD', 'HANDED_OVER')");

            migrationBuilder.CreateIndex(
                name: "UX_Reservations_ActiveLoanItem",
                table: "Reservations",
                column: "LoanItemId",
                unique: true,
                filter: "\"Status\" IN ('HELD', 'HANDED_OVER')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceRecords");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "ReserveOperations");

            migrationBuilder.DropTable(
                name: "AssetUnits");
        }
    }
}
