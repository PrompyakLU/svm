using Microsoft.EntityFrameworkCore;
using SvmApi.Application.Workflow;
using SvmApi.Domain.Remediation;
using SvmApi.Domain.Vulnerabilities;
using SvmApi.Infrastructure.Persistence;

namespace SvmApi.Infrastructure.Workflow;

/// <summary>
/// <see cref="IEvidenceChecker"/> ค่าเริ่มต้น: instance ต้องมีหลักฐานอย่างน้อยหนึ่งไฟล์ใน <c>trans_evidence</c>
/// ที่ผ่าน malware scan แล้ว (<see cref="ScanStatus.Clean"/> — NFR-18, EX-21/22; Pending/Infected/Unscannable ไม่นับ).
/// ไม่กรองด้วย data scope เพราะผู้เรียก (<see cref="WorkflowService"/>) โหลด instance ผ่าน scope แล้ว.
/// </summary>
public sealed class DbEvidenceChecker(SvmDbContext db) : IEvidenceChecker
{
    public async Task<bool> HasRequiredEvidenceAsync(Guid instanceId, CancellationToken cancellationToken)
        => await db.Evidences.AsNoTracking()
            .AnyAsync(e => e.InstanceId == instanceId && e.ScanStatus == ScanStatus.Clean, cancellationToken);
}

/// <summary>
/// <see cref="IApprovalChecker"/> ค่าเริ่มต้น (fail-closed): ยังไม่มี approval module/ตาราง จึงปฏิเสธทุกครั้ง
/// → transition ที่ <c>requiresApproval</c> (FalsePositive/RiskAccepted/ExceptionApproved) ถูกบล็อกจนกว่าจะลงทะเบียน implementation จริงแทน.
/// </summary>
public sealed class DenyApprovalChecker : IApprovalChecker
{
    public Task<bool> HasApprovalAsync(Guid instanceId, VulnerabilityStatus targetStatus, CancellationToken cancellationToken)
        => Task.FromResult(false);
}
