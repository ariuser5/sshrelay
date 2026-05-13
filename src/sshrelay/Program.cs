using System.CommandLine;
using Microsoft.Extensions.Logging;
using SshRelay;

var rootCommand = new RootCommand("A CLI for relaying commands through an existing SSH session.");

var builtInVersionOption = rootCommand.Options.OfType<VersionOption>().FirstOrDefault();
builtInVersionOption?.Aliases.Add("-v");

// ── serve ──────────────────────────────────────────────────────────────────
// Starts the relay IPC server.  --dummy uses DummyConnection; in the future
// --ssh will use a real SSH session.

var servePipeOption = new Option<string>("--pipe", "-p")
{
	Description = "Named pipe name for the relay server to listen on.",
	DefaultValueFactory = _ => "sshrelay"
};

var serveDummyOption = new Option<bool>("--dummy")
{
	Description = "Use a dummy connection instead of a real SSH session (for testing).",
	DefaultValueFactory = _ => false
};

var serveVerboseOption = new Option<bool>("--verbose")
{
    Description = "Enable debug logging output.",
    DefaultValueFactory = _ => false
};

var serveIdentityFileOption = new Option<string?>("--identity-file", "-i")
{
    Description = "Path to the SSH private key file (same semantics as ssh -i).",
    DefaultValueFactory = _ => null
};

var serveCommand = new Command("serve", "Start the relay IPC server.");
serveCommand.Add(servePipeOption);
serveCommand.Add(serveDummyOption);
serveCommand.Add(serveVerboseOption);
serveCommand.Add(serveIdentityFileOption);
serveCommand.SetAction(async (parseResult, ct) =>
{
    var pipeName = parseResult.GetValue(servePipeOption)!;
    var useDummy = parseResult.GetValue(serveDummyOption);
    var verbose = parseResult.GetValue(serveVerboseOption);
    var identityFile = parseResult.GetValue(serveIdentityFileOption);
    
    IConnection connection = useDummy
        ? new DummyConnection()
        : new SshConnection("localhost", Environment.UserName, identityFilePath: identityFile);

    Console.WriteLine($"Starting relay server on pipe '{pipeName}' " +
                      $"(connection: {connection.GetType().Name})...");
    Console.WriteLine("Press Ctrl+C to stop.");

    var minimumLevel = verbose ? LogLevel.Debug : LogLevel.Information;
    using var loggerFactory = LoggerFactory.Create(b =>
    {
        b.AddConsole();
        b.SetMinimumLevel(minimumLevel);
    });
    var logger = loggerFactory.CreateLogger<RelayServer>();
    var server = new RelayServer(connection, pipeName, logger);
    await server.RunAsync(ct);
    return 0;
});

rootCommand.Add(serveCommand);

// ── exec ───────────────────────────────────────────────────────────────────
// Sends a single command to the relay server and prints the response.

var execPipeOption = new Option<string>("--pipe", "-p")
{
    Description = "Named pipe name of the running relay server.",
    DefaultValueFactory = _ => "sshrelay"
};

var execCommandArg = new Argument<string>("command");
execCommandArg.Description = "The command to relay.";

var execCommand = new Command("exec", "Send a command to the relay server and print the response.");
execCommand.Add(execPipeOption);
execCommand.Add(execCommandArg);
execCommand.SetAction(async (parseResult, ct) =>
{
    var pipeName = parseResult.GetValue(execPipeOption)!;
    var cmd = parseResult.GetValue(execCommandArg)!;

    var client = new RelayClient(pipeName);
    var response = await client.SendCommandAsync(cmd, ct);
    Console.WriteLine(response);
    return 0;
});

rootCommand.Add(execCommand);

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
