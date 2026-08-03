---
id: instance-routing
slug: /architecture/instance-routing
title: Unity Instance Routing
sidebar_label: Instance Routing
description: How a tool call finds the right Unity Editor, how discovery differs between stdio and HTTP, and what the server does when it cannot tell which Editor you meant.
---

# Unity Instance Routing

With one Unity Editor open, routing is invisible. With two, every tool call has to answer a question
first: which Editor? Answering it wrong is not a cosmetic problem, because the wrong answer means an
agent working on project A writes a script into project B. That is what [#1023](https://github.com/CoplayDev/unity-mcp/issues/1023)
reported.

This page covers how the answer is reached, how the two transports differ underneath, and which parts
are still wrong. For task-level instructions, see the [Multi-Instance Routing guide](/guides/multi-instance).

## What identifies an Editor

An instance identifier looks like `MyGame@a1b2c3d4`. The name comes from the project folder, and the
hash is a SHA1 of `Application.dataPath` (the absolute path to `Assets`), hex-encoded and truncated.

Two things about that are worth knowing before you rely on it:

The hash is a function of the path, so it is not portable. Moving the project, renaming its folder,
or checking it out on another machine produces a different hash, which invalidates any pin that
referenced the old one.

The truncation length differs by transport. The stdio path truncates to 8 characters
(`StdioBridgeHost.ComputeProjectHash`), the HTTP path to 16 (`ProjectIdentityUtility.ComputeProjectHash`).
The same project therefore advertises `MyGame@a1b2c3d4` over stdio and `MyGame@a1b2c3d4e5f6a7b8` over
HTTP. Anything that compares identifiers across transports, or caches one and replays it under the
other, will not match.

## How the server finds Editors

The two transports discover instances by completely different means, and most of the behavioural
differences below follow from this one split.

### stdio: filesystem scan plus TCP probe

There is no registration step. Unity writes a status file and the server polls for it.

Each Editor writes `unity-mcp-status-<hash8>.json` into `~/.unity-mcp` (or `$UNITY_MCP_STATUS_DIR`),
refreshed roughly every half second from `EditorApplication.update`. The file carries the listening
port, a `reloading` flag, a heartbeat timestamp and some project metadata.

Discovery globs that directory, then opens a TCP connection to each advertised port and performs a
framed `ping`/`pong` handshake, with a 0.3 second connect timeout. Ports that do not answer are
dropped, except when the file's `reloading` flag is set and the heartbeat is under 60 seconds old, in
which case the instance is kept and reported as reloading.

The consequence is that discovery costs real I/O. Every per-call `unity_instance=` resolution passes
`force_refresh=True`, so it re-probes every candidate port rather than using the 5 second cache.

It also means stale entries are possible. `Stop()` deletes the status file on a clean shutdown, but a
crashed or force-quit Editor leaves its file behind, and nothing garbage-collects it. Those ghosts are
probed on every discovery and cost a connect timeout each before being discarded.

### HTTP: an in-memory registry of live sockets

Unity dials out to `/hub/plugin` and stays connected. On connect the server sends a welcome frame
advertising a 30 second timeout and a 15 second keepalive; Unity replies with a `register` message
carrying its project name, hash, version and path. The server mints a session UUID, stores the socket
in `PluginRegistry`, and answers with `registered`.

Discovery is then a dictionary read. No files, no probes, no timeouts, and no staleness, because an
entry exists exactly as long as its WebSocket does.

Registration is also idempotent by hash: if an Editor re-registers with a hash that is already
present, the older session is evicted, its socket closed, and any in-flight commands on it are failed
immediately rather than left to time out. That eviction is the domain-reload reconnect path.

## The routing rules

Three mechanisms, checked in this order.

**A per-call `unity_instance` argument wins.** The middleware pops it out of the tool arguments before
validation, so no tool declares it in its own signature. This is stateless and needs no prior setup.

**Otherwise, the session pin applies.** `set_active_instance` stores a choice in session state keyed on
`ctx.session_id`. The key must be the session and nothing else. #1023 was caused by keying on the
client-supplied `client_id`, which let two clients presenting the same id share one pin. Any fallback
chain ending in `client_id`, `user_id` or a literal `"global"` reintroduces that.

**Otherwise, if exactly one Editor is connected, use it.** This is the ordinary single-project case and
should not require configuration. It is disabled when `http_remote_hosted` is set, because on a shared
server "the only instance" means only right now.

If none of those apply, the server raises rather than picking. With two Editors connected and nothing
pinned, you get the available identifiers back and are asked to choose:

```
Multiple Unity instances are connected and none is selected.
Pass unity_instance on the call or use set_active_instance with one of: ['A@h1', 'B@h2'].
Read mcpforunity://instances for current sessions.
```

An error you can act on is better than a write into the wrong project. This is the rule most of the
open routing PRs were trying to soften, and it is the one part of the design that is not negotiable.

## Selector formats

| Form | Example | Notes |
|---|---|---|
| `Name@hash` | `MyGame@a1b2c3d4` | Exact match |
| Hash prefix | `a1b2` | Must be unique; ambiguous prefixes raise |
| Port number | `6401` | stdio only |

Port targeting is stdio-only because a port identifies an Editor only when each Editor owns its own
listener. Under HTTP the same value is rejected explicitly rather than being reinterpreted.

Three formats that are not supported, and should not be added:

Bare project-name matching does not help in the case that matters, which is two checkouts of the same
project. Case-insensitive exact matching only widens the range of near-misses that resolve to
something. Treating a numeric value as a hash prefix under HTTP is the worst of the three, because
hashes can begin with digits, so `6401` would silently match an Editor the user did not mean.

`mcpforunity://instances` is the discovery surface, and error messages point at it rather than
embedding their own lists.

## Transport differences in one table

| | stdio | HTTP |
|---|---|---|
| Processes | One Python process per MCP client | One shared server |
| Discovery | Glob `~/.unity-mcp/unity-mcp-status-*.json`, then TCP probe | Read `PluginRegistry`, populated by Unity dialling in |
| Hash length | 8 chars | 16 chars |
| Session key | Per-process UUID | `MCP-Session-Id` header |
| Port targeting | Yes | Rejected |
| Command timeout | 90 s total across retries | 30 s per command, overridable per call |
| Reload handling | Status-file `reloading` flag, short-circuits before I/O | Waits up to 20 s for the socket to re-register |
| Two MCP clients | They fight over one TCP slot | Cleanly isolated |
| Multi-tenant | None | `user_id` from `X-API-Key`, sessions keyed `(user_id, hash)` |

That second-to-last row is the one people get bitten by. Under stdio the bridge assumes one Python
server per Editor, so when a new connection arrives it closes the existing ones on the grounds that
the old server must be gone. That assumption holds while each client owns its own process, which is
how stdio is meant to be used, but it means a second concurrent client does not coexist with the
first, it replaces it. Under HTTP two clients get separate sessions and separate pins against one
shared socket per Editor, and their commands are serialized in arrival order.

## What we are not building

No broker, gateway, queue, or scheduling layer. The proposal recurs in various forms, and the reason
for declining it is not stylistic.

A scheduler sitting on top of the bridge multiplies failure modes rather than removing them. The
transport still has open reliability bugs around domain reloads, and every queued command becomes one
that can be lost, duplicated or reordered in addition to the ways it can already fail. Fixing the
bridge has to come first.

The problem a broker solves is also not the problem users report. The reports are about writes landing
in the wrong project, which is a correctness problem.

Throughput has since been measured, and it argues the same way. Four concurrent agents issuing 527
calls against one Editor over eleven minutes drove 1,498 commands through the bridge at 97.9% success
without degrading it. The queueing that did show up — cheap reads stretching from ~5 s to ~17 s behind
another agent's writes — comes from Unity executing one command at a time on its main thread, which is
below the layer a broker would sit at. Reordering the queue cannot make a single-threaded Editor
concurrent; it only moves the wait and adds the loss and duplication modes described above.

And a broker is load-bearing from the moment it ships, whereas the rules above are small enough that
any one of them can be removed later without a migration. If concurrent-agent throughput turns out to
be a real constraint, it deserves its own design starting from profiling data.

## Known gaps

**The REST route still guesses.** `/api/command`, which is what the CLI talks to, falls back to
`next(iter(sessions.sessions.keys()))` when no instance is given. That is the coin flip removed from
the MCP path, still present on this one. It also matches hashes by exact equality only, so an
identifier that works as an MCP argument can 404 through the CLI, and it is not registered at all in
remote-hosted mode.

**A pin to a departed Editor costs 20 seconds and then reports the wrong thing.** When a target hash is
set, the ambiguity checks in the resolve loop are skipped, so a pin to an Editor that has gone away
waits out the full window and returns a generic "no Unity session available" rather than saying that
the pinned instance is not there.

Clearing the pin automatically is not the fix. Clearing it and then selecting the one remaining Editor
is #1023's exact harm arriving through a different door. The acceptable shape is to clear only after
the reconnect wait has expired for that specific hash, and never to retarget silently.

**Resolution hard-fails during the reconnect window.** Resolving an instance calls `_discover_instances`
before the command is dispatched. Under HTTP the registry is a live-socket map, so a domain reload
empties it until the Editor re-registers, and calls arriving in that gap fail with
`Instance 'X' not found. Available: none.` It was the dominant failure in multi-agent load testing —
roughly seven in ten client-visible errors — and the message misreports a two-second gap as an absent
Editor. The failures themselves are clean, because nothing has been dispatched yet. Awaiting the
re-registration for that specific hash, as the command path already does, would remove most of them.

**A timed-out command still runs.** When `asyncio.wait_for` expires, the `finally` in
`PluginHub.send_command` pops the pending future, but nothing tells Unity to stop. If the command had
already begun executing it runs to completion, and its `command_result` arrives to find no future
waiting and is discarded. The caller is told the command failed while its effect landed. Commands still
queued when the socket drops are lost cleanly instead, so the outcome depends on how far the command
got — and with no idempotency key, a retry cannot be distinguished from a first attempt. Ordinary tool
calls stay far below the 30 second budget, so this needs a genuinely long command such as
`execute_code`, an import, or a test run.

**Session state is never cleaned up.** Keys carry a 24 hour TTL re-armed on every write, and nothing
deletes them when an MCP session ends, so under HTTP every terminated session leaves entries behind
for up to a day.

**Two state keys, two lifetimes.** The pin is stored under `mcpforunity.active_instance`, but tools read
`unity_instance`, which the middleware rewrites on every call. A per-call override written to the
second key can therefore outlive the call that set it, which contradicts the documented promise that
per-call routing does not change the session default.

## Contributing

Work that implements these rules, closes the REST-route gap, or improves discovery and schema
advertisement is wanted. Work that adds a fourth routing rule, widens the selector grammar, or
introduces a brokering layer will be declined, and that is a statement about surface area rather than
about code quality.

The routing logic lives in `Server/src/transport/unity_instance_middleware.py`. If a change needs to
touch a dozen files to express itself, that usually means it is working against the contract rather
than extending it.
