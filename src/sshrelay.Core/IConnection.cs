namespace SshRelay;

/// <summary>
/// Represents an abstract connection that can execute remote commands.
/// Implement this interface to provide a real SSH backend or a test double.
/// </summary>
public interface IConnection
{
    /// <summary>Gets a value indicating whether the connection is currently active.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Executes a command on the remote side and returns the output.
    /// </summary>
    /// <param name="command">The command string to execute.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The stdout output produced by the command.</returns>
    Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default);
}
