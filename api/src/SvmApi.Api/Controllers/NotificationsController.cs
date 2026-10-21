using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SvmApi.Api.Auth;
using SvmApi.Application.Common;
using SvmApi.Application.Notifications;

namespace SvmApi.Api.Controllers;

/// <summary>
/// NotificationModule (C14 · US-026 · FR-42): in-app notification ของผู้ใช้ปัจจุบัน.
/// ต้อง login เท่านั้น (api-spec: bearer) — ผู้ใช้เห็นและ mark read ได้เฉพาะแถวของตนเอง (กรอง recipient ใน service);
/// id ของคนอื่นตอบ 404 เหมือนไม่มีอยู่.
/// </summary>
[ApiController]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(INotificationInbox inbox) : ControllerBase
{
    /// <summary>รายการ in-app notification (ใหม่สุดก่อน): <c>?unreadOnly=true&amp;page=1&amp;pageSize=25</c>.</summary>
    [HttpGet("api/v1/notifications")]
    [ProducesResponseType<PagedResult<NotificationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
        => Ok(await inbox.ListAsync(unreadOnly, page, pageSize, cancellationToken));

    /// <summary>mark read (idempotent) → 204.</summary>
    [HttpPost("api/v1/notifications/{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
        => await inbox.MarkReadAsync(id, cancellationToken)
            ? NoContent()
            : AuthProblem.ToResult(
                HttpContext, StatusCodes.Status404NotFound, "Not found",
                "The requested record was not found.", NotificationErrorCodes.NotFound);
}
