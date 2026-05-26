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

function dispatchInternalMouseWheel(terminal, lines) {
  if (terminal.modes.mouseTrackingMode !== 'none') {
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

    if (mouseService.triggerMouseEvent(event)) {
      return;
    }
  }

  terminal.scrollLines(lines);
}

function verifyNormalBufferMouseTrackingReceivesWheel() {
  const terminal = new Terminal({ allowProposedApi: true, cols: 80, rows: 4, scrollback: 100 });
  const reports = [];
  terminal.onData(data => reports.push(data));

  return new Promise(resolve => {
    terminal.write('\x1b[?1000h\x1b[?1006h1\r\n2\r\n3\r\n4\r\n5\r\n6\r\n', () => {
      assert.strictEqual(terminal.buffer.active.type, 'normal');
      const before = terminal.buffer.active.viewportY;
      dispatchInternalMouseWheel(terminal, -3);
      assert.strictEqual(terminal.buffer.active.viewportY, before);
      assert.deepStrictEqual(reports, ['\x1b[<64;1;1M']);
      resolve();
    });
  });
}

function verifyNormalBufferWithoutMouseTrackingScrollsBack() {
  const terminal = new Terminal({ allowProposedApi: true, cols: 80, rows: 4, scrollback: 100 });
  const reports = [];
  terminal.onData(data => reports.push(data));

  return new Promise(resolve => {
    terminal.write('1\r\n2\r\n3\r\n4\r\n5\r\n6\r\n', () => {
      assert.strictEqual(terminal.buffer.active.type, 'normal');
      const before = terminal.buffer.active.viewportY;
      dispatchInternalMouseWheel(terminal, -3);
      assert(terminal.buffer.active.viewportY < before);
      assert.deepStrictEqual(reports, []);
      resolve();
    });
  });
}

function verifyCodexStyleNormalBufferMouseTrackingReceivesWheel() {
  const terminal = new Terminal({ allowProposedApi: true, cols: 20, rows: 6, scrollback: 100 });
  const reports = [];
  terminal.onData(data => reports.push(data));

  return new Promise(resolve => {
    terminal.write('\x1b[?1000h\x1b[?1006hPROMPT> codex', () => {
      assert.strictEqual(terminal.buffer.active.type, 'normal');
      assert.strictEqual(terminal.buffer.active.baseY, 0);
      const before = terminal.buffer.active.viewportY;
      dispatchInternalMouseWheel(terminal, -3);
      assert.strictEqual(terminal.buffer.active.viewportY, before);
      assert.deepStrictEqual(reports, ['\x1b[<64;1;1M']);
      resolve();
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

  await verifyNormalBufferMouseTrackingReceivesWheel();
  await verifyNormalBufferWithoutMouseTrackingScrollsBack();
  await verifyCodexStyleNormalBufferMouseTrackingReceivesWheel();

  console.log('screen run tests passed');
})();
