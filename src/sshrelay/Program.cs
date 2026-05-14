using System.CommandLine;
using SshRelay.Commands;

var rootCommand = new RootCommand("A CLI for relaying commands through an existing SSH session.");

var builtInVersionOption = rootCommand.Options.OfType<VersionOption>().FirstOrDefault();
builtInVersionOption?.Aliases.Add("-v");

rootCommand.Add(ServeCommand.Build());
rootCommand.Add(ExecCommand.Build());

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
