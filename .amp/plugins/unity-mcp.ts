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
 *   UNITY_MCP_TIMEOUT_MS  Per-call timeout in ms (default 120000)
 */

import type { PluginAPI } from '@ampcode/plugin'

const BASE_URL = (process.env.UNITY_MCP_SERVER_URL ?? 'http://127.0.0.1:8080').replace(/\/+$/, '')
const TIMEOUT_MS = Number(process.env.UNITY_MCP_TIMEOUT_MS ?? 120_000)

type CommandBody = {
	type: string
	params: Record<string, unknown>
	unity_instance?: string
}

async function postCommand(body: CommandBody): Promise<string> {
	const ctrl = new AbortController()
	const timer = setTimeout(() => ctrl.abort(), TIMEOUT_MS)
	try {
		const res = await fetch(`${BASE_URL}/api/command`, {
			method: 'POST',
			headers: { 'content-type': 'application/json' },
			body: JSON.stringify(body),
			signal: ctrl.signal,
		})
		const text = await res.text()
		if (!res.ok) {
			// Pass through the server's structured error so the model can read it.
			return text || JSON.stringify({ success: false, error: `HTTP ${res.status}` })
		}
		return text
	} catch (err) {
		const msg = err instanceof Error ? err.message : String(err)
		return JSON.stringify({
			success: false,
			error: `Unity MCP server unreachable at ${BASE_URL}: ${msg}. Is the Python server running and is Unity open with the MCP for Unity package?`,
		})
	} finally {
		clearTimeout(timer)
	}
}

async function listInstances(): Promise<string> {
	try {
		const res = await fetch(`${BASE_URL}/api/instances`, { signal: AbortSignal.timeout(5_000) })
		return await res.text()
	} catch (err) {
		const msg = err instanceof Error ? err.message : String(err)
		return JSON.stringify({ success: false, error: msg })
	}
}

export default function (amp: PluginAPI) {
	amp.registerTool({
		name: 'unity',
		description: [
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
		].join('\n'),
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
			const tool = String(input.tool ?? '').trim()
			if (!tool) {
				return JSON.stringify({ success: false, error: 'Missing required field: tool' })
			}
			const params = (input.params && typeof input.params === 'object' ? input.params : {}) as Record<string, unknown>
			const body: CommandBody = { type: tool, params }
			if (typeof input.unity_instance === 'string' && input.unity_instance.length > 0) {
				body.unity_instance = input.unity_instance
			}
			return postCommand(body)
		},
	})

	amp.registerCommand(
		'status',
		{ title: 'Status', category: 'Unity', description: 'Show connected Unity instances and server reachability' },
		async (ctx) => {
			const text = await listInstances()
			await ctx.ui.notify(`Unity MCP @ ${BASE_URL}\n${text}`)
		},
	)

	amp.on('session.start', async (_event, ctx) => {
		try {
			const res = await fetch(`${BASE_URL}/api/instances`, { signal: AbortSignal.timeout(1_500) })
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
