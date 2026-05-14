using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace SshRelay.Commands;

internal static class ExecCommand
{
    internal static Command Build(
        Option<string> pipeOption,
        Option<bool> verboseOption,
        Option<string?> logLevelOption)
    {
        var commandArg = new Argument<string>("command")
        {
            Description = "The command to execute on the relay server."
        };

        var command = new Command(
            "exec",
            "Send a command to a running relay server via the named pipe.");
			
        command.Add(pipeOption);
        command.Add(verboseOption);
        command.Add(logLevelOption);
        command.Add(commandArg);
        command.SetAction(async (parseResult, ct) =>
        {
            var pipeName = parseResult.GetValue(pipeOption)!;
            var verbose = parseResult.GetValue(verboseOption);
            var logLevelText = parseResult.GetValue(logLevelOption);
            var cmd = parseResult.GetValue(commandArg)!;

			var logLevel = LoggingHelper.ResolveLogLevel(logLevelText, verbose);
            using var loggerFactory = LoggingHelper.CreateLoggerFactory(logLevel);
            var clientLogger = loggerFactory.CreateLogger<RelayClient>();
            var client = new RelayClient(pipeName, logger: clientLogger)
			{
				ConnectTimeoutMs = 5_000,
				CommandTimeoutMs = 10_000
			};
			
			string response = await client.SendCommandAsync(cmd, ct);
            Console.WriteLine(response);
            return 0;
        });

        return command;
    }
}
