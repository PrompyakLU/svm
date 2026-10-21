using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SvmApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaTimerEscalationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assetId",
                table: "trans_vulnerabilityFinding",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "escalationLevel",
                table: "trans_slaTimer",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "lastEscalatedAt",
                table: "trans_slaTimer",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_trans_vulnerabilityFinding_assetId",
                table: "trans_vulnerabilityFinding",
                column: "assetId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_vulnerabilityFinding_validationStatus_instanceId",
                table: "trans_vulnerabilityFinding",
                columns: new[] { "validationStatus", "instanceId" });

            migrationBuilder.AddForeignKey(
                name: "FK_trans_vulnerabilityFinding_master_asset_assetId",
                table: "trans_vulnerabilityFinding",
                column: "assetId",
                principalTable: "master_asset",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_trans_vulnerabilityFinding_master_asset_assetId",
                table: "trans_vulnerabilityFinding");

            migrationBuilder.DropIndex(
                name: "IX_trans_vulnerabilityFinding_assetId",
                table: "trans_vulnerabilityFinding");

            migrationBuilder.DropIndex(
                name: "IX_trans_vulnerabilityFinding_validationStatus_instanceId",
                table: "trans_vulnerabilityFinding");

            migrationBuilder.DropColumn(
                name: "assetId",
                table: "trans_vulnerabilityFinding");

            migrationBuilder.DropColumn(
                name: "escalationLevel",
                table: "trans_slaTimer");

            migrationBuilder.DropColumn(
                name: "lastEscalatedAt",
                table: "trans_slaTimer");
        }
    }
}
