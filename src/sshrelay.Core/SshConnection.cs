using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshRelay;

/// <summary>
/// <see cref="IConnection"/> backed by the local OpenSSH client.
/// </summary>
public sealed class SshConnection : IConnection
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string? _identityFilePath;
    private readonly ILogger<SshConnection> _logger;
    private volatile bool _isConnected;

    /// <summary>
    /// Creates a new <see cref="SshConnection"/> targeting the given host.
    /// </summary>
    public SshConnection(
        string host,
        string username,
        int port = 22,
        string? identityFilePath = null,
        ILogger<SshConnection>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host must be provided.", nameof(host));

        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username must be provided.", nameof(username));

        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");

        _host = host;
        _username = username;
        _port = port;
        _identityFilePath = identityFilePath;
        _logger = logger ?? NullLogger<SshConnection>.Instance;
    }

    /// <inheritdoc/>
    public bool IsConnected => _isConnected;

    /// <inheritdoc/>
    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command must be provided.", nameof(command));

        if (!string.IsNullOrWhiteSpace(_identityFilePath) && !File.Exists(_identityFilePath))
            throw new FileNotFoundException("SSH identity file was not found.", _identityFilePath);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "ssh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        processStartInfo.ArgumentList.Add("-o");
        processStartInfo.ArgumentList.Add("BatchMode=yes");
        processStartInfo.ArgumentList.Add("-o");
        processStartInfo.ArgumentList.Add("ConnectTimeout=10");
        processStartInfo.ArgumentList.Add("-p");
        processStartInfo.ArgumentList.Add(_port.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(_identityFilePath))
        {
            processStartInfo.ArgumentList.Add("-i");
            processStartInfo.ArgumentList.Add(_identityFilePath);
        }

        processStartInfo.ArgumentList.Add($"{_username}@{_host}");
        processStartInfo.ArgumentList.Add("--");
        processStartInfo.ArgumentList.Add(command);

        _logger.LogDebug("Executing SSH command against {User}@{Host}:{Port}.", _username, _host, _port);

        using var process = new Process { StartInfo = processStartInfo };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Failed to start ssh process.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _isConnected = false;
            _logger.LogError(ex, "Unable to start SSH process.");
            throw new InvalidOperationException(
                "Unable to start ssh process. Ensure OpenSSH client is installed and available on PATH.",
                ex);
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort cancellation; the process may already have exited.
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            _isConnected = false;

            var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            var hint = BuildAuthenticationHint(details);
            _logger.LogWarning(
                "SSH command failed with exit code {ExitCode}. Error: {Error}",
                process.ExitCode,
                details.Trim());
            throw new InvalidOperationException(
                $"SSH command failed with exit code {process.ExitCode}: {details.Trim()}{hint}");
        }

        _isConnected = true;
        _logger.LogDebug("SSH command completed successfully.");
        
        var result = stdout.TrimEnd('\r', '\n');
        _logger.LogTrace("SSH command output: {Output}", result);
        return result;
    }

    private static string BuildAuthenticationHint(string details)
    {
        if (string.IsNullOrWhiteSpace(details))
            return string.Empty;

        if (details.Contains("Permission denied (publickey)", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("passphrase", StringComparison.OrdinalIgnoreCase))
        {
            return " If your key is passphrase-protected, load it into ssh-agent first (for example: 'ssh-add <keyfile>') and retry.";
        }

        return string.Empty;
    }
}
