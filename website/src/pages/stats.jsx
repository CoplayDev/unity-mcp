import React, { useEffect, useState } from 'react';
import Layout from '@theme/Layout';
import useBaseUrl from '@docusaurus/useBaseUrl';

function Bar({ label, value, max }) {
  const pct = max > 0 ? Math.round((value / max) * 100) : 0;
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 12, margin: '6px 0' }}>
      <div style={{ width: 120 }}>{label}</div>
      <div style={{ flex: 1, background: 'var(--ifm-color-emphasis-200)', borderRadius: 4 }}>
        <div style={{ width: `${pct}%`, background: 'var(--ifm-color-primary)', height: 18, borderRadius: 4 }} />
      </div>
      <div style={{ width: 90, textAlign: 'right' }}>{(value || 0).toLocaleString()}</div>
    </div>
  );
}

export default function Stats() {
  const dataUrl = useBaseUrl('/stats/data.json');
  const [stats, setStats] = useState(null);
  const [error, setError] = useState(false);
  useEffect(() => {
    fetch(dataUrl).then((r) => r.json()).then(setStats).catch(() => setError(true));
  }, [dataUrl]);
  return (
    <Layout title="Stats" description="MCP for Unity adoption — PyPI installs and docs traffic (anonymous, aggregate)">
      <main className="container margin-vert--lg">
        <h1>Adoption stats</h1>
        <p>Anonymous, aggregate numbers only — PyPI installs of <code>mcpforunityserver</code> and docs traffic. No personal data.</p>
        {error && <p>Stats are being generated — check back soon.</p>}
        {stats && (
          <>
            <h2>PyPI installs</h2>
            <Bar label="Last day" value={stats.pypi.lastDay} max={stats.pypi.lastMonth} />
            <Bar label="Last week" value={stats.pypi.lastWeek} max={stats.pypi.lastMonth} />
            <Bar label="Last month" value={stats.pypi.lastMonth} max={stats.pypi.lastMonth} />
            <h2>Docs traffic</h2>
            {stats.web ? <p>{(stats.web.total || 0).toLocaleString()} total pageviews</p>
                       : <p>Web analytics pending account provisioning.</p>}
            <p style={{ opacity: 0.6, marginTop: 24 }}>Last updated: {stats.generatedAt}</p>
          </>
        )}
      </main>
    </Layout>
  );
}
