using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SvmApi.Domain.Access;
using SvmApi.Domain.Assets;
using SvmApi.Domain.Reference;
using SvmApi.Domain.Remediation;
using SvmApi.Domain.Risk;
using SvmApi.Domain.Sla;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Domain.Workflow;

namespace SvmApi.Infrastructure.Persistence.Seed;

/// <summary>
/// Seed ข้อมูลเริ่มต้น/ตัวอย่างของ SVM (permission, role, user, asset, reference, config).
/// <list type="bullet">
/// <item>Idempotent: ตรวจด้วย natural key แล้วเพิ่มเฉพาะที่ยังไม่มี — ไม่เขียนทับสิ่งที่แอดมินแก้ไปแล้ว
/// (role/user ที่มีอยู่ถูกข้ามทั้งชุด รวม permission/role/scope ของมัน)</item>
/// <item>ไม่เรียก Migrate: ถ้ายังมี migration ค้างหรือเชื่อมต่อ DB ไม่ได้ → log warning แล้วข้าม ไม่ throw</item>
/// <item>ไม่มีรหัสผ่านใน code: demo users สร้างเมื่อ <see cref="SeedOptions.DemoPassword"/> ไม่ว่างเท่านั้น (hash ด้วย <see cref="IPasswordHasher{TUser}"/>)</item>
/// <item>แต่ละ stage commit แยกกัน (atomic ต่อ stage); stage ที่ล้มเหลวไม่ทำให้ API ล้ม</item>
/// </list>
/// ข้อมูลเป็น demo seed เท่านั้น ห้ามใช้เป็นข้อมูลจริงใน production · FR-01..03, FR-13/14, FR-16, FR-22/23, FR-35/66, NFR-16.
/// </summary>
public sealed class SvmDbSeeder(
    SvmDbContext db,
    IPasswordHasher<User> passwordHasher,
    IOptions<SeedOptions> options,
    TimeProvider timeProvider,
    ILogger<SvmDbSeeder> logger)
{
    private readonly SeedOptions _options = options.Value;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!await CanSeedAsync(ct))
        {
            return;
        }

        var demoUsersEnabled = !string.IsNullOrWhiteSpace(_options.DemoPassword);
        if (!demoUsersEnabled)
        {
            logger.LogError(
                "Seed:DemoPassword is not configured: demo users, user roles, user scopes and asset ownerships "
                + "will NOT be seeded (reference/config/asset data still is). Set it via user-secrets or the "
                + "Seed__DemoPassword environment variable and restart to seed users.");
        }

        // ลำดับสำคัญ: permission -> role -> user -> reference/config -> asset -> ownership
        await RunStageAsync("permissions", SeedPermissionsAsync, ct);
        await RunStageAsync("roles", SeedRolesAsync, ct);
        await RunStageAsync("role-grants", SeedRoleGrantsAsync, ct);
        await RunStageAsync("users", c => SeedUsersAsync(demoUsersEnabled, c), ct);

        await RunStageAsync("cve", SeedCvesAsync, ct);
        await RunStageAsync("cwe", SeedCwesAsync, ct);
        await RunStageAsync("cvss-versions", SeedCvssVersionsAsync, ct);

        await RunStageAsync("risk-weights", SeedRiskWeightsAsync, ct);
        await RunStageAsync("risk-thresholds", SeedRiskThresholdsAsync, ct);
        await RunStageAsync("status-transitions", SeedStatusTransitionsAsync, ct);
        await RunStageAsync("sla-policies", SeedSlaPoliciesAsync, ct);
        await RunStageAsync("business-calendar", SeedBusinessCalendarAsync, ct);
        await RunStageAsync("assignment-rules", SeedAssignmentRulesAsync, ct);

        await RunStageAsync("assets", SeedAssetsAsync, ct);
        await RunStageAsync("asset-ownerships", SeedAssetOwnershipsAsync, ct);
    }

    // ---------------------------------------------------------------- guards

    private async Task<bool> CanSeedAsync(CancellationToken ct)
    {
        try
        {
            // provider ที่ไม่ใช่ relational (เช่น in-memory ในการทดสอบ) ไม่มีแนวคิด migration
            if (db.Database.IsRelational())
            {
                var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                if (pending.Count > 0)
                {
                    logger.LogWarning(
                        "Seeding skipped: {PendingCount} pending migration(s). Apply migrations first (the seeder never migrates).",
                        pending.Count);
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Seeding skipped: unable to check migrations / connect to the database.");
            return false;
        }
    }

    private async Task RunStageAsync(string stage, Func<CancellationToken, Task<int>> action, CancellationToken ct)
    {
        try
        {
            var added = await action(ct);
            logger.LogInformation("Seed stage '{Stage}': added {Added} row(s).", stage, added);
        }
        catch (DbUpdateException ex)
        {
            // เช่น instance อื่น seed พร้อมกันจนชน unique index — ปลอดภัยที่จะข้าม
            db.ChangeTracker.Clear();
            logger.LogWarning(ex, "Seed stage '{Stage}' could not be saved (possible concurrent seeding); skipped.", stage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            logger.LogError(ex, "Seed stage '{Stage}' failed; continuing with remaining stages.", stage);
        }
    }

    private async Task<int> SaveIfAnyAsync(int added, CancellationToken ct)
    {
        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return added;
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    private static HashSet<string> NewKeySet(IEnumerable<string> keys)
        => new(keys, StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- access

    private async Task<int> SeedPermissionsAsync(CancellationToken ct)
    {
        var existing = NewKeySet(await db.Permissions.AsNoTracking().Select(p => p.Code).ToListAsync(ct));
        var added = 0;

        foreach (var def in PermissionCatalog.All)
        {
            if (!existing.Add(def.Code))
            {
                continue;
            }

            db.Permissions.Add(new Permission
            {
                Id = Guid.NewGuid(),
                Code = def.Code,
                Description = def.Description,
            });
            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }

    private async Task<int> SeedRolesAsync(CancellationToken ct)
    {
        var existing = NewKeySet(await db.Roles.AsNoTracking().Select(r => r.Code).ToListAsync(ct));
        var permissionIds = await db.Permissions.AsNoTracking()
            .Select(p => new { p.Code, p.Id })
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);

        var now = UtcNow;
        var added = 0;

        foreach (var def in SeedData.Roles)
        {
            // role ที่มีอยู่แล้วถูกข้ามทั้งชุด (รวม role->permission) เพื่อไม่ทับสิ่งที่แอดมินปรับ
            if (!existing.Add(def.Code))
            {
                continue;
            }

            var role = new Role
            {
                Id = Guid.NewGuid(),
                Code = def.Code,
                Name = def.Name,
                Description = def.Description,
                IsSystem = true,
            };
            db.Roles.Add(role);
            added++;

            foreach (var permissionCode in def.Permissions.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!permissionIds.TryGetValue(permissionCode, out var permissionId))
                {
                    logger.LogWarning(
                        "Role {Role}: permission '{Permission}' not found in config_permission; skipped.",
                        def.Code, permissionCode);
                    continue;
                }

                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permissionId,
                    CreatedAt = now,
                });
            }
        }

        return await SaveIfAnyAsync(added, ct);
    }

    /// <summary>
    /// patch แบบ additive ให้ role ที่มีอยู่แล้ว (<see cref="SeedData.RoleGrantPatches"/>): เพิ่มเฉพาะ link ที่ยังไม่มี
    /// ไม่ลบ/แก้ row เดิม; ข้ามเงียบ ๆ ถ้าไม่พบ role หรือ permission. คืนจำนวน row ที่เพิ่ม.
    /// </summary>
    private async Task<int> SeedRoleGrantsAsync(CancellationToken ct)
    {
        var roleIds = await db.Roles.AsNoTracking()
            .Select(r => new { r.Code, r.Id })
            .ToDictionaryAsync(r => r.Code, r => r.Id, StringComparer.OrdinalIgnoreCase, ct);
        var permissionIds = await db.Permissions.AsNoTracking()
            .Select(p => new { p.Code, p.Id })
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);
        var links = await db.RolePermissions.AsNoTracking()
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync(ct);
        var existing = links.Select(l => (l.RoleId, l.PermissionId)).ToHashSet();

        var now = UtcNow;
        var added = 0;

        foreach (var (roleCode, permissionCodes) in SeedData.RoleGrantPatches)
        {
            if (!roleIds.TryGetValue(roleCode, out var roleId))
            {
                continue;
            }

            foreach (var permissionCode in permissionCodes.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!permissionIds.TryGetValue(permissionCode, out var permissionId)
                    || !existing.Add((roleId, permissionId)))
                {
                    continue;
                }

                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = permissionId,
                    CreatedAt = now,
                });
                added++;
            }
        }

        return await SaveIfAnyAsync(added, ct);
    }

    private async Task<int> SeedUsersAsync(bool demoUsersEnabled, CancellationToken ct)
    {
        if (!demoUsersEnabled)
        {
            return 0;
        }

        var usernames = SeedData.DemoUsers.Select(u => u.Username).ToList();
        var existing = NewKeySet(await db.Users.AsNoTracking()
            .Where(u => usernames.Contains(u.Username))
            .Select(u => u.Username)
            .ToListAsync(ct));
        var roleIds = await db.Roles.AsNoTracking()
            .Select(r => new { r.Code, r.Id })
            .ToDictionaryAsync(r => r.Code, r => r.Id, StringComparer.OrdinalIgnoreCase, ct);

        var password = _options.DemoPassword!;
        var now = UtcNow;
        var added = 0;

        foreach (var def in SeedData.DemoUsers)
        {
            // user ที่มีอยู่แล้วถูกข้ามทั้งชุด (รวม role/scope) — ไม่เปลี่ยนรหัสผ่านหรือสิทธิ์ที่แอดมินตั้งไว้
            if (existing.Contains(def.Username))
            {
                continue;
            }

            if (!roleIds.TryGetValue(def.RoleCode, out var roleId))
            {
                logger.LogWarning("Demo user {Username}: role '{Role}' not found; skipped.", def.Username, def.RoleCode);
                continue;
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = def.Username,
                DisplayName = def.DisplayName,
                Email = def.Email,
                IsActive = true,
                CreatedAt = now,
            };
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            db.Users.Add(user);

            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = roleId,
                CreatedAt = now,
            });

            foreach (var scope in def.Scopes)
            {
                db.UserScopes.Add(new UserScope
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    ScopeType = scope.Type,
                    ScopeValue = scope.Value,
                });
            }

            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }

    // ------------------------------------------------------------- reference

    private async Task<int> SeedCvesAsync(CancellationToken ct)
    {
        var existing = NewKeySet(await db.Cves.AsNoTracking().Select(c => c.CveId).ToListAsync(ct));
        var added = 0;

        foreach (var def in SeedData.Cves)
        {
            if (!existing.Add(def.CveId))
            {
                continue;
            }

            db.Cves.Add(new Cve
            {
                Id = Guid.NewGuid(),
                CveId = def.CveId,
                Description = def.Description,
                PublishedDate = def.PublishedDate,
                IsKev = def.IsKev,
                KevDateAdded = def.KevDateAdded,
            });
            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }

    private async Task<int> SeedCwesAsync(CancellationToken ct)
    {
        var existing = NewKeySet(await db.Cwes.AsNoTracking().Select(c => c.CweId).ToListAsync(ct));
        var added = 0;

        foreach (var def in SeedData.Cwes)
        {
            if (!existing.Add(def.CweId))
            {
                continue;
            }

            db.Cwes.Add(new Cwe
            {
                Id = Guid.NewGuid(),
                CweId = def.CweId,
                Name = def.Name,
                Description = def.Description,
            });
            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }

    private async Task<int> SeedCvssVersionsAsync(CancellationToken ct)
    {
        var existing = NewKeySet(await db.CvssVersions.AsNoTracking().Select(v => v.Version).ToListAsync(ct));
        var added = 0;

        foreach (var def in SeedData.CvssVersions)
        {
            if (!existing.Add(def.Version))
            {
                continue;
            }

            db.CvssVersions.Add(new CvssVersion
            {
                Id = Guid.NewGuid(),
                Version = def.Version,
                IsSupported = def.IsSupported,
            });
            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }

    // ---------------------------------------------------------------- config

    private async Task<int> SeedRiskWeightsAsync(CancellationToken ct)
    {
        var existing = (await db.RiskWeights.AsNoTracking().Select(w => w.Factor).ToListAsync(ct)).ToHashSet();
        var added = 0;

        foreach (var def in SeedData.RiskWeights)
        {
            if (!existing.Add(def.Factor))
            {
                continue;
            }

            db.RiskWeights.Add(new RiskWeight
            {
                Id = Guid.NewGuid(),
                Factor = def.Factor,
                Weight = def.Weight,
                IsActive = true,
            });
            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }

    private async Task<int> SeedRiskThresholdsAsync(CancellationToken ct)
    {
        var existing = (await db.RiskThresholds.AsNoTracking().Select(t => t.RiskLevel).ToListAsync(ct)).ToHashSet();
        var added = 0;

        foreach (var def in SeedData.RiskThresholds)
        {
            if (!existing.Add(def.RiskLevel))
            {
                continue;
            }

            db.RiskThresholds.Add(new RiskThreshold
            {
                Id = Guid.NewGuid(),
                RiskLevel = def.RiskLevel,
                MinScore = def.MinScore,
                MaxScore = def.MaxScore,
            });
            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }

    private async Task<int> SeedStatusTransitionsAsync(CancellationToken ct)
    {
        var rows = await db.StatusTransitions.AsNoTracking()
            .Select(t => new { t.FromStatus, t.ToStatus })
            .ToListAsync(ct);
        var existing = rows.Select(r => (r.FromStatus, r.ToStatus)).ToHashSet();
        var added = 0;

        foreach (var def in SeedData.StatusTransitions)
        {
            if (!existing.Add((def.From, def.To)))
            {
                continue;
            }

            db.StatusTransitions.Add(new StatusTransition
            {
                Id = Guid.NewGuid(),
                FromStatus = def.From,
                ToStatus = def.To,
                RequiredPermission = def.RequiredPermission,
                RequiresEvidence = def.RequiresEvidence,
                RequiresApproval = def.RequiresApproval,
                IsActive = true,
            });
            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }

    private async Task<int> SeedSlaPoliciesAsync(CancellationToken ct)
    {
        var rows = await db.SlaPolicies.AsNoTracking()
            .Select(p => new { p.RiskLevel, p.AssetCriticality, p.InternetExposure, p.KevOnly })
            .ToListAsync(ct);
        var existing = rows
            .Select(r => (r.RiskLevel, r.AssetCriticality, r.InternetExposure, r.KevOnly))
            .ToHashSet();
        var added = 0;

        foreach (var def in SeedData.SlaPolicies)
        {
            if (!existing.Add((def.RiskLevel, def.AssetCriticality, def.InternetExposure, def.KevOnly)))
            {
                continue;
            }

            db.SlaPolicies.Add(new SlaPolicy
            {
                Id = Guid.NewGuid(),
                RiskLevel = def.RiskLevel,
                AssetCriticality = def.AssetCriticality,
                InternetExposure = def.InternetExposure,
                KevOnly = def.KevOnly,
                AcknowledgeHours = def.AcknowledgeHours,
                StartHours = def.StartHours,
                ResolveHours = def.ResolveHours,
                IsActive = true,
            });
            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }

    /// <summary>
    /// ปฏิทินทำงาน: 1 ม.ค. ปีปัจจุบัน ถึง 31 ธ.ค. ปีถัดไป — จันทร์–ศุกร์ = วันทำงาน, เสาร์–อาทิตย์ = ไม่ใช่.
    /// ไม่ seed วันหยุดนักขัตฤกษ์ (ผู้ดูแลระบบกำหนดเองผ่าน admin) · EX-67, US-024.
    /// </summary>
    private async Task<int> SeedBusinessCalendarAsync(CancellationToken ct)
    {
        var year = timeProvider.GetUtcNow().Year;
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year + 1, 12, 31);

        var existing = (await db.BusinessCalendars.AsNoTracking()
            .Where(c => c.Date >= start && c.Date <= end)
            .Select(c => c.Date)
            .ToListAsync(ct)).ToHashSet();

        var added = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (existing.Contains(date))
            {
                continue;
            }

            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            db.BusinessCalendars.Add(new BusinessCalendar
            {
                Id = Guid.NewGuid(),
                Date = date,
                IsWorkingDay = !isWeekend,
                Description = isWeekend ? "Weekend" : "Working day",
            });
            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }

    private async Task<int> SeedAssignmentRulesAsync(CancellationToken ct)
    {
        var rows = await db.AssignmentRules.AsNoTracking()
            .Select(r => new { r.MatchType, r.MatchValue })
            .ToListAsync(ct);
        var existing = rows
            .Select(r => (r.MatchType, Value: r.MatchValue.ToUpperInvariant()))
            .ToHashSet();
        var added = 0;

        foreach (var def in SeedData.AssignmentRules)
        {
            if (!existing.Add((def.MatchType, def.MatchValue.ToUpperInvariant())))
            {
                continue;
            }

            db.AssignmentRules.Add(new AssignmentRule
            {
                Id = Guid.NewGuid(),
                MatchType = def.MatchType,
                MatchValue = def.MatchValue,
                AssignToTeam = def.AssignToTeam,
                Priority = def.Priority,
                IsActive = true,
            });
            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }

    // ---------------------------------------------------------------- assets

    private async Task<int> SeedAssetsAsync(CancellationToken ct)
    {
        var existing = NewKeySet(await db.Assets.AsNoTracking().Select(a => a.AssetCode).ToListAsync(ct));
        var now = UtcNow;
        var added = 0;

        foreach (var def in SeedData.Assets)
        {
            if (!existing.Add(def.AssetCode))
            {
                continue;
            }

            db.Assets.Add(new Asset
            {
                Id = Guid.NewGuid(),
                AssetCode = def.AssetCode,
                Name = def.Name,
                AssetType = def.AssetType,
                IpAddress = def.IpAddress,
                Hostname = def.Hostname,
                Url = def.Url,
                Environment = def.Environment,
                BusinessUnit = def.BusinessUnit,
                Criticality = def.Criticality,
                InternetExposure = def.InternetExposure,
                Status = AssetStatus.Active,
                CloudResourceId = def.CloudResourceId,
                RepositoryId = def.RepositoryId,
                CreatedAt = now,
            });
            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }

    /// <summary>
    /// ผูกเจ้าของ asset กับ demo users — natural key = (asset, ownerType): ถ้า asset มี owner ประเภทนั้นอยู่แล้ว
    /// (ไม่ว่าใครเป็นเจ้าของ) จะไม่เพิ่ม; ถ้าไม่พบ user (ไม่ได้ seed demo users) จะข้ามรายการนั้น.
    /// </summary>
    private async Task<int> SeedAssetOwnershipsAsync(CancellationToken ct)
    {
        var assetCodes = SeedData.Assets.Select(a => a.AssetCode).ToList();
        var usernames = SeedData.DemoUsers.Select(u => u.Username).ToList();

        var assetIds = await db.Assets.AsNoTracking()
            .Where(a => assetCodes.Contains(a.AssetCode))
            .Select(a => new { a.AssetCode, a.Id })
            .ToDictionaryAsync(a => a.AssetCode, a => a.Id, StringComparer.OrdinalIgnoreCase, ct);

        var userIds = await db.Users.AsNoTracking()
            .Where(u => usernames.Contains(u.Username))
            .Select(u => new { u.Username, u.Id })
            .ToDictionaryAsync(u => u.Username, u => u.Id, StringComparer.OrdinalIgnoreCase, ct);

        if (assetIds.Count == 0 || userIds.Count == 0)
        {
            return 0;
        }

        var seededAssetIds = assetIds.Values.ToList();
        var ownershipRows = await db.AssetOwnerships.AsNoTracking()
            .Where(o => seededAssetIds.Contains(o.AssetId))
            .Select(o => new { o.AssetId, o.OwnerType })
            .ToListAsync(ct);
        var existing = ownershipRows.Select(o => (o.AssetId, o.OwnerType)).ToHashSet();

        var added = 0;
        foreach (var def in SeedData.AssetOwnerships)
        {
            if (!assetIds.TryGetValue(def.AssetCode, out var assetId)
                || !userIds.TryGetValue(def.Username, out var userId))
            {
                continue;
            }

            if (!existing.Add((assetId, def.OwnerType)))
            {
                continue;
            }

            db.AssetOwnerships.Add(new AssetOwnership
            {
                Id = Guid.NewGuid(),
                AssetId = assetId,
                OwnerUserId = userId,
                OwnerType = def.OwnerType,
                TeamName = def.TeamName,
            });
            added++;
        }

        return await SaveIfAnyAsync(added, ct);
    }
}
