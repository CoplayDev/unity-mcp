/**
 * Unity MCP — Amp plugin
 *
 * Exposes the Unity Editor (via the MCP for Unity Python server's REST endpoint)
 * to Amp through a single `unity` tool. The Python server retains every tool
 * implementation and dispatches to Unity over WebSocket; this plugin is a thin
 * proxy so Amp sees one tool definition (cheap on tokens) instead of 38+.
 *
 * Requires the MCP for Unity Python server running locally
 * (default http://127.0.0.1:8080) and the Unity Editor connected to it.
 *
 * Configuration:
 *   UNITY_MCP_SERVER_URL  Override base URL (default http://127.0.0.1:8080)
 *   UNITY_MCP_TIMEOUT_MS  Per-call timeout in ms, must be a positive number
 *                         (default 120000; falls back to default if missing,
 *                         non-numeric, NaN, infinite, or <= 0)
 */

import type { PluginAPI } from '@ampcode/plugin'

/** Default base URL for the local MCP for Unity Python server. */
export const DEFAULT_BASE_URL = 'http://127.0.0.1:8080'

/** Default per-call timeout for proxied Unity commands, in milliseconds. */
export const DEFAULT_TIMEOUT_MS = 120_000

/** Timeout for the lightweight `/api/instances` reachability probes, in milliseconds. */
export const INSTANCES_PROBE_TIMEOUT_MS = 5_000

/** Timeout for the optional `session.start` reachability probe, in milliseconds. */
export const SESSION_START_PROBE_TIMEOUT_MS = 1_500

/**
 * Body shape sent to the Python server's `/api/command` endpoint, mirroring
 * the request the existing `unity-mcp` CLI issues.
 */
export type CommandBody = {
	type: string
	params: Record<string, unknown>
	unity_instance?: string
}

/**
 * Minimal `fetch`-shaped function we depend on. Defined explicitly so tests can
 * supply a stub without relying on global mocking.
 */
export type FetchLike = (
	input: string,
	init?: { method?: string; headers?: Record<string, string>; body?: string; signal?: AbortSignal },
) => Promise<{ ok: boolean; status: number; text(): Promise<string>; json(): Promise<unknown> }>

/**
 * Strip a trailing slash (or run of slashes) so callers can append a path
 * without producing `//api/command`.
 */
export function normalizeBaseUrl(raw: string): string {
	return raw.replace(/\/+$/, '')
}

/**
 * Resolve the per-call timeout from an environment variable value, clamping
 * missing / non-numeric / non-positive / non-finite values to a sane default.
 *
 * Without this, `Number(undefined)` yields `NaN`, and an `AbortController`
 * scheduled with `setTimeout(_, NaN)` fires immediately, aborting every
 * request before the network even touches the wire.
 */
export function resolveTimeoutMs(raw: string | undefined, defaultMs: number = DEFAULT_TIMEOUT_MS): number {
	if (raw === undefined || raw === '') return defaultMs
	const parsed = Number(raw)
	return Number.isFinite(parsed) && parsed > 0 ? parsed : defaultMs
}

/**
 * Build the JSON body for `/api/command` from the agent-supplied tool input.
 * Only includes `unity_instance` when the caller passed a non-empty string.
 */
export function buildCommandBody(input: Record<string, unknown>): CommandBody {
	const tool = String(input.tool ?? '').trim()
	if (!tool) {
		throw new Error('Missing required field: tool')
	}
	const rawParams = input.params
	const params =
		rawParams && typeof rawParams === 'object' && !Array.isArray(rawParams)
			? (rawParams as Record<string, unknown>)
			: {}
	const body: CommandBody = { type: tool, params }
	if (typeof input.unity_instance === 'string' && input.unity_instance.length > 0) {
		body.unity_instance = input.unity_instance
	}
	return body
}

/**
 * Format a structured failure response that mirrors the Python server's
 * `{ success: false, error: ... }` shape so the agent can parse either case
 * with the same code path.
 */
export function formatUnreachableError(baseUrl: string, message: string): string {
	return JSON.stringify({
		success: false,
		error: `Unity MCP server unreachable at ${baseUrl}: ${message}. Is the Python server running and is Unity open with the MCP for Unity package?`,
	})
}

/**
 * POST a Unity command to the Python server and return the raw response body
 * verbatim so the calling agent sees the server's structured success/error
 * shape unchanged.
 *
 * Network failures and non-2xx responses are normalized to a JSON string with
 * `{ success: false, error: ... }` so callers never receive a thrown error.
 */
export async function postCommand(
	fetchFn: FetchLike,
	baseUrl: string,
	timeoutMs: number,
	body: CommandBody,
): Promise<string> {
	const ctrl = new AbortController()
	const timer = setTimeout(() => ctrl.abort(), timeoutMs)
	try {
		const res = await fetchFn(`${baseUrl}/api/command`, {
			method: 'POST',
			headers: { 'content-type': 'application/json' },
			body: JSON.stringify(body),
			signal: ctrl.signal,
		})
		const text = await res.text()
		if (!res.ok) {
			return text || JSON.stringify({ success: false, error: `HTTP ${res.status}` })
		}
		return text
	} catch (err) {
		const msg = err instanceof Error ? err.message : String(err)
		return formatUnreachableError(baseUrl, msg)
	} finally {
		clearTimeout(timer)
	}
}

/**
 * GET the current list of connected Unity instances from the Python server.
 * Returns the raw response body, or a JSON error string on failure.
 */
export async function listInstances(
	fetchFn: FetchLike,
	baseUrl: string,
	timeoutMs: number = INSTANCES_PROBE_TIMEOUT_MS,
): Promise<string> {
	const ctrl = new AbortController()
	const timer = setTimeout(() => ctrl.abort(), timeoutMs)
	try {
		const res = await fetchFn(`${baseUrl}/api/instances`, { signal: ctrl.signal })
		return await res.text()
	} catch (err) {
		const msg = err instanceof Error ? err.message : String(err)
		return JSON.stringify({ success: false, error: msg })
	} finally {
		clearTimeout(timer)
	}
}

/**
 * Description shown to the LLM for the single `unity` tool. Kept terse and
 * pointed at the existing `unity-mcp-orchestrator` skill so Amp pays the
 * tokens for the catalog only when the model actually needs it.
 */
export const UNITY_TOOL_DESCRIPTION = [
	'Call any MCP for Unity tool through the local Python MCP server.',
	'Use this for every Unity Editor automation: GameObjects, scripts, scenes, assets, prefabs, components, build, tests, console, screenshots, etc.',
	'',
	'Common `tool` values (full list in the unity-mcp-orchestrator skill):',
	'  • Reads:    find_gameobjects, find_in_file, read_console, unity_reflect, unity_docs, preflight',
	'  • GameObj:  manage_gameobject, manage_components, manage_prefabs, manage_scene',
	'  • Scripts:  manage_script, script_apply_edits, refresh_unity, execute_code',
	'  • Assets:   manage_asset, manage_material, manage_texture, manage_shader, manage_packages',
	'  • Editor:   manage_editor, execute_menu_item, manage_camera, set_active_instance',
	'  • Misc:     batch_execute, run_tests, manage_build, manage_animation, manage_ui, manage_vfx',
	'',
	'Resources (read-only state) live under mcpforunity://… and are fetched by name through the same Python server (use the relevant manage_* tool or read_console for reads).',
	'Prefer `batch_execute` when issuing 3+ related calls — it is 10–100× faster.',
	'After scripts change, poll editor state for `is_compiling=false` then call read_console for errors.',
	'`params` is the exact parameter object the underlying tool expects. If unsure of the shape, load the unity-mcp-orchestrator skill or call `unity` with `tool="preflight"` first.',
].join('\n')

const BASE_URL = normalizeBaseUrl(process.env.UNITY_MCP_SERVER_URL ?? DEFAULT_BASE_URL)
const TIMEOUT_MS = resolveTimeoutMs(process.env.UNITY_MCP_TIMEOUT_MS, DEFAULT_TIMEOUT_MS)

/**
 * Plugin entry point. Registers the `unity` proxy tool, the `Unity: Status`
 * palette command, and a `session.start` listener that logs reachability.
 */
export default function (amp: PluginAPI) {
	amp.registerTool({
		name: 'unity',
		description: UNITY_TOOL_DESCRIPTION,
		inputSchema: {
			type: 'object',
			properties: {
				tool: {
					type: 'string',
					description: 'Underlying Unity MCP tool name, e.g. "manage_gameobject", "find_gameobjects", "read_console".',
				},
				params: {
					type: 'object',
					description: 'Parameter object for the tool. Shape depends on the tool.',
					additionalProperties: true,
				},
				unity_instance: {
					type: 'string',
					description: 'Optional Unity instance name or hash when multiple Unity Editors are connected. Omit to use the first available instance.',
				},
			},
			required: ['tool'],
		},
		async execute(input) {
			let body: CommandBody
			try {
				body = buildCommandBody(input)
			} catch (err) {
				const msg = err instanceof Error ? err.message : String(err)
				return JSON.stringify({ success: false, error: msg })
			}
			return postCommand(fetch as FetchLike, BASE_URL, TIMEOUT_MS, body)
		},
	})

	amp.registerCommand(
		'status',
		{ title: 'Status', category: 'Unity', description: 'Show connected Unity instances and server reachability' },
		async (ctx) => {
			const text = await listInstances(fetch as FetchLike, BASE_URL)
			await ctx.ui.notify(`Unity MCP @ ${BASE_URL}\n${text}`)
		},
	)

	amp.on('session.start', async (_event, ctx) => {
		try {
			const res = await (fetch as FetchLike)(`${BASE_URL}/api/instances`, {
				signal: AbortSignal.timeout(SESSION_START_PROBE_TIMEOUT_MS),
			})
			if (!res.ok) {
				ctx.logger.log(`Unity MCP server reachable but /api/instances returned HTTP ${res.status}`)
				return
			}
			const data = (await res.json()) as { instances?: unknown[] }
			const count = Array.isArray(data.instances) ? data.instances.length : 0
			ctx.logger.log(`Unity MCP plugin loaded — ${count} Unity instance(s) connected at ${BASE_URL}`)
		} catch {
			ctx.logger.log(`Unity MCP plugin loaded — server not reachable at ${BASE_URL} (will retry per-call)`)
		}
	})
}
