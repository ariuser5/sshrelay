# sshrelay

`sshrelay` is a C#/.NET CLI scaffold for a future tool that relays commands through an already-established SSH session.

## What problem it solves

In some environments, one long-lived process already owns a trusted SSH connection (for example, a daemon, agent, or worker). Another process still needs to run commands remotely, but should not re-authenticate or create duplicate SSH sessions.

`sshrelay` is intended to provide a clean, auditable handoff where:

- a **session owner** process establishes and maintains SSH connectivity, and
- a **client** process submits commands to be executed through that existing session.

## Basic architecture idea

High-level design target:

1. **SSH Session Host**
   - Maintains one or more authenticated SSH sessions.
   - Exposes a local IPC endpoint (e.g., Unix domain socket / named pipe).
2. **Relay Protocol**
   - Defines command request/response payloads, correlation IDs, and error handling.
   - Supports command execution options (timeouts, env vars, working dir, etc.).
3. **CLI / Client**
   - Sends relay requests to the host.
   - Prints streamed output and structured exit results.

Current repository scaffold:

- `sshrelay.sln` — solution entry point
- `src/sshrelay/` — minimal .NET 8 CLI starter app

## Current status / roadmap

This is **initial scaffolding only**. SSH transport and relay features are not implemented yet.

Planned next steps:

- [ ] Define session registry and lifecycle model
- [ ] Add local authenticated IPC channel
- [ ] Implement request/response protocol
- [ ] Add command execution relay path
- [ ] Add integration tests for session reuse and failure handling
- [ ] Harden audit logging and security boundaries

## Build and run

### Prerequisites

- .NET 8 SDK (LTS)

### Build

```bash
dotnet build sshrelay.sln
```

### Run

```bash
dotnet run --project src/sshrelay/sshrelay.csproj -- --help
```

Try the placeholder command:

```bash
dotnet run --project src/sshrelay/sshrelay.csproj -- relay
```

## Security considerations (important)

SSH command relaying introduces high-impact trust boundaries. Before implementing runtime features, ensure:

- **Strong local authentication/authorization** for any relay client.
- **Least privilege** for the process that owns SSH credentials/sessions.
- **Input validation and command policy controls** (allow-lists, argument constraints where possible).
- **Auditability** (who requested what command, when, and with what result).
- **Session isolation** between tenants/users/workloads.
- **Safe handling of secrets** in command payloads, logs, and crash output.

Until these controls are implemented, this project should be treated as a development scaffold.

## License

MIT (see `LICENSE`).
