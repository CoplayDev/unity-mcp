export function normalizePypiRecent(recent) {
  const d = (recent && recent.data) || {};
  return { lastDay: d.last_day ?? 0, lastWeek: d.last_week ?? 0, lastMonth: d.last_month ?? 0 };
}

export function buildStats({ recent, webPageviews = null, generatedAt }) {
  return { generatedAt, pypi: normalizePypiRecent(recent), web: webPageviews };
}

// Renders the unified stats as a Markdown table. The workflow appends this to
// the GitHub Actions run summary, which is visible only to repo collaborators —
// nothing is published to the public docs site.
export function renderSummary(stats) {
  const p = stats.pypi;
  const n = (x) => Number(x).toLocaleString('en-US');
  const web = stats.web && stats.web.total != null
    ? `${n(stats.web.total)} total pageviews`
    : '_pending GoatCounter provisioning_';
  return [
    '## MCP for Unity — adoption stats (maintainer-only)',
    `_generated ${stats.generatedAt}_`,
    '',
    '### PyPI installs (`mcpforunityserver`)',
    '| Window | Downloads |',
    '| --- | ---: |',
    `| Last day | ${n(p.lastDay)} |`,
    `| Last week | ${n(p.lastWeek)} |`,
    `| Last month | ${n(p.lastMonth)} |`,
    '',
    '> PyPI counts are install events — CI, mirrors, and Docker rebuilds inflate them, so read as a trend, not a user count.',
    '',
    '### Docs traffic',
    web,
    '',
  ].join('\n');
}

async function main() {
  const pkg = 'mcpforunityserver';
  const recent = await (await fetch(`https://pypistats.org/api/packages/${pkg}/recent`)).json();
  let web = null;
  const token = process.env.GOATCOUNTER_TOKEN, site = process.env.GOATCOUNTER_SITE;
  if (token && site) {
    try {
      const r = await fetch(`https://${site}.goatcounter.com/api/v0/stats/total`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (r.ok) { const j = await r.json(); web = { total: j.total ?? null }; }
    } catch { /* leave web=null on failure */ }
  }
  const stats = buildStats({ recent, webPageviews: web, generatedAt: new Date().toISOString() });
  process.stdout.write(renderSummary(stats));
}

import { fileURLToPath } from 'node:url';
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  main().catch((e) => { console.error(e); process.exit(1); });
}
