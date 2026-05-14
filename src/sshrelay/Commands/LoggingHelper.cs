using Microsoft.Extensions.Logging;

namespace SshRelay.Commands;

internal static class LoggingHelper
{
	internal static ILoggerFactory CreateLoggerFactory(LogLevel minimumLevel)
	{
		return LoggerFactory.Create(b =>
		{
			b.AddConsole();
			b.SetMinimumLevel(minimumLevel);
		});
	}
	
    internal static LogLevel ResolveLogLevel(string? value, bool verbose)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return verbose ? LogLevel.Debug : LogLevel.Information;
        }

        if (Enum.TryParse<LogLevel>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException(
            "Invalid --log-level value. Expected one of: trace, debug, information, warning, error, critical, none.");
    }
}
