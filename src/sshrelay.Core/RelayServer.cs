using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshRelay;

/// <summary>
/// IPC server that accepts named-pipe connections from <see cref="RelayClient"/>
/// instances and forwards every command to the underlying <see cref="IConnection"/>.
/// </summary>
/// <remarks>
/// Protocol (line-delimited UTF-8):
/// <list type="bullet">
///   <item>Client sends: one command per line.</item>
///   <item>Server replies: one response line per command.</item>
///   <item>Client closes the connection to end the session.</item>
/// </list>
/// </remarks>
public sealed class RelayServer
{
    private readonly IConnection _connection;
    private readonly string _pipeName;
    private readonly ILogger<RelayServer> _logger;

    /// <summary>
    /// Creates a <see cref="RelayServer"/> that uses <paramref name="connection"/>
    /// to execute commands received over the named pipe <paramref name="pipeName"/>.
    /// </summary>
    /// <param name="connection">The backend connection to forward commands to.</param>
    /// <param name="pipeName">Name of the local named pipe (default: <c>sshrelay</c>).</param>
    public RelayServer(IConnection connection, string pipeName = "sshrelay", ILogger<RelayServer>? logger = null)
    {
        _connection = connection;
        _pipeName = pipeName;
        _logger = logger ?? NullLogger<RelayServer>.Instance;
    }

    /// <summary>
    /// Runs the server loop, accepting one client at a time, until
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {

        _logger.LogDebug("Probing connection before accepting clients...");
        var probe = await _connection.ExecuteAsync("echo sshrelay-probe", cancellationToken);
        _logger.LogDebug("Connection probe succeeded: {ProbeResult}", probe.Trim());
        _logger.LogInformation("Relay server listening on pipe '{PipeName}'.", _pipeName);

        while (!cancellationToken.IsCancellationRequested)
        {
            var serverStream = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await serverStream.WaitForConnectionAsync(cancellationToken);
                _logger.LogDebug("Incomming client connection.");
            }
            catch (OperationCanceledException)
            {
                await serverStream.DisposeAsync();
                _logger.LogDebug("Relay server stopping.");
                break;
            }

            // Fire-and-forget so the accept loop immediately becomes available
            // for the next client rather than waiting for this one to finish.
            _ = HandleClientAsync(serverStream, cancellationToken);
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream serverStream, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Begin handling client command.");
        
        await using (serverStream)
        {
            using var reader = new StreamReader(serverStream, leaveOpen: true);
            await using var writer = new StreamWriter(serverStream, leaveOpen: true) { AutoFlush = true };

            while (!cancellationToken.IsCancellationRequested)
            {
                string? command;
                try {
                    command = await reader.ReadLineAsync(cancellationToken);
                }
                catch (OperationCanceledException) { 
                    _logger.LogDebug("Cancellation requested while reading command from client.");
                    break; 
                }
                catch (IOException){ 
                    _logger.LogDebug("Client disconnected while reading command.");
                    break;
                }

                if (command is null)
                {
                    // End-of-stream: client closed its write side.
                    break;
                }
                
                _logger.LogDebug("Received command from client.");
                _logger.LogTrace("Received command: '{Command}'.", command);

                string result;
                try {
                    result = await _connection.ExecuteAsync(command, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Command execution failed for '{Command}'.", command);
                    result = $"ERROR: {ex.Message}";
                }

                try
                {
                    _logger.LogTrace("Forwarding command result: '{Result}'.", result);
                    
                    // Base64-encode so the entire response fits on one wire line regardless of content.
                    await writer.WriteLineAsync(
                        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(result)).AsMemory(),
                        cancellationToken);
                }
                catch (OperationCanceledException) {
                    _logger.LogDebug("Cancellation requested while sending response to client.");
                    break;
                }
                catch (IOException) {
                    _logger.LogDebug("Client disconnected before receiving response.");
                    break;
                }
            }

            if (serverStream.IsConnected)
                serverStream.Disconnect();

            _logger.LogDebug("End handling client command.");
        }
    }
}
