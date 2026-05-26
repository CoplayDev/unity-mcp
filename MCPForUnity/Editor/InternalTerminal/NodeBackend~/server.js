const fs = require('fs');
const path = require('path');
const os = require('os');
const http = require('http');
const WebSocket = require('ws');
const pty = require('node-pty');
const { Terminal } = require('@xterm/headless');

const args = parseArgs(process.argv.slice(2));
const port = Number(args.port || 0);
const cwd = path.resolve(args.cwd || process.cwd());
const shell = resolveShell(args.shell);
const DEFAULT_FOREGROUND = 0xe8edf2;
const DEFAULT_BACKGROUND = 0x14161a;
const MAX_COLS = 4096;
const MAX_ROWS = 1000;
const MAX_SCROLLBACK = 100000;
const MAX_SCROLL_LINES = 5000;
const MAX_VIEWPORT_Y = 10000000;
const ANSI_COLORS = [
  0x0d0d0d, 0xdb332e, 0x59b85c, 0xdba638,
  0x4080e6, 0xa659c7, 0x40b3b8, 0xd1d6db,
  0x595f6e, 0xff615c, 0x8ce08f, 0xffcc59,
  0x78a8ff, 0xd18cff, 0x73d6d6, 0xffffff
];

if (!port) {
  console.error('Missing required --port argument.');
  process.exit(1);
}

const server = http.createServer((request, response) => {
  if (request.url === '/health') {
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end(JSON.stringify({
      ok: true,
      shell,
      cwd,
      env: {
        UNITY_MCP_INTERNAL_HOST: process.env.UNITY_MCP_INTERNAL_HOST || '',
        UNITY_MCP_INTERNAL_PORT: process.env.UNITY_MCP_INTERNAL_PORT || '',
        UNITY_MCP_INTERNAL_ROLE: process.env.UNITY_MCP_INTERNAL_ROLE || '',
        UNITY_MCP_INTERNAL_CLIENT_ID: process.env.UNITY_MCP_INTERNAL_CLIENT_ID || ''
      }
    }));
    return;
  }

  if (request.url === '/shutdown') {
    response.writeHead(200, { 'content-type': 'application/json' });
    response.end(JSON.stringify({ ok: true }));
    setTimeout(() => process.exit(0), 25);
    return;
  }

  response.writeHead(404);
  response.end();
});
const wss = new WebSocket.Server({ server, path: '/terminal' });
const sockets = new Set();
let session = null;

wss.on('connection', socket => {
  sockets.add(socket);
  const terminalSession = getSession();
  socket.send(JSON.stringify({ type: 'ready', shell: shell.file, cwd }));
  sendScreen(socket, terminalSession.terminal);

  socket.on('message', raw => {
    let message;
    try {
      message = JSON.parse(raw.toString());
    } catch {
      return;
    }

    handleClientMessage(socket, terminalSession, message);
  });

  socket.on('close', () => {
    sockets.delete(socket);
  });
});

function getSession() {
  if (session) {
    return session;
  }

  const terminal = new Terminal({
    cols: 120,
    rows: 34,
    allowProposedApi: true,
    drawBoldTextInBrightColors: true,
    scrollback: MAX_SCROLLBACK,
    windowsPty: process.platform === 'win32' ? { backend: 'conpty' } : undefined
  });
  if (terminal.unicode && terminal.unicode.versions.includes('11')) {
    terminal.unicode.activeVersion = '11';
  }

  const term = pty.spawn(shell.file, shell.args, {
    name: 'xterm-256color',
    cols: terminal.cols,
    rows: terminal.rows,
    cwd,
    env: buildShellEnvironment()
  });

  session = { terminal, term, screenTimer: null };

  terminal.onData(data => {
    term.write(data);
  });

  term.onData(data => {
    terminal.write(data, () => scheduleScreen(session));
  });

  term.onExit(({ exitCode, signal }) => {
    for (const socket of sockets) {
      if (socket.readyState !== WebSocket.OPEN) {
        continue;
      }

      socket.send(JSON.stringify({ type: 'exit', exitCode, signal }));
      socket.close();
    }

    if (session && session.screenTimer) {
      clearTimeout(session.screenTimer);
    }

    session = null;
  });

  return session;
}

function handleClientMessage(socket, terminalSession, message) {
  const { terminal, term } = terminalSession;

  if (message.type === 'key') {
    const sequence = keyToSequence(message, terminal);
    if (sequence) {
      term.write(sequence);
    }
  }

  if (message.type === 'text') {
    const text = String(message.data || '');
    if (text) {
      term.write(text);
    }
  }

  if (message.type === 'paste') {
    const text = String(message.data || '').replace(/\r?\n/g, '\r');
    if (terminal.modes.bracketedPasteMode) {
      term.write(`\x1b[200~${text}\x1b[201~`);
    } else {
      term.write(text);
    }
  }

  if (message.type === 'scroll') {
    handleScroll(socket, terminal, term, clamp(Number(message.lines), -MAX_SCROLL_LINES, MAX_SCROLL_LINES));
  }

  if (message.type === 'mouseWheel') {
    handleMouseWheel(socket, terminalSession, message);
  }

  if (message.type === 'scrollTo') {
    handleScrollTo(socket, terminal, clamp(Number(message.viewportY), 0, MAX_VIEWPORT_Y));
  }

  if (message.type === 'resize') {
    const cols = clamp(Number(message.cols), 2, MAX_COLS);
    const rows = clamp(Number(message.rows), 2, MAX_ROWS);
    terminal.resize(cols, rows);
    term.resize(cols, rows);
    broadcastScreen(terminal);
  }
}

function scheduleScreen(terminalSession) {
  if (terminalSession.screenTimer) {
    return;
  }

  terminalSession.screenTimer = setTimeout(() => {
    terminalSession.screenTimer = null;
    broadcastScreen(terminalSession.terminal);
  }, 16);
}

function broadcastScreen(terminal) {
  for (const socket of sockets) {
    if (socket.readyState === WebSocket.OPEN) {
      sendScreen(socket, terminal);
    }
  }
}

function sendScreen(socket, terminal) {
  if (socket.readyState !== WebSocket.OPEN) {
    return;
  }

  const buffer = terminal.buffer.active;
  const cursorViewportY = buffer.baseY + buffer.cursorY - buffer.viewportY;
  const cursorVisible = cursorViewportY >= 0 && cursorViewportY < terminal.rows;
  const runs = [];
  for (let y = 0; y < terminal.rows; y += 1) {
    const line = buffer.getLine(buffer.viewportY + y);
    const row = [];
    let run = null;
    for (let x = 0; x < terminal.cols; x += 1) {
      const cell = line && line.getCell(x);
      const text = cell ? cell.getChars() || ' ' : ' ';
      const width = cell ? clamp(Number(cell.getWidth()), 0, 2) : 1;
      const style = cell ? resolveStyle(cell) : { foreground: DEFAULT_FOREGROUND, background: DEFAULT_BACKGROUND };
      const flags = cell
        ? (cell.isBold() ? 1 : 0)
          | (cell.isItalic() ? 2 : 0)
          | (cell.isUnderline() ? 4 : 0)
          | (cell.isInverse() ? 8 : 0)
          | (cell.isInvisible() ? 16 : 0)
        : 0;

      if (
        run
        && run.fg === style.foreground
        && run.bg === style.background
        && run.flags === flags
        && run.w === 1
        && width === 1
      ) {
        run.text += text;
        continue;
      }

      run = {
        x,
        text,
        fg: style.foreground,
        bg: style.background,
        w: width,
        flags
      };
      row.push(run);
    }
    runs.push(row);
  }

  socket.send(JSON.stringify({
    type: 'screen',
    cols: terminal.cols,
    rows: terminal.rows,
    cursorX: buffer.cursorX,
    cursorY: cursorVisible ? cursorViewportY : -1,
    cursorVisible,
    viewportY: buffer.viewportY,
    baseY: buffer.baseY,
    bufferLength: buffer.length,
    alternate: buffer.type === 'alternate',
    runs
  }));
}

function handleScroll(socket, terminal, term, lines) {
  if (!lines) {
    return;
  }

  if (terminal.buffer.active.type === 'alternate') {
    const sequence = lines > 0 ? '\x1b[B' : '\x1b[A';
    for (let index = 0; index < Math.min(50, Math.abs(lines)); index += 1) {
      term.write(sequence);
    }
    return;
  }

  terminal.scrollLines(lines);
  sendScreen(socket, terminal);
}

function buildShellEnvironment() {
  const env = {
    ...process.env,
    TERM: 'xterm-256color',
    COLORTERM: 'truecolor'
  };

  if (process.platform === 'win32') {
    ensureWindowsEnvironment(env);
  }

  return env;
}

function handleMouseWheel(socket, terminalSession, message) {
  const { terminal, term } = terminalSession;
  const lines = clamp(Number(message.lines), -MAX_SCROLL_LINES, MAX_SCROLL_LINES);
  if (!lines) {
    return;
  }

  if (terminal.modes.mouseTrackingMode !== 'none'
    && triggerMouseWheel(terminal, message, lines)) {
    return;
  }

  handleScroll(socket, terminal, term, lines);
}

function triggerMouseWheel(terminal, message, lines) {
  const mouseService = terminal._core && terminal._core.coreMouseService;
  if (!mouseService || typeof mouseService.triggerMouseEvent !== 'function') {
    return false;
  }

  const col = clamp(Number(message.col), 0, Math.max(0, terminal.cols - 1));
  const row = clamp(Number(message.row), 0, Math.max(0, terminal.rows - 1));
  const wheelUp = lines < 0;
  const event = {
    col,
    row,
    button: wheelUp ? 4 : 5,
    action: wheelUp ? 0 : 1,
    ctrl: Boolean(message.ctrl),
    alt: Boolean(message.alt),
    shift: Boolean(message.shift)
  };

  let sent = false;
  for (let index = 0; index < Math.min(50, Math.abs(lines)); index += 1) {
    sent = mouseService.triggerMouseEvent(event) || sent;
  }

  return sent;
}

function handleScrollTo(socket, terminal, viewportY) {
  const buffer = terminal.buffer.active;
  if (buffer.type === 'alternate') {
    return;
  }

  const target = clamp(viewportY, 0, Math.max(0, buffer.baseY));
  terminal.scrollToLine(target);
  sendScreen(socket, terminal);
}

function resolveStyle(cell) {
  let foreground = resolveColor(cell, true);
  let background = resolveColor(cell, false);

  if (cell.isBold() && cell.isFgPalette()) {
    const color = cell.getFgColor();
    if (color >= 0 && color < 8) {
      foreground = ANSI_COLORS[color + 8];
    }
  }

  if (cell.isInverse()) {
    return { foreground: background, background: foreground };
  }

  return { foreground, background };
}

function resolveColor(cell, foreground) {
  if (foreground ? cell.isFgDefault() : cell.isBgDefault()) {
    return foreground ? DEFAULT_FOREGROUND : DEFAULT_BACKGROUND;
  }

  if (foreground ? cell.isFgRGB() : cell.isBgRGB()) {
    return foreground ? cell.getFgColor() : cell.getBgColor();
  }

  const color = foreground ? cell.getFgColor() : cell.getBgColor();
  return paletteToRgb(color);
}

function paletteToRgb(code) {
  code = clamp(Number(code), 0, 255);
  if (code < 16) {
    return ANSI_COLORS[code];
  }

  if (code >= 232) {
    const value = 8 + (code - 232) * 10;
    return (value << 16) | (value << 8) | value;
  }

  const colorIndex = code - 16;
  const r = Math.floor(colorIndex / 36);
  const g = Math.floor(colorIndex / 6) % 6;
  const b = colorIndex % 6;
  return (paletteChannel(r) << 16) | (paletteChannel(g) << 8) | paletteChannel(b);
}

function paletteChannel(value) {
  return value === 0 ? 0 : 55 + value * 40;
}

function keyToSequence(message, terminal) {
  const key = String(message.key || '');
  const text = String(message.text || '');
  const ctrl = Boolean(message.ctrl);
  const alt = Boolean(message.alt);
  const shift = Boolean(message.shift);

  let sequence = '';
  if (ctrl) {
    sequence = controlSequence(key, text);
    if (sequence) {
      return alt ? `\x1b${sequence}` : sequence;
    }
  }

  if (text && text !== '\0' && !isControlText(text)) {
    sequence = text;
  } else {
    sequence = specialKeySequence(key, terminal, shift, alt, ctrl);
  }

  return sequence && alt && !sequence.startsWith('\x1b') ? `\x1b${sequence}` : sequence;
}

function controlSequence(key, text) {
  const upper = key.length === 1 ? key.toUpperCase() : key;
  if (upper >= 'A' && upper <= 'Z') {
    return String.fromCharCode(upper.charCodeAt(0) - 64);
  }

  if (text === ' ' || key === 'Space') return '\x00';
  if (text === '[' || key === 'LeftBracket') return '\x1b';
  if (text === '\\' || key === 'Backslash') return '\x1c';
  if (text === ']' || key === 'RightBracket') return '\x1d';
  if (text === '^' || key === 'Caret') return '\x1e';
  if (text === '_' || key === 'Underscore') return '\x1f';
  return '';
}

function specialKeySequence(key, terminal, shift, alt, ctrl) {
  switch (key) {
    case 'Return':
    case 'KeypadEnter':
    case 'Enter':
      return '\r';
    case 'Backspace':
      return '\x7f';
    case 'Tab':
      return shift ? '\x1b[Z' : '\t';
    case 'Escape':
      return '\x1b';
    case 'UpArrow':
      return cursorSequence('A', terminal, shift, alt, ctrl);
    case 'DownArrow':
      return cursorSequence('B', terminal, shift, alt, ctrl);
    case 'RightArrow':
      return cursorSequence('C', terminal, shift, alt, ctrl);
    case 'LeftArrow':
      return cursorSequence('D', terminal, shift, alt, ctrl);
    case 'Home':
      return homeEndSequence('H', terminal, shift, alt, ctrl);
    case 'End':
      return homeEndSequence('F', terminal, shift, alt, ctrl);
    case 'Insert':
      return modifiedTildeSequence(2, shift, alt, ctrl);
    case 'Delete':
      return modifiedTildeSequence(3, shift, alt, ctrl);
    case 'PageUp':
      return modifiedTildeSequence(5, shift, alt, ctrl);
    case 'PageDown':
      return modifiedTildeSequence(6, shift, alt, ctrl);
    default:
      return '';
  }
}

function cursorSequence(final, terminal, shift, alt, ctrl) {
  const modifier = modifierValue(shift, alt, ctrl);
  if (modifier > 1) {
    return `\x1b[1;${modifier}${final}`;
  }

  return terminal.modes.applicationCursorKeysMode ? `\x1bO${final}` : `\x1b[${final}`;
}

function homeEndSequence(final, terminal, shift, alt, ctrl) {
  const modifier = modifierValue(shift, alt, ctrl);
  if (modifier > 1) {
    return `\x1b[1;${modifier}${final}`;
  }

  return terminal.modes.applicationCursorKeysMode ? `\x1bO${final}` : `\x1b[${final}`;
}

function modifiedTildeSequence(number, shift, alt, ctrl) {
  const modifier = modifierValue(shift, alt, ctrl);
  return modifier > 1 ? `\x1b[${number};${modifier}~` : `\x1b[${number}~`;
}

function modifierValue(shift, alt, ctrl) {
  return 1 + (shift ? 1 : 0) + (alt ? 2 : 0) + (ctrl ? 4 : 0);
}

function isControlText(text) {
  return text.length === 1 && text.charCodeAt(0) < 32;
}

server.listen(port, '127.0.0.1', () => {
  console.log(`WTL Internal Terminal listening at http://127.0.0.1:${port}`);
  console.log(`Shell: ${shell.file} ${shell.args.join(' ')}`);
  console.log(`Working directory: ${cwd}`);
});

function parseArgs(argv) {
  const result = {};
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (!token.startsWith('--')) {
      continue;
    }

    const key = token.slice(2);
    const next = argv[index + 1];
    if (next && !next.startsWith('--')) {
      result[key] = next;
      index += 1;
    } else {
      result[key] = true;
    }
  }

  return result;
}

function defaultShell() {
  if (process.platform === 'win32') {
    const pwsh = findExecutable('pwsh.exe');
    if (pwsh) {
      return { file: pwsh, args: ['-NoLogo'] };
    }

    const powershell = findExecutable('powershell.exe');
    if (powershell) {
      return { file: powershell, args: ['-NoLogo'] };
    }

    const cmd = findExecutable('cmd.exe') || process.env.ComSpec || 'cmd.exe';
    return { file: cmd, args: ['/K', 'chcp.com 65001 > nul'] };
  }

  return {
    file: process.env.SHELL || '/bin/bash',
    args: ['-l']
  };
}

function resolveShell(shellPath) {
  if (shellPath) {
    return { file: String(shellPath), args: [] };
  }

  return defaultShell();
}

function clamp(value, min, max) {
  if (!Number.isFinite(value)) {
    return min;
  }

  return Math.min(max, Math.max(min, Math.floor(value)));
}

function findOnPath(executable) {
  return Boolean(findExecutable(executable));
}

function findExecutable(executable) {
  for (const directory of getSearchDirectories()) {
    if (!directory) {
      continue;
    }

    try {
      const candidate = path.join(directory, executable);
      if (fs.existsSync(candidate)) {
        return candidate;
      }
    } catch {
      // Ignore invalid PATH entries.
    }
  }

  return null;
}

function getSearchDirectories() {
  const directories = [];
  const seen = new Set();
  addPathEntries(directories, seen, process.env.PATH || process.env.Path || process.env.path || '');

  if (process.platform === 'win32') {
    const systemRoot = process.env.SystemRoot || process.env.windir || 'C:\\Windows';
    addDirectory(directories, seen, process.env.ComSpec ? path.dirname(process.env.ComSpec) : '');
    addDirectory(directories, seen, path.join(systemRoot, 'System32'));
    addDirectory(directories, seen, path.join(systemRoot, 'System32', 'WindowsPowerShell', 'v1.0'));
    addDirectory(directories, seen, path.join(process.env.LOCALAPPDATA || '', 'Microsoft', 'WindowsApps'));
    addDirectory(directories, seen, path.join(process.env.ProgramFiles || 'C:\\Program Files', 'nodejs'));
    addDirectory(directories, seen, path.join(process.env.APPDATA || '', 'npm'));
  }

  return directories;
}

function addPathEntries(directories, seen, value) {
  for (const entry of String(value || '').split(path.delimiter)) {
    addDirectory(directories, seen, entry);
  }
}

function addDirectory(directories, seen, directory) {
  if (!directory) {
    return;
  }

  const normalized = path.resolve(directory);
  const key = process.platform === 'win32' ? normalized.toLowerCase() : normalized;
  if (!seen.has(key)) {
    seen.add(key);
    directories.push(normalized);
  }
}

function ensureWindowsEnvironment(env) {
  const systemRoot = env.SystemRoot || env.windir || 'C:\\Windows';
  env.SystemRoot = systemRoot;
  env.windir = env.windir || systemRoot;
  env.ComSpec = env.ComSpec || path.join(systemRoot, 'System32', 'cmd.exe');
  env.PATHEXT = env.PATHEXT || '.COM;.EXE;.BAT;.CMD;.VBS;.VBE;.JS;.JSE;.WSF;.WSH;.MSC;.CPL';

  const pathValue = env.PATH || env.Path || env.path || '';
  const directories = [];
  const seen = new Set();
  addPathEntries(directories, seen, pathValue);
  addDirectory(directories, seen, path.join(systemRoot, 'System32'));
  addDirectory(directories, seen, systemRoot);
  addDirectory(directories, seen, path.join(systemRoot, 'System32', 'Wbem'));
  addDirectory(directories, seen, path.join(systemRoot, 'System32', 'WindowsPowerShell', 'v1.0'));
  addDirectory(directories, seen, path.join(systemRoot, 'System32', 'OpenSSH'));
  addDirectory(directories, seen, path.join(process.env.ProgramFiles || 'C:\\Program Files', 'nodejs'));
  addDirectory(directories, seen, path.join(env.APPDATA || path.join(os.homedir(), 'AppData', 'Roaming'), 'npm'));

  const updatedPath = directories.join(path.delimiter);
  env.PATH = updatedPath;
  env.Path = updatedPath;
}
