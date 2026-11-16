using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>
/// Task 051, item 9: every log event of every API host the integration suite starts (AshApiFactory registers it), as
/// text — the message and every property value — so a test can scan the whole run for secrets. Bounded: the oldest
/// lines go first beyond <see cref="MaxLines"/>.
/// </summary>
public sealed class SuiteLogSink : ILogEventSink
{
    public const int MaxLines = 200_000;
    public static readonly SuiteLogSink Instance = new();

    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public void Emit(LogEvent logEvent)
    {
        var text = string.Join(" | ",
            new[] { logEvent.RenderMessage(), logEvent.Exception?.ToString() ?? string.Empty }
                .Concat(logEvent.Properties.Select(p => $"{p.Key}={p.Value}")));
        _lines.Enqueue(text);
        while (_lines.Count > MaxLines && _lines.TryDequeue(out _))
        {
        }
    }
}
