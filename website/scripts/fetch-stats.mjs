export function normalizePypiRecent(recent) {
  const d = (recent && recent.data) || {};
  return { lastDay: d.last_day ?? 0, lastWeek: d.last_week ?? 0, lastMonth: d.last_month ?? 0 };
}

export function buildStats({ recent, webPageviews = null, generatedAt }) {
  return { generatedAt, pypi: normalizePypiRecent(recent), web: webPageviews };
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
  const fs = await import('node:fs/promises');
  const path = await import('node:path');
  const here = path.dirname(fileURLToPath(import.meta.url));
  const out = path.join(here, '..', 'static', 'stats', 'data.json');
  await fs.mkdir(path.dirname(out), { recursive: true });
  await fs.writeFile(out, JSON.stringify(stats, null, 2) + '\n');
  console.log(`Wrote ${out}`);
}

import { fileURLToPath } from 'node:url';
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  main().catch((e) => { console.error(e); process.exit(1); });
}
