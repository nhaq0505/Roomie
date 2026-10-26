import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import vm from 'node:vm';
import { build } from 'esbuild';

const require = createRequire(import.meta.url);

// Bundle getInitialDates from dates.ts
const bundle = await build({
  stdin: {
    contents: 'export { getInitialDates } from "./src/features/admin/monitoring/dates";',
    resolveDir: process.cwd(),
    loader: 'ts',
  },
  bundle: true,
  write: false,
  format: 'cjs',
  platform: 'node',
  external: ['antd', '@ant-design/icons', 'react', 'react-dom'],
});

const module = { exports: {} };
vm.runInNewContext(bundle.outputFiles[0].text, {
  module,
  exports: module.exports,
  require,
  Intl,
  Date,
  console,
});

const { getInitialDates } = module.exports;

// Test 1: Midday UTC corresponds to 19:00 in Vietnam (UTC+7)
const middayUtc = new Date('2026-10-20T12:00:00Z');
const [from1, to1] = getInitialDates(middayUtc);
assert.equal(to1, '2026-10-20', 'today in Vietnam');
assert.equal(from1, '2026-09-21', '29 days earlier in Vietnam calendar');

// Test 2: Early morning UTC (01:00 UTC = 08:00 AM Vietnam, but 21:00 previous day in New York UTC-4)
// In a negative timezone (e.g. UTC-4 / UTC-5), local system date is Oct 19,
// but Vietnam calendar is already Oct 20!
const morningVietnam = new Date('2026-10-20T01:00:00Z');
const [from2, to2] = getInitialDates(morningVietnam);
assert.equal(to2, '2026-10-20', 'Vietnam date correctly identified regardless of local negative timezone');
assert.equal(from2, '2026-09-21', '30-day window correctly computed without off-by-one error');

// Test 3: Leap year boundary handling in Vietnam calendar (e.g. 2028-03-01)
const leapYearDate = new Date('2028-03-01T04:00:00Z');
const [from3, to3] = getInitialDates(leapYearDate);
assert.equal(to3, '2028-03-01');
assert.equal(from3, '2028-02-01', 'subtracting 29 days in leap year lands on Feb 1');

// Test 4: Verify exactly 30 calendar days span (inclusive)
const [from4, to4] = getInitialDates(new Date('2026-10-20T10:00:00Z'));
const daysSpan = Math.round((new Date(to4).getTime() - new Date(from4).getTime()) / (1000 * 60 * 60 * 24)) + 1;
assert.equal(daysSpan, 30, 'inclusive 30-day window');

console.log('PASS: getInitialDates Vietnam calendar and negative timezone resilience verified');
