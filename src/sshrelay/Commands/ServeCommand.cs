using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace SshRelay.Commands;

internal static class ServeCommand
{
    internal static Command Build(
        Option<string> pipeOption,
        Option<string?> hostOption,
        Option<string?> usernameOption,
        Option<int> portOption,
        Option<string?> identityFileOption,
        Option<bool> verboseOption,
        Option<string?> logLevelOption,
		Option<bool> dummyOption)
    {
        var command = new Command("serve", "Start the relay IPC server.");
        command.Add(pipeOption);
        command.Add(hostOption);
        command.Add(usernameOption);
        command.Add(portOption);
        command.Add(identityFileOption);
        command.Add(verboseOption);
        command.Add(logLevelOption);
        command.Add(dummyOption);
        command.SetAction(async (parseResult, ct) =>
        {
            var pipeName = parseResult.GetValue(pipeOption)!;
            var host = parseResult.GetValue(hostOption) ?? "localhost";
            var username = parseResult.GetValue(usernameOption) ?? Environment.UserName;
            var port = parseResult.GetValue(portOption);
            var useDummy = parseResult.GetValue(dummyOption);
            var verbose = parseResult.GetValue(verboseOption);
            var logLevelText = parseResult.GetValue(logLevelOption);
            var identityFile = parseResult.GetValue(identityFileOption);

			var logLevel = LoggingHelper.ResolveLogLevel(logLevelText, verbose);
            using var loggerFactory = LoggingHelper.CreateLoggerFactory(logLevel);
            var relayLogger = loggerFactory.CreateLogger<RelayServer>();
            var sshLogger = loggerFactory.CreateLogger<SshShellConnection>();

            IConnection connection = useDummy
                ? new DummyConnection()
                : new SshShellConnection(host, username, port, identityFile, sshLogger);

            Console.WriteLine(
				$"Starting relay server on pipe '{pipeName}' " +
                $"(connection: {connection.GetType().Name})...");
            Console.WriteLine("Press Ctrl+C to stop.");

            var server = new RelayServer(connection, pipeName, relayLogger);
            await server.RunAsync(ct);

            if (connection is IAsyncDisposable disposable)
                await disposable.DisposeAsync();

            return 0;
        });

        return command;
    }
}
