using System.CommandLine;

namespace SshRelay.Commands;

internal static class ExecCommand
{
    internal static Command Build()
    {
        var pipeOption = new Option<string>("--pipe", "-p")
        {
            Description = "Named pipe name of the running relay server.",
            DefaultValueFactory = _ => "sshrelay"
        };

        var commandArg = new Argument<string>("command")
        {
            Description = "The command to relay."
        };

        var command = new Command("exec", "Send a command to the relay server and print the response.");
        command.Add(pipeOption);
        command.Add(commandArg);
        command.SetAction(async (parseResult, ct) =>
        {
            var pipeName = parseResult.GetValue(pipeOption)!;
            var cmd = parseResult.GetValue(commandArg)!;

            var client = new RelayClient(pipeName);
            var response = await client.SendCommandAsync(cmd, ct);
            Console.WriteLine(response);
            return 0;
        });

        return command;
    }
}
