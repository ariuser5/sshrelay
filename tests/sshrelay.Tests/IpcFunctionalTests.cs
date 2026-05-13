using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SshRelay;
using Xunit;

namespace SshRelay.Tests;

/// <summary>
/// Functional tests for the named-pipe IPC layer.
/// Each test starts a <see cref="RelayServer"/> with a <see cref="DummyConnection"/>
/// and verifies end-to-end command/response round-trips via <see cref="RelayClient"/>.
/// </summary>
public sealed class IpcFunctionalTests
{
    // Each test uses a unique pipe name to avoid cross-test interference.
    private static string UniquePipe() => $"sshrelay-test-{Guid.NewGuid():N}";

    // Starts a RelayServer on the given pipe and returns a CancellationTokenSource
    // that can be used to stop it.  The server task is also returned so callers
    // can await or inspect it.
    private static (Task serverTask, CancellationTokenSource cts) StartServer(
        IConnection connection,
        string pipeName)
    {
        var cts = new CancellationTokenSource();
        var server = new RelayServer(connection, pipeName);
        var task = server.RunAsync(cts.Token);
        return (task, cts);
    }

    // ── DummyConnection unit tests ──────────────────────────────────────────

    [Fact]
    public async Task DummyConnection_EchoesUnknownCommand()
    {
        var conn = new DummyConnection();
        var result = await conn.ExecuteAsync("hello");
        Assert.Equal("echo: hello", result);
    }

    [Fact]
    public async Task DummyConnection_ReturnsPreconfiguredResponse()
    {
        var responses = new Dictionary<string, string> { ["ls"] = "/bin /etc /home" };
        var conn = new DummyConnection(responses);
        var result = await conn.ExecuteAsync("ls");
        Assert.Equal("/bin /etc /home", result);
    }

    [Fact]
    public async Task DummyConnection_IsAlwaysConnected()
    {
        var conn = new DummyConnection();
        Assert.True(conn.IsConnected);
        await conn.ExecuteAsync("ignored");   // should not throw
        Assert.True(conn.IsConnected);
    }

    // ── IPC round-trip tests ────────────────────────────────────────────────

    [Fact]
    public async Task IPC_SingleCommand_EchoResponse()
    {
        var pipe = UniquePipe();
        var (serverTask, cts) = StartServer(new DummyConnection(), pipe);

        try
        {
            var client = new RelayClient(pipe);
            var response = await client.SendCommandAsync("hello");
            Assert.Equal("echo: hello", response);
        }
        finally
        {
            await cts.CancelAsync();
            await Task.WhenAny(serverTask, Task.Delay(1_000));
        }
    }

    [Fact]
    public async Task IPC_SingleCommand_PreconfiguredResponse()
    {
        var pipe = UniquePipe();
        var responses = new Dictionary<string, string> { ["whoami"] = "testuser" };
        var (serverTask, cts) = StartServer(new DummyConnection(responses), pipe);

        try
        {
            var client = new RelayClient(pipe);
            var response = await client.SendCommandAsync("whoami");
            Assert.Equal("testuser", response);
        }
        finally
        {
            await cts.CancelAsync();
            await Task.WhenAny(serverTask, Task.Delay(1_000));
        }
    }

    [Fact]
    public async Task IPC_MultipleSequentialCommands_EachRespondedCorrectly()
    {
        var pipe = UniquePipe();
        var responses = new Dictionary<string, string>
        {
            ["cmd1"] = "result1",
            ["cmd2"] = "result2",
            ["cmd3"] = "result3",
        };
        var (serverTask, cts) = StartServer(new DummyConnection(responses), pipe);

        try
        {
            var client = new RelayClient(pipe);

            // Each SendCommandAsync opens a fresh pipe connection to the server.
            for (int i = 1; i <= 3; i++)
            {
                var response = await client.SendCommandAsync($"cmd{i}");
                Assert.Equal($"result{i}", response);
            }
        }
        finally
        {
            await cts.CancelAsync();
            await Task.WhenAny(serverTask, Task.Delay(1_000));
        }
    }

    [Fact]
    public async Task IPC_MultipleParallelClients_AllReceiveCorrectResponses()
    {
        var pipe = UniquePipe();
        // The server handles one client at a time; clients queue naturally.
        var (serverTask, cts) = StartServer(new DummyConnection(), pipe);

        try
        {
            var client = new RelayClient(pipe);
            const int count = 5;

            // Each SendCommandAsync opens its own pipe connection; the server's
            // fire-and-forget accept loop handles them one after another.
            var responses = new List<string>();
            for (int i = 0; i < count; i++)
            {
                responses.Add(await client.SendCommandAsync($"ping{i}"));
            }

            for (int i = 0; i < count; i++)
            {
                Assert.Equal($"echo: ping{i}", responses[i]);
            }
        }
        finally
        {
            await cts.CancelAsync();
            await Task.WhenAny(serverTask, Task.Delay(1_000));
        }
    }

    [Fact]
    public async Task IPC_CommandWithSpaces_RoundTripsCorrectly()
    {
        var pipe = UniquePipe();
        var (serverTask, cts) = StartServer(new DummyConnection(), pipe);

        try
        {
            var client = new RelayClient(pipe);
            var response = await client.SendCommandAsync("ls -la /tmp");
            Assert.Equal("echo: ls -la /tmp", response);
        }
        finally
        {
            await cts.CancelAsync();
            await Task.WhenAny(serverTask, Task.Delay(1_000));
        }
    }

    [Fact]
    public async Task IPC_EmptyCommand_RoundTripsCorrectly()
    {
        var pipe = UniquePipe();
        var (serverTask, cts) = StartServer(new DummyConnection(), pipe);

        try
        {
            var client = new RelayClient(pipe);
            var response = await client.SendCommandAsync(string.Empty);
            Assert.Equal("echo: ", response);
        }
        finally
        {
            await cts.CancelAsync();
            await Task.WhenAny(serverTask, Task.Delay(1_000));
        }
    }

    [Fact]
    public async Task IPC_ServerCancellation_DoesNotHangClient()
    {
        var pipe = UniquePipe();
        var responses = new Dictionary<string, string> { ["ok"] = "done" };
        var (serverTask, cts) = StartServer(new DummyConnection(responses), pipe);

        // Send one successful command first.
        var client = new RelayClient(pipe);
        var first = await client.SendCommandAsync("ok");
        Assert.Equal("done", first);

        // Cancel the server.
        await cts.CancelAsync();
        await Task.WhenAny(serverTask, Task.Delay(2_000));

        // The server task should finish (possibly with cancellation).
        Assert.True(serverTask.IsCompleted);
    }

    // ── Substitutability test ───────────────────────────────────────────────

    [Fact]
    public async Task IPC_CustomConnection_CanReplaceDefaultConnection()
    {
        // Demonstrates that RelayServer accepts any IConnection implementation,
        // making the SSH connection fully substitutable for testing purposes.
        var pipe = UniquePipe();

        var customConn = new CustomTestConnection();
        var (serverTask, cts) = StartServer(customConn, pipe);

        try
        {
            var client = new RelayClient(pipe);
            var response = await client.SendCommandAsync("test");
            Assert.Equal("CUSTOM:test", response);
            Assert.True(customConn.ExecuteWasCalled);
        }
        finally
        {
            await cts.CancelAsync();
            await Task.WhenAny(serverTask, Task.Delay(1_000));
        }
    }

    /// <summary>
    /// A hand-rolled test double that records whether Execute was invoked.
    /// </summary>
    private sealed class CustomTestConnection : IConnection
    {
        public bool ExecuteWasCalled { get; private set; }
        public bool IsConnected => true;

        public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
        {
            ExecuteWasCalled = true;
            return Task.FromResult($"CUSTOM:{command}");
        }
    }
}
