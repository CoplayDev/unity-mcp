---
title: v8 - New Networking Setup
author: Marcus Sanatan <marcus@coplay.dev>
date: 2025-11-15
---

# HTTP and Stdio Support

This project has 3 components:

- MCP Client
- MCP Server
- Unity Editor plugin

![3 components of MCP for Unity](./images/networking-architecture.png)

The MCP clients (e.g., Cursor, VS Code, Windsurf, Claude Code) are how users interact with our systems. They communicate with the MCP server by sending commands. The MCP commands communicates with our Unity plugin, which gives reports on the action it completed (for function tools) or gives it data (for resources).

The MCP protocol defines how clients and servers can communicate, but we have to get creative when communicating with Unity. Let's learn more.

## How do MCP components communicate?

MCP servers support communicating over [stdio or via Streamable HTTP](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports).

### Stdio Architecture

MCP for Unity communicates via stdio. Particularly, the MCP server and the MCP client use stdio to communicate. The MCP server and the Unity plugin editor communicate via a locally opened port, typically 6400, but users can change it to any port.

Why can't the Unity plugin communicate to the server via stdio like the MCP client? When we create MCP configs that use `uvx`, MCP clients run the command in an *internal subprocess*, and communicate with the MCP server via stdio (think pipes in the OS e.g. `ls -l | grep "example.txt"`).

Unity can't reach that internal subprocess, so we listen to port 6400, which the MCP server can open and send/receive data.

> **Note**: Previously we used `uv`, and we installed the server locally in the plugin. Now we use `uvx` which installs the server for us, directly from our GitHub repo.

When the user sends a prompt:

1. The MCP client will send a request to the MCP server via stdio
2. The MCP server would process the request and connect to port 6400
3. The MCP server sends the command, and the Unity plugin responds via port 6400
4. The MCP server parses the response and returns JSON to the MCP client via stdio

In this new version of MCP for Unity, our MCP server supports both the stdio and HTTP protocols. 

### HTTP Architecture

We create MCP configs that reference a URL, by default http://localhost:8080, however, users can change it to any address. MCP clients connect to the running HTTP server, and communicate with the MCP server via HTTP POST requests with JSON. Unlike in stdio, the MCP server is not a subprocess of the client, but is run independently of all other components.

What about the MCP server and Unity? We could maintain the communication channel that's used in stdio i.e. communicating via port 6400. However, this would limit the HTTP server to only being run locally. A remote HTTP server would not have access to a user's port 6400 (unless users open their ports to the internet, which may be hard for some and is an unnecessary security risk).

To work with both locally and remotely hosted HTTP servers, we set up a *WebSocket connection* between Unity and the MCP Server. This allows for real time communication between the two components.

When the user sends a prompt:

1. The MCP client will send an HTTP POST request to the MCP server
2. The MCP server would process the request and send a message to Unity via WebSockets
3. The Unity plugin sends a response via WebSockets to the MCP server
4. The MCP server parses the response and returns JSON to the MCP client via Server-Sent Events

MCP for Unity previously only supported local connections with MCP clients via stdio. Now, we continue to support local stdio connections, but additionally support local HTTP connections and remote HTTP connections.

## Why add HTTP support?

Let's discuss both technical and political reasons:

- More flexibility on where the HTTP server can be run:
  - Do you want to run the MCP server in your terminal/PowerShell/Command Prompt? You can.
  - Do you want to run the MCP server in Windows Subsystem for Linux (WSL), where you prefer to install Python/`uv`? You can.
  - Do you want to run the MCP server in a docker container? You can.
  - Do you want to run the MCP server on a dedicated server all your personal computers connect to? You can.
  - Do you want to run MCP server in the cloud and have various projects use it? You can.
- HTTP opens up easier ways to communicate with the MCP server w/o using the MCP protocol
  - For example, this version supports custom tools that only require C# code (see [CUSTOM_TOOLS.md](./CUSTOM_TOOLS.md) for more info). This was easy to implement because we added a special endpoint to handle tool registration
- Our MCP server can now be hosted by various MCP marketplaces, they typically require an HTTP server because they host it remotely.
- We can distribute the plugin with a remote URL, so users would not need to install Python or `uv` installed to use MCP for Unity.
  - This is a contentious issue. Who should host the server, particularly for an open source, community centered project? For now, Coplay will host the server as it is the sponsor of this project. This remote URL would not be the default for users who install via Git or OpenUPM, but it will become the default for users who install via the Unity Asset Store, where we can't submit the plugin if it requires Python/`uv` to be installed.
  - Not having to setup Python and `uv` has benefits to non-asset store users, but I think to avoid maintaining this server, we'll explore running the MCP server inside the Unity plugin as a background process using the [official MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

## How was it implemented?

Significant changes were made to the server and Unity plugin to support the HTTP protocol, as well as the new WebSocket connection, with the right amount of abstraction to support both stdio and HTTP.

### Server

`server.py` is still the main entrypoint for the backend, but now it's been modified to setup both HTTP and stdio connections. It processes command line arguments or environment variables for the HTTP mode. CLI args take precedence over the environment variables. The following code runs the server:

```python
mcp.run(transport=transport, host=host, port=port)
```

And that's pretty much it in terms of HTTP support between the MCP server and client. Things get more interesting for the connection to the Unity plugin.

Backward compatability with stdio connections was maintained, but we did make some small performance optimisations. Namely, we have an in-memory cache of unity isntances using the `StdioPortRegistry` class.

It still calls `PortDiscovery.discover_all_unity_instances()`, but we add a lock when calling it, so multiple attempts to retrieve the instances do not cause our app to run multiple file scans at the same time. 

The `UnityConnection` class uses the cached ports to retrieve the open port for a specific instances when creating a new connection, and when sending a command.

For WebSocket connections, we need to understand the `PluginHub` and the `PluginRegistry` classes. The plugin hub is what manages the WebSocket connections with the MCP server in-memory. It also has the `send_command_for_instance` function, which actually sends the command to the Unity plugin.

The in-memory mapping of sessions to WebSockets connections in the plugin hub is done via the `PluginRegistry` class.

You're wondering if every function tool needs to use the `send_command_for_instance` and the current function and choose between WebSockets/stdio every invocation? No, to keep tool calls as simple as posisble, all users have to do is call the `send_with_unity_instance`, which delegates the actual sending of data to `send_command_for_instance` or `send_fn`, which is a function that's parsed to the arguments of `send_with_unity_instance`, typically `async_send_command_with_retry`.

### Unity Plugin

Let's start with how things worked before this change. The `MCPForUnityBridge` was a monolith of all networking logic. As we began to develop a service architecture, we created the `BridgeControlService` to wrap the `MCPForUnityBridge` class, to make the migration to the new architecture easier.

The `BridgeControlService` called functions in the `MCPForUnityBridge`, which managed the state and processing for TCP communication.

In this version `BridgeControlService` wraps around the `TransportManager`, it doesn't have hardcoded logic specific to stdio. The `TransportManager` object manages the state of the network and delegates the actual networking logic to the appropriate transport client - either WebSocket or stdio. The `TransportManager` interacts with objects that implement the `IMcpTransportClient` interface.

The legacy `McpForUnityBridge` was renamed and moved to `StdioBridgeHost`. The `StdioTransportClient` class is a thin wrapper over the `StdioBridgeHost` class, that implements the `IMcpTransportClient` interface. All the logic for the WebSocket connection is in the `WebSocketTransportClient` class.

### MCP Configs

### Stdio config updates

Since we support both HTTP and stdio connections, we had to do some work around the MCP config builders. The major change was reworking how stdio connections were constructed to use `uvx` with the remote package instead of the locally bundled server and `uv`, HTTP configs are much simpler.

The remote git URL we use to get the package is versioned, which added some complications. We frequently make changes to the `main` branch of this repo, some are breaking (the last version before this was v7, which was a major breaking change as well). We don't control how users update their MCP for Unity package. So if we point to the main branch, their plugin could be talking to an incompatible version of the server.

To address this, we have a process to auto-update stdio configurations. The `StdIoVersionMigration` class runs when the plugin is loaded. It checks a new editor pref that stores the last version we upgraded clients to. If the plugin was updated, the package version will mismatch the editor pref's version, and we'll cycle through a user's configured MCP clients and re-configure them.

This way, whenever a user updates the plugin, they will automatically point to the correct version of the MCP server for their MCP clients to use.

Relevant commits:

- https://github.com/CoplayDev/unity-mcp/pull/375/commits/7dff06679b89564ad92c88d8fe70c08e8efcbc22

### Upgrading configs from v7 to v8

The new HTTP config and the new stdio config using `uvx` is a departure from the previous MCP configs which have `uv` and a path to `server.py`. No matter the protocol, all users would have to update their MCP configs. Not all users are on Discord where we can reach them, and not all our Discord users read our messages in any case. Forcing users to update their configs after updating is something they can easily ignore or forget.

So we added the `LegacyServerSrcMigration` class. It looks for the `MCPForUnity.UseEmbeddedServer` editor pref, which was used in earlier versions. If this pref exists, we will reconfigure all of a user's MCP clients (defaulting to HTTP) at startup. The editor pref is then deleted, so this config update only happens once.

Relevant commits:

- https://github.com/CoplayDev/unity-mcp/pull/375/commits/996ca48894a669344e3a7f3eff3d9e9913caec7d

## Other changes

This version contains numerous other updates, including:

### Using `uvx` instead of `uv`

Previously, the MCP server was bundled with the plugin in the `UnityMcpServer~/src` folder. I don't have the context as to why this was done, but I imagine `uv` support for running remote packages was not available/popular at the time this repo was created.

By using `uvx` and remote packages, we can safely offload all aspects of server file management from our plugin.

Relevant commits:

- https://github.com/CoplayDev/unity-mcp/pull/375/commits/64d64fde45af540229cf1995561cafc436bc3686
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/c830d56648710e4723a238a4692b7f85df4d4e42
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/85e934265c25b24cf44e4e758cb261fdb6eb333f
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/d217e2899e4b245ee25cb5f667dbb0be3dcf4948
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/50902b92f2f539b6292fec08e3fe9bedb91b2341
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/08b3d1893f003cc0c354079329879aa7b2ed8829
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/014f8c7db9c7b91054e177a64f30eb6bea3f9193
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/cad8c20faff9caf51bfc7772a40404f6fceeac33

### Asynchronous tools and resources

Previously we had `async_send_with_unity_instance` and `send_with_unity_instance` functions to communicate with the Unity. Now, we only have `send_with_unity_instance`, and it's asynchronous. 

This was required for the HTTP server, because we cannot block the event loop with synchronous commands. This change does not affect how the server works when using stdio.

Relevant commits:

- https://github.com/CoplayDev/unity-mcp/pull/375/commits/d5d738d83d96eabdc19e13bb650cd8fe578c58bc
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/8b4bcb65cdaf1bdefcb3828c170307de0588c18f
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/d6e2466b6869cc64ad8a358ec95d045830f37eff

### Custom tools

Custom tools were revamped once more, this time they're reached the simplest version that we wanted them to have - custom tools are written entirely in C# - no Python required. How does it work?

Like before, we do reflection on the `McpForUnityToolAttribute`. However, this time the attribute now accepts a `name`, `description`, and `AutoRegister`. The `AutoRegister` boolean is true by default, but for our core tools it's false, as they don't have their tool details nor parameters defined in C# as yet.

Parameters are defined using the `ToolParameterAttribute`, which contains `Name`, `Description`, `Required`, and `DefaultValue` properties. 

The `ToolDiscoveryService` class uses reflection to find all classes with `McpForUnityToolAttribute`. It does the same for `ToolParameterAttribute`. With that data, it constructs a `ToolMetadata` object. These tools are stored in-memory in a dictionary that maps tool names with their metadata.

When we initiate a websocket connection, after successfully registering and retrieving a session ID, we call the `SendRegisterToolsAsync` function. This function sends a JSON payload to the server with all the tools that were found in the `ToolDiscoveryService`.

In the `plugin_hub`'s `on_receive` handler, we look out for the `register_tools` message type, and map the tools to the session ID. This is important, we only want custom tools to be available for the project they've been added to.

That requirement of keeping tools local to the projeect made this implementation a bit trickier. We have the requirement because in this project, we can run multiple Unity instances at the same time. So it doesn't make sense to make every tool globally available to all connected projects.

To make tools local to the project, we add a `unity://custom-tools` resource which lists all tools mapped to a session (which is retrieve from FastMCP's context). And then we add a `execute_custom_tool` function tool which can call the tools the user added. This worked surprisingly well, but required some tweaks:

- We removed the fallback for session IDs in the server. If there's more than one Unity instance connected to the server, the MCP client MUST call `set_active_instance` so the mapping between session IDs and Unity instances will be correct.
- We removed the `read_resources` tool. It simply did not work, and LLMs would go in circles for a long time before actually reading the resource directly. This only works because MCP resources have up to date information and gives the MCP clients the right context to call the tools.

> **Note**: FastMCP can register and deregister tools while the server is running, however, not all MCP clients can process the updates in real time. We recommend that users refresh/reconfigure the MCP servers in the clients so they can see the new custom tools.

Relevant commits:

- https://github.com/CoplayDev/unity-mcp/pull/375/commits/b4be06893ef218a84468dbc71b9dc8614289e433
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/77641dae64e8b3c572dd876af0b59ea454f04b0c
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/f968c8f446dff6fb0c70d033b148de934c6aebf3
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/ea754042b645a22cefb4f2fb820d1f4756af4ded
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/e9254c7776d7d948722b58805ee047499fc5a65b
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/662656b56a1b77c3f59116522e89c78b9b8af76f
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/cd88e86762cf82e0db8e687a2e64211c25b47b80
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/95c5265816aa7205588130f211f86e5e1e2d637b
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/85cd5c0cf47582bb43eab7ec998f4044a6430275
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/a84c2c29a08cabc3345e50147afa896ea4ae37bf
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/4f22d54ae38f84cfc05e50ad30675f4bb728f76d
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/01976a507396bf7fca1fd253172dd4c83ff33867
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/7525dfa547db5730cd911db25d2baa8bad969c71
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/53a397597df3fcaa4fa54188e9920348158c7425

### Window logic has been split into separate classes

The main `MCPForUnityEditorWindow.cs` class, and the releated uxml and uss files, were getting quite long. We had a similar problem with the last immediate UI version of it. To keep it maintanable, we split the logic into 3 separate view classes: Settings, Connection andn ClientConfig. They correspond to the 3 visual sections the window has.

Each section has its own C#, uxml and uss files, but we use a common uss file for shared styles.

Relevant commits:

- https://github.com/CoplayDev/unity-mcp/pull/375/commits/154b4ff3ad9c98f5f5ee8628cd8bcb79d0e108b5
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/1a9bb008a416a2b3abb0d91819a8173d362748b8

#### Setup Wizard

The Setup Wizard also got revamped. For starters, it's no longer a wizard, just a single window with a status and instructions. We check if Python and uv are installed, based on us being able to check their version by calling them in a process. That's the most reliable indicator of them being correctly installed. Otherwise, we have buttons that open up the webpages for users to download them as needed.

Relevant commits:

- https://github.com/CoplayDev/unity-mcp/pull/375/commits/aa63f21ea42372853690618d928cd1fad73e7c25
- https://github.com/CoplayDev/unity-mcp/pull/375/commits/cd4529c21f35e5be10a98dcf9303c210ebf42d2b

### Response classes

Previously, the Response class had helper functions that returned generic objects. It's not the worst option, but we've improved it with strongly typed classes. Instead of `Response.Success()` returning a success message, we now return `SuccessResponse` objects. Similarly, `Response.Error()` now returns `ErrorResponse` objects.

JSON serialization is the exact same, but it's clearer in the code what's being transmitted between the client and server.

Relevant commits:

- https://github.com/CoplayDev/unity-mcp/pull/375/commits/f917d9489540498a908f514a561160c08d9d1023

### Miscellaneous

- The shortcut (Cmd+Shift+M on macOS, Ctrl+Shift+M on Windows/Linux) can now be used to open and close the MCP for Unity window.
- The `McpLog` object now has a `Debug` function, which only logs when the user selects "Show debug logs" in the settings.
- All `EditorPrefs` are defined in `MCPForUnity/Editor/Constants/EditorPrefKeys.cs`. At a glance, we can see all the settings we have available.

## Future plans

This was a big change, and it touches all the repo. So a lot of inefficiencies and room for improvement were exposed while working on it. Here are some items to address:

- Loose types in Python. A lot of the new code would use dictionaries for structured data, which works, but we can benefit much more from using Pydantic classes with proper type checking. We always want to know when data is not being transferred in the format we expect it to. Plus, strong types make the code easier for humans and LLMs to reason about.
- A lot of tools define a `_coerce_int` function, why? Why are we redefining a function that's the same across files? Can we use a shared function, or maybe use it as middleware?
- Similarly, the `DummyMCP` class is defined in 10 server tests, we could set this up in `conftest.py`. These tests were originally indepdendent of the `Server` project, but in v7 they became integration tests we run with `pytest`. With `pytest` being the default test runner, we can relook at how the tests are structured and optimize their setup.
- `server_version.txt` is used in one place, but the server can now read its own pyproject.toml to get the version, so we can remove this.
- ~~Think about a structure of the MCP server some more. The `tools`, `resources` and `registry` folders make sense, but everything else just forms part of the high level repo. It's growing, so some thought about how we create modules will help with scalability.~~
  - This was done, Server folder is much more hierarchical and structured.
- The way we register tools is a good platform for all tools to be defined by C#. Having all tools in the plugin makes it easier for us to maintain, the community to contribute, and users to modify this project to suit their needs. If all tools are registered from the plugin, we can allow users to select the tools they want to use, giving them even more control of their experience.
  - Of course, we need some testing of this custom tool architecture to know if it can scale to all tools. Also, custom tool registration is only supported with HTTP, so we'll need to support this feature when the stdio protocol is being used.
