using System.CommandLine;
using SshRelay.Commands;

var rootCommand = new RootCommand("A CLI for relaying commands through an existing SSH session.");

var builtInVersionOption = rootCommand.Options.OfType<VersionOption>().FirstOrDefault();
builtInVersionOption?.Aliases.Add("-v");

// ── shared options ────────────────────────────────────────────────────────────
var pipeOption = new Option<string>("--pipe", "-p")
{
	Description = "Named pipe name used by the relay server / client.",
	DefaultValueFactory = _ => "sshrelay"
};

var hostOption = new Option<string?>("--host", "-H")
{
	Description = "SSH host to connect to.",
};

var usernameOption = new Option<string?>("--username", "-u")
{
	Description = "SSH username (defaults to the current OS user when not specified).",
};

var portOption = new Option<int>("--port")
{
	Description = "SSH port.",
	DefaultValueFactory = _ => 22
};

var identityFileOption = new Option<string?>("--identity-file", "-i")
{
	Description = "Path to the SSH private key file (same semantics as ssh -i).",
};

var verboseOption = new Option<bool>("--verbose")
{
	Description = "Enable debug-level logging output.",
	DefaultValueFactory = _ => false
};

var logLevelOption = new Option<string?>("--log-level", "-l")
{
	Description = "Minimum log level: trace, debug, information, warning, error, critical, none.",
};

var dummyOption = new Option<bool>("--dummy")
{
    Description = "Use a dummy connection instead of a real SSH session (for testing).",
    DefaultValueFactory = _ => false
};

rootCommand.Add(ServeCommand.Build(pipeOption, hostOption, usernameOption, portOption, identityFileOption, verboseOption, logLevelOption, dummyOption));
rootCommand.Add(ExecCommand.Build(pipeOption, verboseOption, logLevelOption));

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
