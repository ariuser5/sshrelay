using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace SshRelay.Commands;

internal static class ServeCommand
{
    internal static Command Build()
    {
        var pipeOption = new Option<string>("--pipe", "-p")
        {
            Description = "Named pipe name for the relay server to listen on.",
            DefaultValueFactory = _ => "sshrelay"
        };

        var dummyOption = new Option<bool>("--dummy")
        {
            Description = "Use a dummy connection instead of a real SSH session (for testing).",
            DefaultValueFactory = _ => false
        };

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Enable debug logging output.",
            DefaultValueFactory = _ => false
        };

        var logLevelOption = new Option<string?>("--log-level", "-l")
        {
            Description = "Minimum log level: trace, debug, information, warning, error, critical, none.",
            DefaultValueFactory = _ => null
        };

        var identityFileOption = new Option<string?>("--identity-file", "-i")
        {
            Description = "Path to the SSH private key file (same semantics as ssh -i).",
            DefaultValueFactory = _ => null
        };

        var command = new Command("serve", "Start the relay IPC server.");
        command.Add(pipeOption);
        command.Add(dummyOption);
        command.Add(verboseOption);
        command.Add(logLevelOption);
        command.Add(identityFileOption);
        command.SetAction(async (parseResult, ct) =>
        {
            var pipeName = parseResult.GetValue(pipeOption)!;
            var useDummy = parseResult.GetValue(dummyOption);
            var verbose = parseResult.GetValue(verboseOption);
            var logLevelText = parseResult.GetValue(logLevelOption);
            var identityFile = parseResult.GetValue(identityFileOption);

            var minimumLevel = ResolveLogLevel(logLevelText, verbose);
            using var loggerFactory = LoggerFactory.Create(b =>
            {
                b.AddConsole();
                b.SetMinimumLevel(minimumLevel);
            });

            var relayLogger = loggerFactory.CreateLogger<RelayServer>();
            var sshLogger = loggerFactory.CreateLogger<SshConnection>();

            IConnection connection = useDummy
                ? new DummyConnection()
                : new SshConnection("localhost", Environment.UserName, identityFilePath: identityFile, logger: sshLogger);

            Console.WriteLine($"Starting relay server on pipe '{pipeName}' " +
                              $"(connection: {connection.GetType().Name})...");
            Console.WriteLine("Press Ctrl+C to stop.");

            var server = new RelayServer(connection, pipeName, relayLogger);
            await server.RunAsync(ct);
            return 0;
        });

        return command;
    }

    private static LogLevel ResolveLogLevel(string? value, bool verbose)
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
