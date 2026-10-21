using System.Threading.Channels;
using SvmApi.Application.Admin;

namespace SvmApi.Infrastructure.Admin;

/// <summary>Bounded, non-blocking in-process queue. Requests are copied so callers cannot mutate queued work.</summary>
public sealed class SlaRecalculationQueue : ISlaRecalculationScheduler
{
    private readonly Channel<SlaRecalculationRequest> channel = Channel.CreateBounded<SlaRecalculationRequest>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public bool Enqueue(SlaRecalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RiskLevels is null || request.RiskLevels.Count == 0 || request.RiskLevels.Any(x => !Enum.IsDefined(x))) return false;
        return channel.Writer.TryWrite(new SlaRecalculationRequest(request.RiskLevels.Distinct().ToArray(), "CONFIG_CHANGED"));
    }

    public IAsyncEnumerable<SlaRecalculationRequest> ReadAllAsync(CancellationToken cancellationToken)
        => channel.Reader.ReadAllAsync(cancellationToken);
}
