using System.CommandLine;

var rootCommand = new RootCommand("A starter CLI for relaying commands through an existing SSH session.");

var builtInVersionOption = rootCommand.Options.OfType<VersionOption>().FirstOrDefault();
if (builtInVersionOption is not null)
{
    builtInVersionOption.Aliases.Add("-v");
}

var relayCommand = new Command("relay", "Placeholder command for future SSH command relaying");
relayCommand.SetAction(_ =>
{
    Console.WriteLine("relay command scaffolded: SSH session broker integration is not implemented yet.");
    Console.WriteLine("Use this starter to build session registration, command queuing, and execution relaying.");
    return 0;
});

rootCommand.Add(relayCommand);

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
