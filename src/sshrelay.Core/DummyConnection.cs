namespace SshRelay;

/// <summary>
/// An <see cref="IConnection"/> implementation that returns pre-programmed or
/// echo responses without any real network I/O.  Intended for testing and local
/// development.
/// </summary>
public sealed class DummyConnection : IConnection
{
    private readonly IReadOnlyDictionary<string, string> _responses;

    /// <summary>
    /// Initializes the dummy connection with an optional command→response map.
    /// Commands not present in the map are answered with "echo: {command}".
    /// </summary>
    /// <param name="responses">Optional fixed response map.</param>
    public DummyConnection(IReadOnlyDictionary<string, string>? responses = null)
    {
        _responses = responses ?? new Dictionary<string, string>();
    }

    /// <inheritdoc/>
    public bool IsConnected => true;

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var output = _responses.TryGetValue(command, out var response)
            ? response
            : $"echo: {command}";

        return Task.FromResult(output);
    }
}
