import { test } from 'node:test';
import assert from 'node:assert/strict';
import { normalizePypiRecent, buildStats } from './fetch-stats.mjs';

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
