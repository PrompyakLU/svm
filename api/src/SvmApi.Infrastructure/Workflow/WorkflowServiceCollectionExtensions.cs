using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SvmApi.Application.Workflow;

namespace SvmApi.Infrastructure.Workflow;

/// <summary>
/// ลงทะเบียน WorkflowModule (C7). idempotent (TryAdd) — Intake/Verification ที่จะเรียก <see cref="IWorkflowService.ReopenAsync"/> เรียกซ้ำได้.
/// <see cref="IEvidenceChecker"/> / <see cref="IApprovalChecker"/> เป็น seam: ลงทะเบียน implementation ของตนก่อนเรียกเมธอดนี้เพื่อแทนที่ค่าเริ่มต้น.
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddSvmWorkflow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IEvidenceChecker, DbEvidenceChecker>();
        services.TryAddScoped<IApprovalChecker, DenyApprovalChecker>();
        services.TryAddScoped<IWorkflowService, WorkflowService>();

        return services;
    }
}
