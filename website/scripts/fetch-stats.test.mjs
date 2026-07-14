import { test } from 'node:test';
import assert from 'node:assert/strict';
import { normalizePypiRecent, buildStats, renderSummary } from './fetch-stats.mjs';

test('normalizePypiRecent extracts day/week/month', () => {
  assert.deepEqual(
    normalizePypiRecent({ data: { last_day: 100, last_week: 700, last_month: 3000 } }),
    { lastDay: 100, lastWeek: 700, lastMonth: 3000 },
  );
});

test('normalizePypiRecent handles missing data', () => {
  assert.deepEqual(normalizePypiRecent({}), { lastDay: 0, lastWeek: 0, lastMonth: 0 });
});

test('normalizePypiRecent marks failed requests as unavailable', () => {
  assert.deepEqual(
    normalizePypiRecent(null),
    { lastDay: null, lastWeek: null, lastMonth: null, unavailable: true },
  );
});

test('buildStats maps github + pypi + web', () => {
  const stats = buildStats({
    recent: { data: { last_day: 1, last_week: 2, last_month: 3 } },
    repo: { stargazers_count: 11102, forks_count: 1229 },
    clones: { uniques: 84 },
    views: { uniques: 530 },
    webPageviews: { total: 42 },
    generatedAt: 't',
  });
  assert.deepEqual(stats.github, { stars: 11102, forks: 1229, uniqueCloners14d: 84, uniqueViewers14d: 530 });
  assert.deepEqual(stats.pypi, { lastDay: 1, lastWeek: 2, lastMonth: 3 });
  assert.deepEqual(stats.web, { total: 42 });
});

test('buildStats nulls github fields when sources unavailable', () => {
  const stats = buildStats({ recent: { data: {} }, generatedAt: 't' });
  assert.deepEqual(stats.github, { stars: null, forks: null, uniqueCloners14d: null, uniqueViewers14d: null });
  assert.equal(stats.web, null);
});

test('renderSummary leads with real-user signals and labels PyPI as inflated', () => {
  const stats = buildStats({
    recent: { data: { last_day: 4912, last_week: 38270, last_month: 192776 } },
    repo: { stargazers_count: 11102, forks_count: 1229 },
    clones: { uniques: 84 },
    views: { uniques: 530 },
    generatedAt: 't',
  });
  const md = renderSummary(stats);
  assert.match(md, /Real-user signals/);
  assert.match(md, /GitHub stars \| 11,102/);
  assert.match(md, /Unique repo cloners \(14d\) \| 84/);
  assert.match(md, /true active users/);            // DAU/WAU flagged first
  assert.match(md, /inflated — NOT a user count/);
  assert.match(md, /Last month \| 192,776/);
});

test('renderSummary notes when the traffic token is missing', () => {
  const stats = buildStats({ recent: { data: {} }, repo: { stargazers_count: 1, forks_count: 0 }, generatedAt: 't' });
  assert.match(renderSummary(stats), /Administration: read/);
});

test('renderSummary distinguishes unavailable PyPI stats from zero downloads', () => {
  const stats = buildStats({ recent: null, repo: { stargazers_count: 1, forks_count: 0 }, generatedAt: 't' });
  const md = renderSummary(stats);
  assert.match(md, /Last day \| —/);
  assert.match(md, /PyPI stats are unavailable/);
  assert.match(md, /Do not treat these dashes as zero downloads/);
});
