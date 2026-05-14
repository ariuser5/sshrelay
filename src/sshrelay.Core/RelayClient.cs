using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshRelay;

/// <summary>
/// IPC client that connects to a <see cref="RelayServer"/> over a named pipe
/// and sends commands for remote execution.
/// </summary>
public sealed class RelayClient
{
    private readonly string _pipeName;
    private readonly ILogger<RelayClient> _logger;

    /// <summary>
    /// Creates a <see cref="RelayClient"/> targeting the named pipe
    /// <paramref name="pipeName"/> on the local machine.
    /// </summary>
    /// <param name="pipeName">Name of the local named pipe (default: <c>sshrelay</c>).</param>
    /// <param name="connectTimeoutMs">
    /// Milliseconds to wait when connecting to the pipe server (default: 5000).
    /// </param>
    /// <param name="logger">Optional logger; falls back to a no-op logger when not provided.</param>
    public RelayClient(string pipeName = "sshrelay", ILogger<RelayClient>? logger = null)
    {
        _pipeName = pipeName;
        _logger = logger ?? NullLogger<RelayClient>.Instance;
    }

    /// <summary>
    /// Milliseconds to wait when connecting to the pipe server (default: 5000).
    /// </summary>    
    public int ConnectTimeoutMs { get; set; } = 5_000;
    
    /// <summary>
    /// Milliseconds to wait for a response from the server after sending a command (default: 10000).
    /// </summary>
    public int CommandTimeoutMs { get; set; } = 10_000;

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
        _logger.LogDebug("Connecting to pipe '{PipeName}'.", _pipeName);

        await using var clientStream = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        
        await ConnectWithTimeoutAsync(clientStream, ConnectTimeoutMs, cancellationToken);

        if (!clientStream.IsConnected)
            throw new IOException($"Failed to connect to the relay server on pipe '{_pipeName}'.");
            
        _logger.LogDebug("Sending command...");
        _logger.LogTrace("Sending raw command: {Command}", command);

        string response = await StreamCommand(clientStream, command, CommandTimeoutMs, cancellationToken);
        
        _logger.LogDebug("Received response.");
        _logger.LogTrace("Raw response: {Response}", response);
        
        return response;
    }
    
    private static async Task ConnectWithTimeoutAsync(
        NamedPipeClientStream clientStream,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);
        try 
        {
            await clientStream.ConnectAsync(cts.Token);
            
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Failed to connect to the relay server within {timeoutMs}ms.");
        }
    }
    
    private static async Task<string> StreamCommand(
        Stream clientStream,
        string command,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        var writer = new StreamWriter(clientStream, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(clientStream, leaveOpen: true);
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);
        string response;
        try
        {
            await writer.WriteLineAsync(command.AsMemory(), cts.Token);
            var encoded = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
            response = encoded.Length == 0
                ? string.Empty
                : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("No response received from the relay server within the allotted time.");
        }
        finally
        {
            // The server disconnects the pipe after it sends its response.
            // Suppress the resulting IOException that occurs when the writer
            // tries to flush its internal buffer on disposal.
            try { await writer.DisposeAsync(); } catch (IOException) { }
        }

        return response;
    }
}
