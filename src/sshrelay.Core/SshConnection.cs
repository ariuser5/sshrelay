namespace SshRelay;

/// <summary>
/// Placeholder <see cref="IConnection"/> backed by a real SSH session.
/// The actual SSH transport is not yet implemented; this class exists to
/// show where the real implementation will live and to satisfy the interface
/// contract so that the rest of the codebase compiles.
/// </summary>
public sealed class SshConnection : IConnection
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;

    /// <summary>
    /// Creates a new <see cref="SshConnection"/> targeting the given host.
    /// </summary>
    public SshConnection(string host, string username, int port = 22)
    {
        _host = host;
        _username = username;
        _port = port;
    }

    /// <inheritdoc/>
    public bool IsConnected => false; // not yet implemented

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            $"Real SSH transport to {_username}@{_host}:{_port} is not yet implemented. " +
            "Use DummyConnection for testing.");
    }
}
