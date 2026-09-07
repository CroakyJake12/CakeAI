using System.Text.Json;

namespace HavenOS.Apps.Motion;

internal sealed record MotionCapabilityStatus(
    string Route,
    bool EngineAvailable,
    bool TimelineAvailable,
    bool RenderAvailable,
    bool ExportAvailable,
    bool PersistenceAvailable,
    string Message);

internal static class MotionSurface
{
    public const string Route = "motion";

    public static MotionCapabilityStatus GetStatus() =>
        new(
            Route,
            EngineAvailable: false,
            TimelineAvailable: false,
            RenderAvailable: false,
            ExportAvailable: false,
            PersistenceAvailable: false,
            Message: "Motion engine is not available in this build.");

    public static int SelfTest()
    {
        var status = GetStatus();

        if (!string.Equals(status.Route, Route, StringComparison.Ordinal))
            return 10;

        if (status.EngineAvailable
            || status.TimelineAvailable
            || status.RenderAvailable
            || status.ExportAvailable
            || status.PersistenceAvailable)
        {
            return 11;
        }

        return 0;
    }
}

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--self-test", StringComparison.Ordinal))
            return MotionSurface.SelfTest();

        if (args.Length == 0 || (args.Length == 1 && string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine(JsonSerializer.Serialize(MotionSurface.GetStatus()));
            return 0;
        }

        Console.Error.WriteLine(
            "Motion currently exposes status only; editing, timeline, rendering, export, and persistence are unavailable.");
        return 2;
    }
}
