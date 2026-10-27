using System.Diagnostics;

namespace Ash.Tests.Integration.Infrastructure;

/// <summary>Writes stopwatch timings to the console and to timings.log next to the test assembly.</summary>
public static class Timing
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "timings.log");
    private static readonly object Gate = new();

    public static async Task<T> MeasureAsync<T>(string label, Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await action();
        }
        finally
        {
            Log(label, sw.Elapsed);
        }
    }

    public static T Measure<T>(string label, Func<T> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            Log(label, sw.Elapsed);
        }
    }

    public static void Log(string label, TimeSpan elapsed)
    {
        var line = $"[TIMING] {label}: {elapsed.TotalMilliseconds:F0} ms";
        Console.WriteLine(line);
        lock (Gate)
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
    }
}
