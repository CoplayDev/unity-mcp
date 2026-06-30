const PKG = 'mcpforunityserver';
const REPO = 'CoplayDev/unity-mcp';
const GH = 'https://api.github.com';

export function normalizePypiRecent(recent) {
  if (recent == null) {
    return { lastDay: null, lastWeek: null, lastMonth: null, unavailable: true };
  }
  const d = (recent && recent.data) || {};
  return { lastDay: d.last_day ?? 0, lastWeek: d.last_week ?? 0, lastMonth: d.last_month ?? 0 };
}

// Builds the unified stats object. Honest user signals (GitHub clones/stars,
// and the in-product DAU/WAU surfaced elsewhere) sit alongside the noisy PyPI
// download proxy, which renderSummary clearly labels as inflated.
export function buildStats({ recent, repo = null, clones = null, views = null, webPageviews = null, generatedAt }) {
  return {
    generatedAt,
    github: {
      stars: repo?.stargazers_count ?? null,
      forks: repo?.forks_count ?? null,
      uniqueCloners14d: clones?.uniques ?? null,
      uniqueViewers14d: views?.uniques ?? null,
    },
    pypi: normalizePypiRecent(recent),
    web: webPageviews,
  };
}

export function renderSummary(stats) {
  const g = stats.github, p = stats.pypi;
  const n = (x) => (x == null ? '—' : Number(x).toLocaleString('en-US'));
  const web = stats.web && stats.web.total != null
    ? `${n(stats.web.total)} total pageviews`
    : '_pending GoatCounter provisioning_';
  const trafficMissing = g.uniqueCloners14d == null && g.uniqueViewers14d == null;
  return [
    '## MCP for Unity — adoption (maintainer-only)',
    `_generated ${stats.generatedAt}_`,
    '',
    '### Real-user signals (the honest ones)',
    '| Signal | Value | Reads as |',
    '| --- | ---: | --- |',
    `| In-product DAU / WAU | _pending_ | **true active users** — already collected by the in-product telemetry; needs a Coplay read API |`,
    `| Unique repo cloners (14d) | ${n(g.uniqueCloners14d)} | developers actually pulling the code |`,
    `| Unique repo viewers (14d) | ${n(g.uniqueViewers14d)} | distinct repo visitors |`,
    `| GitHub stars | ${n(g.stars)} | real accounts interested |`,
    `| GitHub forks | ${n(g.forks)} | |`,
    ...(trafficMissing
      ? ['', '> Cloners/viewers are blank: the workflow needs a maintainer PAT with **Administration: read** (`STATS_GITHUB_TOKEN`). See the external-analytics doc.']
      : []),
    '',
    '### Reach proxy (inflated — NOT a user count)',
    '| Window | PyPI downloads |',
    '| --- | ---: |',
    `| Last day | ${n(p.lastDay)} |`,
    `| Last week | ${n(p.lastWeek)} |`,
    `| Last month | ${n(p.lastMonth)} |`,
    '',
    ...(p.unavailable
      ? ['> PyPI stats are unavailable: the pypistats.org request failed. Do not treat these dashes as zero downloads.', '']
      : []),
    '> PyPI counts are install events — CI, mirrors, `uvx` re-fetches, and Docker rebuilds inflate them heavily. Treat as a trend line, not people.',
    '',
    '### Docs traffic',
    web,
    '',
  ].join('\n');
}

async function main() {
  const get = async (url, headers) => {
    try { const r = await fetch(url, { headers }); return r.ok ? await r.json() : null; } catch { return null; }
  };
  const ghToken = process.env.STATS_GITHUB_TOKEN || process.env.GITHUB_TOKEN;
  const ghHeaders = {
    'User-Agent': 'mcp-for-unity-stats',
    Accept: 'application/vnd.github+json',
    ...(ghToken ? { Authorization: `Bearer ${ghToken}` } : {}),
  };

  const recent = await get(`https://pypistats.org/api/packages/${PKG}/recent`, { Accept: 'application/json' });
  const repo = await get(`${GH}/repos/${REPO}`, ghHeaders);
  const clones = await get(`${GH}/repos/${REPO}/traffic/clones`, ghHeaders);
  const views = await get(`${GH}/repos/${REPO}/traffic/views`, ghHeaders);

  let web = null;
  const gcToken = process.env.GOATCOUNTER_TOKEN, gcSite = process.env.GOATCOUNTER_SITE;
  if (gcToken && gcSite) {
    const j = await get(`https://${gcSite}.goatcounter.com/api/v0/stats/total`, { Authorization: `Bearer ${gcToken}` });
    if (j) web = { total: j.total ?? null };
  }

  const stats = buildStats({ recent, repo, clones, views, webPageviews: web, generatedAt: new Date().toISOString() });
  process.stdout.write(renderSummary(stats));
}

import { fileURLToPath } from 'node:url';
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  main().catch((e) => { console.error(e); process.exit(1); });
}
