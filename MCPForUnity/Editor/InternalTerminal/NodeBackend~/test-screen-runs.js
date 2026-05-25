const assert = require('assert');
const { Terminal } = require('@xterm/headless');

function buildRuns(cells) {
  const row = [];
  let run = null;
  for (let x = 0; x < cells.length; x += 1) {
    const cell = cells[x];
    const width = cell.width;
    if (
      run
      && run.fg === cell.fg
      && run.bg === cell.bg
      && run.flags === cell.flags
      && run.w === 1
      && width === 1
    ) {
      run.text += cell.text;
      continue;
    }

    run = {
      x,
      text: cell.text,
      fg: cell.fg,
      bg: cell.bg,
      flags: cell.flags,
      w: width
    };
    row.push(run);
  }

  return row;
}

const style = { fg: 0xffffff, bg: 0x000000, flags: 0 };
const runs = buildRuns([
  { text: 'a', width: 1, ...style },
  { text: '中', width: 2, ...style },
  { text: ' ', width: 0, ...style },
  { text: 'b', width: 1, ...style }
]);

assert.deepStrictEqual(runs, [
  { x: 0, text: 'a', w: 1, ...style },
  { x: 1, text: '中', w: 2, ...style },
  { x: 2, text: ' ', w: 0, ...style },
  { x: 3, text: 'b', w: 1, ...style }
]);

function collectMouseWheelReports(enableMouseTracking, lines) {
  const terminal = new Terminal({ allowProposedApi: true, cols: 80, rows: 24 });
  const reports = [];
  terminal.onData(data => reports.push(data));

  return new Promise(resolve => {
    const setup = enableMouseTracking ? '\x1b[?1000h\x1b[?1006h' : '';
    terminal.write(setup, () => {
      const mouseService = terminal._core && terminal._core.coreMouseService;
      const wheelUp = lines < 0;
      const event = {
        col: 0,
        row: 0,
        button: wheelUp ? 4 : 5,
        action: wheelUp ? 0 : 1,
        ctrl: false,
        alt: false,
        shift: false
      };

      const sent = mouseService.triggerMouseEvent(event);
      resolve({ sent, reports });
    });
  });
}

(async () => {
  const wheelUp = await collectMouseWheelReports(true, -1);
  assert.strictEqual(wheelUp.sent, true);
  assert.strictEqual(wheelUp.reports.length, 1);
  assert.strictEqual(wheelUp.reports[0], '\x1b[<64;1;1M');

  const wheelDown = await collectMouseWheelReports(true, 1);
  assert.strictEqual(wheelDown.sent, true);
  assert.strictEqual(wheelDown.reports.length, 1);
  assert.strictEqual(wheelDown.reports[0], '\x1b[<65;1;1M');

  const disabled = await collectMouseWheelReports(false, -1);
  assert.strictEqual(disabled.sent, false);
  assert.deepStrictEqual(disabled.reports, []);

  console.log('screen run tests passed');
})();
