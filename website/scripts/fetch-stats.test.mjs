import { test } from 'node:test';
import assert from 'node:assert/strict';
import { normalizePypiRecent, buildStats, renderSummary } from './fetch-stats.mjs';

test('normalizePypiRecent extracts day/week/month', () => {
  const recent = { data: { last_day: 100, last_week: 700, last_month: 3000 } };
  assert.deepEqual(normalizePypiRecent(recent), { lastDay: 100, lastWeek: 700, lastMonth: 3000 });
});

test('normalizePypiRecent handles missing data', () => {
  assert.deepEqual(normalizePypiRecent({}), { lastDay: 0, lastWeek: 0, lastMonth: 0 });
});

test('buildStats shapes the unified object', () => {
  const recent = { data: { last_day: 1, last_week: 2, last_month: 3 } };
  const stats = buildStats({ recent, webPageviews: { total: 42 }, generatedAt: '2026-06-27T00:00:00Z' });
  assert.equal(stats.generatedAt, '2026-06-27T00:00:00Z');
  assert.deepEqual(stats.pypi, { lastDay: 1, lastWeek: 2, lastMonth: 3 });
  assert.deepEqual(stats.web, { total: 42 });
});

test('renderSummary formats PyPI windows with thousands separators', () => {
  const stats = buildStats({
    recent: { data: { last_day: 4912, last_week: 38270, last_month: 192776 } },
    webPageviews: null,
    generatedAt: '2026-06-27T00:00:00Z',
  });
  const md = renderSummary(stats);
  assert.match(md, /Last day \| 4,912/);
  assert.match(md, /Last month \| 192,776/);
  assert.match(md, /pending GoatCounter provisioning/);
});

test('renderSummary shows the web total when present', () => {
  const stats = buildStats({ recent: { data: {} }, webPageviews: { total: 1234 }, generatedAt: 't' });
  assert.match(renderSummary(stats), /1,234 total pageviews/);
});
