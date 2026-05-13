using SshRelay;

var pipeName = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
	? args[0]
	: "sshrelay";

var client = new RelayClient(pipeName);

Console.WriteLine($"Dummy client connected to named pipe '{pipeName}'.");
Console.WriteLine("Type a command and press Enter. Press Ctrl+C or Ctrl+Z then Enter to exit.");

while (true)
{
	var command = Console.ReadLine();
	if (command is null)
	{
		break;
	}

	try
	{
		var response = await client.SendCommandAsync(command);
		Console.WriteLine(response);
	}
	catch (Exception ex)
	{
		Console.WriteLine($"ERROR: {ex.Message}");
	}
}
