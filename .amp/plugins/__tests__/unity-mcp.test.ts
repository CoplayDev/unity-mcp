/**
 * Unit tests for the Unity MCP Amp plugin.
 *
 * Run with:
 *   bun test .amp/plugins/__tests__/unity-mcp.test.ts
 *
 * Covers the pure helpers (timeout/url parsing, body building, error
 * formatting) and the network-facing helpers (postCommand, listInstances)
 * with an injected fetch stub.
 */

import { describe, expect, test } from 'bun:test'

import {
	DEFAULT_BASE_URL,
	DEFAULT_TIMEOUT_MS,
	type CommandBody,
	type FetchLike,
	buildCommandBody,
	formatUnreachableError,
	listInstances,
	normalizeBaseUrl,
	postCommand,
	resolveTimeoutMs,
} from '../unity-mcp'

// ---------------------------------------------------------------------------
// resolveTimeoutMs — guards against the "NaN -> immediate abort" bug
// ---------------------------------------------------------------------------

describe('resolveTimeoutMs', () => {
	test('returns default when env var is undefined', () => {
		expect(resolveTimeoutMs(undefined)).toBe(DEFAULT_TIMEOUT_MS)
	})

	test('returns default when env var is empty string', () => {
		expect(resolveTimeoutMs('')).toBe(DEFAULT_TIMEOUT_MS)
	})

	test('returns default when env var is non-numeric', () => {
		expect(resolveTimeoutMs('abc')).toBe(DEFAULT_TIMEOUT_MS)
	})

	test('returns default when env var parses to NaN', () => {
		expect(resolveTimeoutMs('not-a-number')).toBe(DEFAULT_TIMEOUT_MS)
	})

	test('returns default when env var is zero', () => {
		expect(resolveTimeoutMs('0')).toBe(DEFAULT_TIMEOUT_MS)
	})

	test('returns default when env var is negative', () => {
		expect(resolveTimeoutMs('-100')).toBe(DEFAULT_TIMEOUT_MS)
	})

	test('returns default when env var is Infinity', () => {
		expect(resolveTimeoutMs('Infinity')).toBe(DEFAULT_TIMEOUT_MS)
	})

	test('accepts a positive integer string', () => {
		expect(resolveTimeoutMs('5000')).toBe(5000)
	})

	test('accepts a positive float string', () => {
		expect(resolveTimeoutMs('1500.5')).toBe(1500.5)
	})

	test('respects a custom default', () => {
		expect(resolveTimeoutMs(undefined, 999)).toBe(999)
		expect(resolveTimeoutMs('bad', 999)).toBe(999)
	})
})

// ---------------------------------------------------------------------------
// normalizeBaseUrl
// ---------------------------------------------------------------------------

describe('normalizeBaseUrl', () => {
	test('leaves a clean URL untouched', () => {
		expect(normalizeBaseUrl('http://127.0.0.1:8080')).toBe('http://127.0.0.1:8080')
	})

	test('strips a single trailing slash', () => {
		expect(normalizeBaseUrl('http://127.0.0.1:8080/')).toBe('http://127.0.0.1:8080')
	})

	test('strips multiple trailing slashes', () => {
		expect(normalizeBaseUrl('http://127.0.0.1:8080///')).toBe('http://127.0.0.1:8080')
	})

	test('matches the documented default', () => {
		expect(DEFAULT_BASE_URL).toBe('http://127.0.0.1:8080')
	})
})

// ---------------------------------------------------------------------------
// buildCommandBody
// ---------------------------------------------------------------------------

describe('buildCommandBody', () => {
	test('builds the minimum valid body', () => {
		expect(buildCommandBody({ tool: 'find_gameobjects' })).toEqual({
			type: 'find_gameobjects',
			params: {},
		})
	})

	test('passes params through unchanged', () => {
		const params = { name: 'Main Camera', includeInactive: true }
		expect(buildCommandBody({ tool: 'find_gameobjects', params })).toEqual({
			type: 'find_gameobjects',
			params,
		})
	})

	test('coerces missing/non-object params to {}', () => {
		expect(buildCommandBody({ tool: 'x', params: undefined }).params).toEqual({})
		expect(buildCommandBody({ tool: 'x', params: null }).params).toEqual({})
		expect(buildCommandBody({ tool: 'x', params: 'string' }).params).toEqual({})
		expect(buildCommandBody({ tool: 'x', params: 42 }).params).toEqual({})
		expect(buildCommandBody({ tool: 'x', params: [1, 2, 3] }).params).toEqual({})
	})

	test('includes unity_instance only when non-empty', () => {
		expect(buildCommandBody({ tool: 'x' }).unity_instance).toBeUndefined()
		expect(buildCommandBody({ tool: 'x', unity_instance: '' }).unity_instance).toBeUndefined()
		expect(buildCommandBody({ tool: 'x', unity_instance: 'MyProject' }).unity_instance).toBe('MyProject')
	})

	test('ignores non-string unity_instance values', () => {
		expect(buildCommandBody({ tool: 'x', unity_instance: 123 }).unity_instance).toBeUndefined()
		expect(buildCommandBody({ tool: 'x', unity_instance: null }).unity_instance).toBeUndefined()
	})

	test('trims tool name and rejects empty', () => {
		expect(buildCommandBody({ tool: '  find_gameobjects  ' }).type).toBe('find_gameobjects')
		expect(() => buildCommandBody({})).toThrow(/Missing required field: tool/)
		expect(() => buildCommandBody({ tool: '   ' })).toThrow(/Missing required field: tool/)
		expect(() => buildCommandBody({ tool: '' })).toThrow(/Missing required field: tool/)
	})
})

// ---------------------------------------------------------------------------
// formatUnreachableError
// ---------------------------------------------------------------------------

describe('formatUnreachableError', () => {
	test('returns parseable JSON with success=false', () => {
		const out = formatUnreachableError('http://127.0.0.1:8080', 'connection refused')
		const parsed = JSON.parse(out)
		expect(parsed.success).toBe(false)
		expect(parsed.error).toContain('http://127.0.0.1:8080')
		expect(parsed.error).toContain('connection refused')
		expect(parsed.error).toContain('Python server')
	})
})

// ---------------------------------------------------------------------------
// postCommand — fetch stub, no real network
// ---------------------------------------------------------------------------

/** Build a minimal Response-like object the helpers can consume. */
function fakeResponse(opts: { ok?: boolean; status?: number; body: string }) {
	return {
		ok: opts.ok ?? true,
		status: opts.status ?? 200,
		text: async () => opts.body,
		json: async () => JSON.parse(opts.body),
	}
}

describe('postCommand', () => {
	const baseBody: CommandBody = { type: 'find_gameobjects', params: { name: 'X' } }

	test('passes through 2xx response body verbatim', async () => {
		const successPayload = JSON.stringify({ success: true, data: [{ id: 1 }] })
		let capturedUrl = ''
		let capturedInit: { method?: string; headers?: Record<string, string>; body?: string } | undefined
		const fetchFn: FetchLike = async (url, init) => {
			capturedUrl = url
			capturedInit = init
			return fakeResponse({ body: successPayload })
		}

		const out = await postCommand(fetchFn, 'http://127.0.0.1:8080', 5_000, baseBody)

		expect(out).toBe(successPayload)
		expect(capturedUrl).toBe('http://127.0.0.1:8080/api/command')
		expect(capturedInit?.method).toBe('POST')
		expect(capturedInit?.headers?.['content-type']).toBe('application/json')
		expect(JSON.parse(capturedInit!.body!)).toEqual(baseBody)
	})

	test('returns server body for non-2xx responses (preserving structured error)', async () => {
		const errPayload = JSON.stringify({ success: false, error: 'No Unity instances connected' })
		const fetchFn: FetchLike = async () => fakeResponse({ ok: false, status: 503, body: errPayload })

		const out = await postCommand(fetchFn, 'http://127.0.0.1:8080', 5_000, baseBody)
		expect(out).toBe(errPayload)
	})

	test('synthesizes an error JSON when non-2xx body is empty', async () => {
		const fetchFn: FetchLike = async () => fakeResponse({ ok: false, status: 500, body: '' })

		const out = await postCommand(fetchFn, 'http://127.0.0.1:8080', 5_000, baseBody)
		const parsed = JSON.parse(out)
		expect(parsed.success).toBe(false)
		expect(parsed.error).toBe('HTTP 500')
	})

	test('returns formatted unreachable error on fetch rejection', async () => {
		const fetchFn: FetchLike = async () => {
			throw new Error('ECONNREFUSED')
		}

		const out = await postCommand(fetchFn, 'http://127.0.0.1:8080', 5_000, baseBody)
		const parsed = JSON.parse(out)
		expect(parsed.success).toBe(false)
		expect(parsed.error).toContain('ECONNREFUSED')
		expect(parsed.error).toContain('http://127.0.0.1:8080')
	})

	test('aborts after the configured timeout', async () => {
		const fetchFn: FetchLike = (_url, init) =>
			new Promise((_resolve, reject) => {
				init?.signal?.addEventListener('abort', () => {
					const err = new Error('aborted')
					err.name = 'AbortError'
					reject(err)
				})
			})

		const start = Date.now()
		const out = await postCommand(fetchFn, 'http://127.0.0.1:8080', 50, baseBody)
		const elapsed = Date.now() - start

		const parsed = JSON.parse(out)
		expect(parsed.success).toBe(false)
		// Should abort close to the 50ms deadline, never instantly (the bug
		// being guarded against would abort at t≈0).
		expect(elapsed).toBeGreaterThanOrEqual(40)
		expect(elapsed).toBeLessThan(2_000)
	})
})

// ---------------------------------------------------------------------------
// listInstances
// ---------------------------------------------------------------------------

describe('listInstances', () => {
	test('returns server body verbatim on success', async () => {
		const payload = JSON.stringify({ success: true, instances: [{ project: 'Demo' }] })
		let capturedUrl = ''
		const fetchFn: FetchLike = async (url) => {
			capturedUrl = url
			return fakeResponse({ body: payload })
		}

		const out = await listInstances(fetchFn, 'http://127.0.0.1:8080')
		expect(out).toBe(payload)
		expect(capturedUrl).toBe('http://127.0.0.1:8080/api/instances')
	})

	test('returns JSON error string on fetch rejection', async () => {
		const fetchFn: FetchLike = async () => {
			throw new Error('boom')
		}

		const out = await listInstances(fetchFn, 'http://127.0.0.1:8080')
		const parsed = JSON.parse(out)
		expect(parsed.success).toBe(false)
		expect(parsed.error).toBe('boom')
	})
})
