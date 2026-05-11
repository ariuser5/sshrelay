using System.CommandLine;
using SshRelay;

var rootCommand = new RootCommand("A CLI for relaying commands through an existing SSH session.");

var builtInVersionOption = rootCommand.Options.OfType<VersionOption>().FirstOrDefault();
if (builtInVersionOption is not null)
{
    builtInVersionOption.Aliases.Add("-v");
}

// ── serve ──────────────────────────────────────────────────────────────────
// Starts the relay IPC server.  --dummy uses DummyConnection; in the future
// --ssh will use a real SSH session.

var servePipeOption = new Option<string>("--pipe", "Named pipe name to listen on.");
servePipeOption.DefaultValueFactory = _ => "sshrelay";

var serveDummyOption = new Option<bool>("--dummy", "Use a dummy (echo) connection instead of a real SSH session.");
serveDummyOption.DefaultValueFactory = _ => false;

var serveCommand = new Command("serve", "Start the relay IPC server.");
serveCommand.Add(servePipeOption);
serveCommand.Add(serveDummyOption);
serveCommand.SetAction(async (parseResult, ct) =>
{
    var pipeName = parseResult.GetValue(servePipeOption)!;
    var useDummy = parseResult.GetValue(serveDummyOption);

    IConnection connection = useDummy
        ? new DummyConnection()
        : new SshConnection("localhost", Environment.UserName);

    Console.WriteLine($"Starting relay server on pipe '{pipeName}' " +
                      $"(connection: {connection.GetType().Name})...");
    Console.WriteLine("Press Ctrl+C to stop.");

    var server = new RelayServer(connection, pipeName);
    await server.RunAsync(ct);
    return 0;
});

rootCommand.Add(serveCommand);

// ── exec ───────────────────────────────────────────────────────────────────
// Sends a single command to the relay server and prints the response.

var execPipeOption = new Option<string>("--pipe", "Named pipe name of the running relay server.");
execPipeOption.DefaultValueFactory = _ => "sshrelay";

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
