using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SvmApi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "config_businessCalendar",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    isWorkingDay = table.Column<bool>(type: "bit", nullable: false),
                    description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_businessCalendar", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "config_cvssVersion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    isSupported = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_cvssVersion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "config_permission",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_permission", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "config_riskThreshold",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    riskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    minScore = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    maxScore = table.Column<decimal>(type: "decimal(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_riskThreshold", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "config_riskWeight",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    factor = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    weight = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    isActive = table.Column<bool>(type: "bit", nullable: false),
                    updatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_riskWeight", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "config_role",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    isSystem = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_role", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "config_slaPolicy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    riskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    assetCriticality = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    internetExposure = table.Column<bool>(type: "bit", nullable: true),
                    kevOnly = table.Column<bool>(type: "bit", nullable: false),
                    acknowledgeHours = table.Column<int>(type: "int", nullable: false),
                    startHours = table.Column<int>(type: "int", nullable: false),
                    resolveHours = table.Column<int>(type: "int", nullable: false),
                    isActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_slaPolicy", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "config_statusTransition",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fromStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    toStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    requiredPermission = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    requiresEvidence = table.Column<bool>(type: "bit", nullable: false),
                    requiresApproval = table.Column<bool>(type: "bit", nullable: false),
                    isActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_statusTransition", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "config_user",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    displayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    passwordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    isActive = table.Column<bool>(type: "bit", nullable: false),
                    expiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    createdBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "master_asset",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assetCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    assetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ipAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    hostname = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    environment = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    businessUnit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    criticality = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    internetExposure = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    cloudResourceId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    repositoryId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    createdBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_master_asset", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "master_cve",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    cveId = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    publishedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    isKev = table.Column<bool>(type: "bit", nullable: false),
                    kevDateAdded = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_master_cve", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "master_cwe",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    cweId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_master_cwe", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "trans_auditLog",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    userId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    objectType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    objectId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    beforeValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    afterValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    prevHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    rowHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trans_auditLog", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "config_rolePermission",
                columns: table => new
                {
                    roleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    permissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    createdBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_rolePermission", x => new { x.roleId, x.permissionId });
                    table.ForeignKey(
                        name: "FK_config_rolePermission_config_permission_permissionId",
                        column: x => x.permissionId,
                        principalTable: "config_permission",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_config_rolePermission_config_role_roleId",
                        column: x => x.roleId,
                        principalTable: "config_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "config_assignmentRule",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    matchType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    matchValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    assignToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    assignToTeam = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    priority = table.Column<int>(type: "int", nullable: false),
                    isActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_assignmentRule", x => x.id);
                    table.ForeignKey(
                        name: "FK_config_assignmentRule_config_user_assignToUserId",
                        column: x => x.assignToUserId,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "config_importBatch",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fileName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    transactionId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    totalRows = table.Column<int>(type: "int", nullable: false),
                    successCount = table.Column<int>(type: "int", nullable: false),
                    failedCount = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    importedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    importedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    errorDetail = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_importBatch", x => x.id);
                    table.ForeignKey(
                        name: "FK_config_importBatch_config_user_importedBy",
                        column: x => x.importedBy,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "config_userRole",
                columns: table => new
                {
                    userId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    roleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    createdBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_userRole", x => new { x.userId, x.roleId });
                    table.ForeignKey(
                        name: "FK_config_userRole_config_role_roleId",
                        column: x => x.roleId,
                        principalTable: "config_role",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_config_userRole_config_user_userId",
                        column: x => x.userId,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "config_userScope",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    userId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    scopeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    scopeValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_config_userScope", x => x.id);
                    table.ForeignKey(
                        name: "FK_config_userScope_config_user_userId",
                        column: x => x.userId,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trans_authSession",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    userId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    refreshTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    previousRefreshTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    lastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    absoluteExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    revokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trans_authSession", x => x.id);
                    table.ForeignKey(
                        name: "FK_trans_authSession_config_user_userId",
                        column: x => x.userId,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trans_notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    relatedInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    deliveryStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trans_notification", x => x.id);
                    table.ForeignKey(
                        name: "FK_trans_notification_config_user_recipientUserId",
                        column: x => x.recipientUserId,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "master_assetOwnership",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ownerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ownerType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    teamName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_master_assetOwnership", x => x.id);
                    table.ForeignKey(
                        name: "FK_master_assetOwnership_config_user_ownerUserId",
                        column: x => x.ownerUserId,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_master_assetOwnership_master_asset_assetId",
                        column: x => x.assetId,
                        principalTable: "master_asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trans_vulnerabilityInstance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    cveId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    cweId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    port = table.Column<int>(type: "int", nullable: true),
                    component = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    firstDetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    lastDetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    riskScore = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    riskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    isKev = table.Column<bool>(type: "bit", nullable: false),
                    assignedToUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    createdBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    rowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trans_vulnerabilityInstance", x => x.id);
                    table.ForeignKey(
                        name: "FK_trans_vulnerabilityInstance_config_user_assignedToUserId",
                        column: x => x.assignedToUserId,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trans_vulnerabilityInstance_master_asset_assetId",
                        column: x => x.assetId,
                        principalTable: "master_asset",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "trans_comment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    mentions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    createdBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trans_comment", x => x.id);
                    table.ForeignKey(
                        name: "FK_trans_comment_config_user_createdBy",
                        column: x => x.createdBy,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trans_comment_trans_vulnerabilityInstance_instanceId",
                        column: x => x.instanceId,
                        principalTable: "trans_vulnerabilityInstance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trans_remediationTask",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ownerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ownerTeam = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    dueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    riskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    recommendation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    rootCause = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    actionPlan = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    targetDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    changeReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    progressPercent = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    obstacle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    createdBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trans_remediationTask", x => x.id);
                    table.ForeignKey(
                        name: "FK_trans_remediationTask_config_user_ownerUserId",
                        column: x => x.ownerUserId,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trans_remediationTask_trans_vulnerabilityInstance_instanceId",
                        column: x => x.instanceId,
                        principalTable: "trans_vulnerabilityInstance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trans_riskScoreHistory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    score = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    riskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    calculationType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    factorBreakdown = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    previousScore = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    createdBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trans_riskScoreHistory", x => x.id);
                    table.ForeignKey(
                        name: "FK_trans_riskScoreHistory_trans_vulnerabilityInstance_instanceId",
                        column: x => x.instanceId,
                        principalTable: "trans_vulnerabilityInstance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trans_slaTimer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    acknowledgeDue = table.Column<DateTime>(type: "datetime2", nullable: false),
                    remediationStartDue = table.Column<DateTime>(type: "datetime2", nullable: false),
                    resolutionDue = table.Column<DateTime>(type: "datetime2", nullable: false),
                    startedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    breachStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    policyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    previousDue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trans_slaTimer", x => x.id);
                    table.ForeignKey(
                        name: "FK_trans_slaTimer_config_slaPolicy_policyId",
                        column: x => x.policyId,
                        principalTable: "config_slaPolicy",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_trans_slaTimer_trans_vulnerabilityInstance_instanceId",
                        column: x => x.instanceId,
                        principalTable: "trans_vulnerabilityInstance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trans_statusHistory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fromStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    toStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    changedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    changedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trans_statusHistory", x => x.id);
                    table.ForeignKey(
                        name: "FK_trans_statusHistory_config_user_changedBy",
                        column: x => x.changedBy,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trans_statusHistory_trans_vulnerabilityInstance_instanceId",
                        column: x => x.instanceId,
                        principalTable: "trans_vulnerabilityInstance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trans_verificationRecord",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    isIndependent = table.Column<bool>(type: "bit", nullable: false),
                    verifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    verifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trans_verificationRecord", x => x.id);
                    table.ForeignKey(
                        name: "FK_trans_verificationRecord_config_user_verifiedBy",
                        column: x => x.verifiedBy,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trans_verificationRecord_trans_vulnerabilityInstance_instanceId",
                        column: x => x.instanceId,
                        principalTable: "trans_vulnerabilityInstance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trans_vulnerabilityFinding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    sourceSystem = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    sourceRecordId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    importBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    findingName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    rawSeverity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    cveId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    cweId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    cvssVersion = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    cvssBaseScore = table.Column<decimal>(type: "decimal(4,1)", nullable: true),
                    cvssVector = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    port = table.Column<int>(type: "int", nullable: true),
                    component = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    detectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    isDuplicate = table.Column<bool>(type: "bit", nullable: false),
                    validationStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    createdAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    createdBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trans_vulnerabilityFinding", x => x.id);
                    table.ForeignKey(
                        name: "FK_trans_vulnerabilityFinding_config_importBatch_importBatchId",
                        column: x => x.importBatchId,
                        principalTable: "config_importBatch",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_trans_vulnerabilityFinding_trans_vulnerabilityInstance_instanceId",
                        column: x => x.instanceId,
                        principalTable: "trans_vulnerabilityInstance",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "trans_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    instanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    taskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    verificationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    fileName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    storagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    contentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    fileSize = table.Column<long>(type: "bigint", nullable: false),
                    scanStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    uploadedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    uploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trans_evidence", x => x.id);
                    table.ForeignKey(
                        name: "FK_trans_evidence_config_user_uploadedBy",
                        column: x => x.uploadedBy,
                        principalTable: "config_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trans_evidence_trans_remediationTask_taskId",
                        column: x => x.taskId,
                        principalTable: "trans_remediationTask",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_trans_evidence_trans_verificationRecord_verificationId",
                        column: x => x.verificationId,
                        principalTable: "trans_verificationRecord",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trans_evidence_trans_vulnerabilityInstance_instanceId",
                        column: x => x.instanceId,
                        principalTable: "trans_vulnerabilityInstance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_config_assignmentRule_assignToUserId",
                table: "config_assignmentRule",
                column: "assignToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_config_businessCalendar_date",
                table: "config_businessCalendar",
                column: "date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_config_cvssVersion_version",
                table: "config_cvssVersion",
                column: "version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_config_importBatch_importedBy",
                table: "config_importBatch",
                column: "importedBy");

            migrationBuilder.CreateIndex(
                name: "IX_config_importBatch_transactionId",
                table: "config_importBatch",
                column: "transactionId",
                unique: true,
                filter: "[transactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_config_permission_code",
                table: "config_permission",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_config_riskWeight_factor",
                table: "config_riskWeight",
                column: "factor",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_config_role_code",
                table: "config_role",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_config_rolePermission_permissionId",
                table: "config_rolePermission",
                column: "permissionId");

            migrationBuilder.CreateIndex(
                name: "IX_config_statusTransition_fromStatus_toStatus",
                table: "config_statusTransition",
                columns: new[] { "fromStatus", "toStatus" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_config_user_isActive",
                table: "config_user",
                column: "isActive");

            migrationBuilder.CreateIndex(
                name: "IX_config_user_username",
                table: "config_user",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_config_userRole_roleId",
                table: "config_userRole",
                column: "roleId");

            migrationBuilder.CreateIndex(
                name: "IX_config_userScope_userId",
                table: "config_userScope",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "IX_master_asset_assetCode",
                table: "master_asset",
                column: "assetCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_master_asset_businessUnit",
                table: "master_asset",
                column: "businessUnit");

            migrationBuilder.CreateIndex(
                name: "IX_master_asset_criticality",
                table: "master_asset",
                column: "criticality");

            migrationBuilder.CreateIndex(
                name: "IX_master_asset_internetExposure",
                table: "master_asset",
                column: "internetExposure");

            migrationBuilder.CreateIndex(
                name: "IX_master_asset_status",
                table: "master_asset",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_master_assetOwnership_assetId_ownerType",
                table: "master_assetOwnership",
                columns: new[] { "assetId", "ownerType" });

            migrationBuilder.CreateIndex(
                name: "IX_master_assetOwnership_ownerUserId",
                table: "master_assetOwnership",
                column: "ownerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_master_cve_cveId",
                table: "master_cve",
                column: "cveId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_master_cwe_cweId",
                table: "master_cwe",
                column: "cweId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trans_auditLog_action",
                table: "trans_auditLog",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "IX_trans_auditLog_objectType_objectId",
                table: "trans_auditLog",
                columns: new[] { "objectType", "objectId" });

            migrationBuilder.CreateIndex(
                name: "IX_trans_auditLog_timestamp",
                table: "trans_auditLog",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_trans_auditLog_userId",
                table: "trans_auditLog",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_authSession_absoluteExpiresAt",
                table: "trans_authSession",
                column: "absoluteExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_trans_authSession_userId",
                table: "trans_authSession",
                column: "userId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_comment_createdBy",
                table: "trans_comment",
                column: "createdBy");

            migrationBuilder.CreateIndex(
                name: "IX_trans_comment_instanceId",
                table: "trans_comment",
                column: "instanceId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_evidence_instanceId",
                table: "trans_evidence",
                column: "instanceId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_evidence_taskId",
                table: "trans_evidence",
                column: "taskId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_evidence_uploadedBy",
                table: "trans_evidence",
                column: "uploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_trans_evidence_verificationId",
                table: "trans_evidence",
                column: "verificationId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_notification_createdAt",
                table: "trans_notification",
                column: "createdAt");

            migrationBuilder.CreateIndex(
                name: "IX_trans_notification_recipientUserId_deliveryStatus",
                table: "trans_notification",
                columns: new[] { "recipientUserId", "deliveryStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_trans_remediationTask_dueDate",
                table: "trans_remediationTask",
                column: "dueDate");

            migrationBuilder.CreateIndex(
                name: "IX_trans_remediationTask_instanceId",
                table: "trans_remediationTask",
                column: "instanceId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_remediationTask_ownerUserId",
                table: "trans_remediationTask",
                column: "ownerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_remediationTask_status",
                table: "trans_remediationTask",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_trans_riskScoreHistory_instanceId_createdAt",
                table: "trans_riskScoreHistory",
                columns: new[] { "instanceId", "createdAt" });

            migrationBuilder.CreateIndex(
                name: "IX_trans_slaTimer_breachStatus",
                table: "trans_slaTimer",
                column: "breachStatus");

            migrationBuilder.CreateIndex(
                name: "IX_trans_slaTimer_instanceId",
                table: "trans_slaTimer",
                column: "instanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trans_slaTimer_policyId",
                table: "trans_slaTimer",
                column: "policyId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_slaTimer_resolutionDue",
                table: "trans_slaTimer",
                column: "resolutionDue");

            migrationBuilder.CreateIndex(
                name: "IX_trans_statusHistory_changedBy",
                table: "trans_statusHistory",
                column: "changedBy");

            migrationBuilder.CreateIndex(
                name: "IX_trans_statusHistory_instanceId_changedAt",
                table: "trans_statusHistory",
                columns: new[] { "instanceId", "changedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_trans_verificationRecord_instanceId",
                table: "trans_verificationRecord",
                column: "instanceId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_verificationRecord_verifiedBy",
                table: "trans_verificationRecord",
                column: "verifiedBy");

            migrationBuilder.CreateIndex(
                name: "IX_trans_vulnerabilityFinding_cveId",
                table: "trans_vulnerabilityFinding",
                column: "cveId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_vulnerabilityFinding_importBatchId",
                table: "trans_vulnerabilityFinding",
                column: "importBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_vulnerabilityFinding_instanceId",
                table: "trans_vulnerabilityFinding",
                column: "instanceId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_vulnerabilityFinding_sourceSystem_sourceRecordId",
                table: "trans_vulnerabilityFinding",
                columns: new[] { "sourceSystem", "sourceRecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_trans_vulnerabilityInstance_assetId",
                table: "trans_vulnerabilityInstance",
                column: "assetId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_vulnerabilityInstance_assignedToUserId",
                table: "trans_vulnerabilityInstance",
                column: "assignedToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_trans_vulnerabilityInstance_isKev",
                table: "trans_vulnerabilityInstance",
                column: "isKev");

            migrationBuilder.CreateIndex(
                name: "IX_trans_vulnerabilityInstance_riskLevel",
                table: "trans_vulnerabilityInstance",
                column: "riskLevel");

            migrationBuilder.CreateIndex(
                name: "IX_trans_vulnerabilityInstance_status",
                table: "trans_vulnerabilityInstance",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_vulnerabilityInstance_dedupKey",
                table: "trans_vulnerabilityInstance",
                columns: new[] { "assetId", "cveId", "location", "port", "component" },
                unique: true,
                filter: "[status] <> 'Archived'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "config_assignmentRule");

            migrationBuilder.DropTable(
                name: "config_businessCalendar");

            migrationBuilder.DropTable(
                name: "config_cvssVersion");

            migrationBuilder.DropTable(
                name: "config_riskThreshold");

            migrationBuilder.DropTable(
                name: "config_riskWeight");

            migrationBuilder.DropTable(
                name: "config_rolePermission");

            migrationBuilder.DropTable(
                name: "config_statusTransition");

            migrationBuilder.DropTable(
                name: "config_userRole");

            migrationBuilder.DropTable(
                name: "config_userScope");

            migrationBuilder.DropTable(
                name: "master_assetOwnership");

            migrationBuilder.DropTable(
                name: "master_cve");

            migrationBuilder.DropTable(
                name: "master_cwe");

            migrationBuilder.DropTable(
                name: "trans_auditLog");

            migrationBuilder.DropTable(
                name: "trans_authSession");

            migrationBuilder.DropTable(
                name: "trans_comment");

            migrationBuilder.DropTable(
                name: "trans_evidence");

            migrationBuilder.DropTable(
                name: "trans_notification");

            migrationBuilder.DropTable(
                name: "trans_riskScoreHistory");

            migrationBuilder.DropTable(
                name: "trans_slaTimer");

            migrationBuilder.DropTable(
                name: "trans_statusHistory");

            migrationBuilder.DropTable(
                name: "trans_vulnerabilityFinding");

            migrationBuilder.DropTable(
                name: "config_permission");

            migrationBuilder.DropTable(
                name: "config_role");

            migrationBuilder.DropTable(
                name: "trans_remediationTask");

            migrationBuilder.DropTable(
                name: "trans_verificationRecord");

            migrationBuilder.DropTable(
                name: "config_slaPolicy");

            migrationBuilder.DropTable(
                name: "config_importBatch");

            migrationBuilder.DropTable(
                name: "trans_vulnerabilityInstance");

            migrationBuilder.DropTable(
                name: "config_user");

            migrationBuilder.DropTable(
                name: "master_asset");
        }
    }
}
