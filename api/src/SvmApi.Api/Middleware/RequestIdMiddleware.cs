namespace SvmApi.Api.Middleware;

/// <summary>
/// Correlation id: อ่าน X-Request-Id จาก request (forward ถ้ามี), ไม่งั้น gen จาก TraceIdentifier,
/// ใส่กลับใน response header และผูกเป็น log scope (operations.md — Correlation).
/// </summary>
public sealed class RequestIdMiddleware(RequestDelegate next, ILogger<RequestIdMiddleware> logger)
{
    public const string HeaderName = "X-Request-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            requestId = context.TraceIdentifier;
        }

        context.Response.Headers[HeaderName] = requestId;

        using (logger.BeginScope(new Dictionary<string, object> { ["requestId"] = requestId }))
        {
            await next(context);
        }
    }
}
