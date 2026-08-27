using Microsoft.Extensions.Logging;

namespace AqiClock.App.Services;

internal static partial class StartupStepRunner
{
    public static bool TryRun(string stepName, Action action, ILogger logger)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception exception)
        {
            LogStartupStepFailed(logger, stepName, exception);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Startup step {StepName} failed; continuing in degraded mode")]
    private static partial void LogStartupStepFailed(ILogger logger, string stepName, Exception exception);
}
