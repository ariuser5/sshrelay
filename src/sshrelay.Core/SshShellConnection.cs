using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshRelay;

/// <summary>
/// <see cref="IConnection"/> backed by a single long-lived SSH shell session.
/// All commands execute inside the same remote shell, so state such as the working
/// directory persists between consecutive <see cref="ExecuteAsync"/> calls.
/// </summary>
public sealed class SshShellConnection : IConnection, IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _username;
    private readonly string? _identityFilePath;
    private readonly ILogger<SshShellConnection> _logger;

    // Per-instance sentinel that is extremely unlikely to appear in normal command output.
    private readonly string _sentinel = $"__SSHRELAY_{Guid.NewGuid():N}__";

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private volatile bool _isConnected;

    // Separate locks: one guards the one-time connection setup, the other
    // serializes command execution so commands do not interleave.
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _executeLock = new(1, 1);

    public SshShellConnection(
        string host,
        string username,
        int port = 22,
        string? identityFilePath = null,
        ILogger<SshShellConnection>? logger = null)
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
        _logger = logger ?? NullLogger<SshShellConnection>.Instance;
    }

    /// <inheritdoc/>
    public bool IsConnected => _isConnected;

    /// <inheritdoc/>
    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException("Command must be provided.", nameof(command));

        await EnsureConnectedAsync(cancellationToken);

        await _executeLock.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteCoreAsync(command, cancellationToken);
        }
        finally
        {
            _executeLock.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        // Fast path: already connected.
        if (_process is { HasExited: false })
            return;

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring the lock.
            if (_process is { HasExited: false })
                return;

            await StartShellAsync(cancellationToken);
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private async Task StartShellAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_identityFilePath) && !File.Exists(_identityFilePath))
            throw new FileNotFoundException("SSH identity file was not found.", _identityFilePath);

        var psi = new ProcessStartInfo
        {
            FileName = "ssh",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // This relay owns stdin/stdout for the remote shell protocol, so the local ssh
        // client must not consume stdin for password or host-key prompts.
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("BatchMode=yes");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("ConnectTimeout=10");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(_port.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(_identityFilePath))
        {
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(_identityFilePath);
        }

        psi.ArgumentList.Add($"{_username}@{_host}");
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add("-l"); // Login shell: sources ~/.bash_profile so the user's PATH and environment are set up.

        _logger.LogDebug("Starting persistent SSH shell to {User}@{Host}:{Port}.", _username, _host, _port);

        var stderrBuffer = new StringBuilder();
        var process = new Process
        { 
            StartInfo = psi,
            EnableRaisingEvents = true
        };
        
        process.Exited += (_, _) =>
        {
            _isConnected = false;
            _logger.LogDebug("SSH process exited with code {ExitCode}.", process.ExitCode);
        };
        
        // Forward stderr lines in real-time (SSH uses stderr for diagnostics, warnings,
        // host-key prompts, etc.) and also buffer them for error reporting on early exit.
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            _logger.LogWarning("SSH stderr: {Line}", e.Data);
            lock (stderrBuffer) stderrBuffer.AppendLine(e.Data);
        };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start ssh process.");

        process.BeginErrorReadLine();

        _process = process;
        _stdin = process.StandardInput;
        _stdin.NewLine = "\n"; // Remote shell (Linux) expects Unix line endings; Windows StreamWriter defaults to \r\n.
        _stdout = process.StandardOutput;

        // Probe the shell to confirm it is ready. This naturally blocks until SSH
        // authentication completes and drains any banner/MOTD output before we
        // hand control back to the caller.
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await _stdin.WriteLineAsync($"printf '{_sentinel}\\n'".AsMemory(), probeCts.Token);
            await _stdin.FlushAsync(probeCts.Token);

            while (true)
            {
                var line = await _stdout.ReadLineAsync(probeCts.Token);
                if (line is null)
                {
                    await process.WaitForExitAsync(cancellationToken);
                    string stderrOnExit;
                    lock (stderrBuffer) stderrOnExit = stderrBuffer.ToString();
                    throw new InvalidOperationException(
                        $"SSH process exited (exit code {process.ExitCode}) before the shell was ready. " +
                        (stderrOnExit.Length > 0 ? $"Output:{Environment.NewLine}{stderrOnExit.Trim()}" : "No output was captured."));
                }

                if (line.StartsWith(_sentinel, StringComparison.Ordinal))
                    break; // Shell is ready and responsive.
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            string stderrOnTimeout;
            lock (stderrBuffer) stderrOnTimeout = stderrBuffer.ToString();
            throw new TimeoutException(
                "SSH shell did not become ready within 30 seconds. " +
                (stderrOnTimeout.Length > 0 ? $"SSH output:{Environment.NewLine}{stderrOnTimeout.Trim()}" : "No SSH output was captured."));
        }

        _isConnected = true;
    }

    private async Task<string> ExecuteCoreAsync(string command, CancellationToken cancellationToken)
    {
		ArgumentNullException.ThrowIfNull(_stdin);
		ArgumentNullException.ThrowIfNull(_stdout);
		
        _logger.LogDebug("Executing command in SSH shell.");
        _logger.LogTrace("Command: {Command}", command);

        try
        {
            // Run the command in a group (not a subshell) so that side-effects such as
            // `cd` affect the persistent shell's state.  Merging stderr into stdout means
            // error output is included in the returned string, not silently discarded.
            // The sentinel printf is on the SAME line as the command group so that no
            // shell hook (e.g. PROMPT_COMMAND) can run between them and corrupt $?.
            await _stdin.WriteLineAsync(
                $"{{ {command}; }} 2>&1; printf '{_sentinel}%s\\n' \"$?\"".AsMemory(),
                cancellationToken);

            await _stdin.FlushAsync(cancellationToken);
        }
        catch (IOException ex) when (_process is { HasExited: true } p)
        {
            _isConnected = false;
            throw new IOException(
                $"SSH shell process exited (code {p.ExitCode}) before the command could be sent. " +
                "Check that SSH credentials are available (e.g. via ssh-agent) and that the host is reachable.",
                ex);
        }
        
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var output = new StringBuilder();
        while (true)
        {
            ArgumentNullException.ThrowIfNull(_stdout);
            string? line;
            try
            {
                line = await _stdout.ReadLineAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out waiting for command output. The SSH shell may be unresponsive.");
            }
            catch (IOException ex) when (_process is { HasExited: true } p)
            {
                _isConnected = false;
                throw new IOException(
                    $"SSH shell process exited (code {p.ExitCode}) while waiting for command output. " +
                    "Check that SSH credentials are available (e.g. via ssh-agent) and that the host is reachable.",
                    ex);
            }

            if (line is null)
            {
                _isConnected = false;
                throw new IOException("SSH shell connection closed unexpectedly.");
            }

            if (line.StartsWith(_sentinel, StringComparison.Ordinal))
            {
                var exitCode = int.TryParse(line[_sentinel.Length..], out var code) ? code : -1;
                if (exitCode != 0)
                {
                    var errorOutput = output.ToString().TrimEnd();
                    _logger.LogWarning("Command exited with code {ExitCode}. Output: {Output}", exitCode, errorOutput);
                    throw new InvalidOperationException($"Command exited with code {exitCode}: {errorOutput}");
                }
                break;
            }

            if (output.Length > 0) output.AppendLine();
            output.Append(line);
        }

        var result = output.ToString();
        _logger.LogTrace("Command output: {Output}", result);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        _isConnected = false;

        if (_stdin is not null)
        {
            try
            {
                await _stdin.WriteLineAsync("exit");
                await _stdin.FlushAsync();
            }
            catch { /* best-effort */ }

            await _stdin.DisposeAsync();
        }

        if (_process is not null)
        {
            try
            {
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch { /* timed out or already exited */ }

            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); } catch { }
            }

            _process.Dispose();
        }

        _connectLock.Dispose();
        _executeLock.Dispose();
    }
}
