using System.Reflection;

var appName = "sshrelay";
var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp(appName, version);
    return;
}

if (args[0] is "-v" or "--version")
{
    Console.WriteLine($"{appName} {version}");
    return;
}

if (args[0].Equals("relay", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("relay command scaffolded: SSH session broker integration is not implemented yet.");
    Console.WriteLine("Use this starter to build session registration, command queuing, and execution relaying.");
    return;
}

Console.Error.WriteLine($"Unknown command: {args[0]}");
Console.Error.WriteLine("Run 'sshrelay --help' to see available commands.");
Environment.ExitCode = 1;

static void PrintHelp(string appName, string version)
{
    Console.WriteLine($"{appName} {version}");
    Console.WriteLine("A starter CLI for relaying commands through an existing SSH session.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine($"  {appName} [command] [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  relay      Placeholder command for future SSH command relaying");
    Console.WriteLine("  --version  Show version");
    Console.WriteLine("  --help     Show help");
}
