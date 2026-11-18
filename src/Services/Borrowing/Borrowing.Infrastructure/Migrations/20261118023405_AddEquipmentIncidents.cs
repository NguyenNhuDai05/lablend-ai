using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Borrowing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEquipmentIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "IncidentNumber");

            migrationBuilder.AddColumn<string>(
                name: "AssetSerialNumber",
                table: "LoanItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IncidentId",
                table: "LoanItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesLoanItemId",
                table: "LoanItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EquipmentIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    LoanId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoanItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAssetUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProblemCategory = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ReplacementRequested = table.Column<bool>(type: "boolean", nullable: false),
                    WaitingConsented = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SubmissionKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PendingOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentIncidents", x => x.Id);
                    table.CheckConstraint("CK_Incident_Category", "\"ProblemCategory\" IN ('POWER_FAILURE','PHYSICAL_DAMAGE','FUNCTIONAL_ERROR','MISSING_COMPONENT','OTHER')");
                    table.CheckConstraint("CK_Incident_Description", "length(btrim(\"Description\")) BETWEEN 10 AND 2000");
                    table.CheckConstraint("CK_Incident_Status", "\"Status\" IN ('REPORTED','INSPECTING','CONFIRMED_DEFECT','WAITING_REPLACEMENT','REPLACEMENT_IN_PROGRESS','RESOLVED_REPLACED','RESOLVED_NO_FAULT','RESOLVED_REPAIRED','RESOLVED_NO_REPLACEMENT','CANCELLED','MANUAL_REVIEW')");
                    table.ForeignKey(
                        name: "FK_EquipmentIncidents_LoanItems_LoanItemId",
                        column: x => x.LoanItemId,
                        principalTable: "LoanItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentIncidents_LoanRequests_LoanId",
                        column: x => x.LoanId,
                        principalTable: "LoanRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentReplacements",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplacementLoanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplacementLoanItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplacementAssetUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReplacedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ReplacedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentReplacements", x => x.OperationId);
                    table.ForeignKey(
                        name: "FK_EquipmentReplacements_EquipmentIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "EquipmentIncidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentReplacements_InventoryOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "InventoryOperations",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EquipmentReplacements_LoanRequests_ReplacementLoanId",
                        column: x => x.ReplacementLoanId,
                        principalTable: "LoanRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IncidentEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentEvidence", x => x.Id);
                    table.CheckConstraint("CK_IncidentEvidence_Size", "octet_length(\"Content\") BETWEEN 1 AND 5242880");
                    table.ForeignKey(
                        name: "FK_IncidentEvidence_EquipmentIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "EquipmentIncidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IncidentHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentHistories_EquipmentIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "EquipmentIncidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IncidentInspections",
                columns: table => new
                {
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CorrectiveAction = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    InspectedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PhysicallyReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedSerialNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentInspections", x => x.IncidentId);
                    table.CheckConstraint("CK_Inspection_Physical", "\"Outcome\" = 'LOST' OR (\"PhysicallyReceivedAt\" IS NOT NULL AND \"VerifiedSerialNumber\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_IncidentInspections_EquipmentIncidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "EquipmentIncidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoanItems_IncidentId",
                table: "LoanItems",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_LoanItems_ReplacesLoanItemId",
                table: "LoanItems",
                column: "ReplacesLoanItemId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentIncidents_Code",
                table: "EquipmentIncidents",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentIncidents_EquipmentTypeId_CreatedAt",
                table: "EquipmentIncidents",
                columns: new[] { "EquipmentTypeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentIncidents_LoanId",
                table: "EquipmentIncidents",
                column: "LoanId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentIncidents_LoanItemId",
                table: "EquipmentIncidents",
                column: "LoanItemId",
                unique: true,
                filter: "\"Status\" NOT IN ('RESOLVED_REPLACED','RESOLVED_NO_FAULT','RESOLVED_REPAIRED','RESOLVED_NO_REPLACEMENT','CANCELLED')");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentIncidents_Status_CreatedAt",
                table: "EquipmentIncidents",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentIncidents_StudentId_SubmissionKey",
                table: "EquipmentIncidents",
                columns: new[] { "StudentId", "SubmissionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReplacements_IncidentId",
                table: "EquipmentReplacements",
                column: "IncidentId",
                unique: true,
                filter: "\"ReplacementAssetUnitId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentReplacements_ReplacementLoanId",
                table: "EquipmentReplacements",
                column: "ReplacementLoanId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentEvidence_IncidentId_ContentHash",
                table: "IncidentEvidence",
                columns: new[] { "IncidentId", "ContentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentHistories_IncidentId_OccurredAt",
                table: "IncidentHistories",
                columns: new[] { "IncidentId", "OccurredAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_LoanItems_EquipmentIncidents_IncidentId",
                table: "LoanItems",
                column: "IncidentId",
                principalTable: "EquipmentIncidents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LoanItems_LoanItems_ReplacesLoanItemId",
                table: "LoanItems",
                column: "ReplacesLoanItemId",
                principalTable: "LoanItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LoanItems_EquipmentIncidents_IncidentId",
                table: "LoanItems");

            migrationBuilder.DropForeignKey(
                name: "FK_LoanItems_LoanItems_ReplacesLoanItemId",
                table: "LoanItems");

            migrationBuilder.DropTable(
                name: "EquipmentReplacements");

            migrationBuilder.DropTable(
                name: "IncidentEvidence");

            migrationBuilder.DropTable(
                name: "IncidentHistories");

            migrationBuilder.DropTable(
                name: "IncidentInspections");

            migrationBuilder.DropTable(
                name: "EquipmentIncidents");

            migrationBuilder.DropIndex(
                name: "IX_LoanItems_IncidentId",
                table: "LoanItems");

            migrationBuilder.DropIndex(
                name: "IX_LoanItems_ReplacesLoanItemId",
                table: "LoanItems");

            migrationBuilder.DropColumn(
                name: "AssetSerialNumber",
                table: "LoanItems");

            migrationBuilder.DropColumn(
                name: "IncidentId",
                table: "LoanItems");

            migrationBuilder.DropColumn(
                name: "ReplacesLoanItemId",
                table: "LoanItems");

            migrationBuilder.DropSequence(
                name: "IncidentNumber");
        }
    }
}
