using System.IO.Pipes;

namespace SshRelay;

/// <summary>
/// IPC client that connects to a <see cref="RelayServer"/> over a named pipe
/// and sends commands for remote execution.
/// </summary>
public sealed class RelayClient
{
    private readonly string _pipeName;
    private readonly int _connectTimeoutMs;

    /// <summary>
    /// Creates a <see cref="RelayClient"/> targeting the named pipe
    /// <paramref name="pipeName"/> on the local machine.
    /// </summary>
    /// <param name="pipeName">Name of the local named pipe (default: <c>sshrelay</c>).</param>
    /// <param name="connectTimeoutMs">
    /// Milliseconds to wait when connecting to the pipe server (default: 5000).
    /// </param>
    public RelayClient(string pipeName = "sshrelay", int connectTimeoutMs = 5_000)
    {
        _pipeName = pipeName;
        _connectTimeoutMs = connectTimeoutMs;
    }

    /// <summary>
    /// Sends <paramref name="command"/> to the relay server and returns the response.
    /// Each call opens a fresh pipe connection.
    /// </summary>
    /// <param name="command">The command to relay.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The response string from the server.</returns>
    public async Task<string> SendCommandAsync(
        string command,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_connectTimeoutMs);

        await using var clientStream = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await clientStream.ConnectAsync(cts.Token);

        var writer = new StreamWriter(clientStream, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(clientStream, leaveOpen: true);

        await writer.WriteLineAsync(command.AsMemory(), cts.Token);
        var response = await reader.ReadLineAsync(cts.Token);

        // The server disconnects the pipe after it sends its response.
        // Suppress the resulting IOException that occurs when the writer
        // tries to flush its internal buffer on disposal.
        try { await writer.DisposeAsync(); } catch (IOException) { }

        return response ?? string.Empty;
    }
}
