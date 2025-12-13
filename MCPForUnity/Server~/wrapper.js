const { spawn } = require("child_process");
const path = require("path");
const fs = require("fs");

const LOG_FILE = path.join(__dirname, "wrapper.log");

// Simple logging helper
function log(msg) {
  try {
    fs.appendFileSync(LOG_FILE, new Date().toISOString() + ": " + msg + "\n");
  } catch (e) {
    // Ignore logging errors
  }
}

log("Wrapper started");

const serverDir = __dirname;

// Use uv run with --quiet to minimize noise
// Add --quiet to uv run commands to suppress "resolved ..." messages
const pythonProcess = spawn(
  "uv",
  ["run", "--quiet", "src/main.py", "--transport", "stdio"],
  {
    cwd: serverDir,
    stdio: ["pipe", "pipe", "pipe"],
    shell: true, // Needed for windows command resolution sometimes
    env: {
      ...process.env,
      PYTHONUNBUFFERED: "1",
      PYTHONIOENCODING: "utf-8",
      HOME: process.env.USERPROFILE, // Ensure uv finds home on Windows
    },
  }
);

let buffer = "";

// Handle stdout: Filter non-JSON lines
pythonProcess.stdout.on("data", (data) => {
  buffer += data.toString("utf8");

  let newlineIdx;
  while ((newlineIdx = buffer.indexOf("\n")) !== -1) {
    // Extract line
    let line = buffer.substring(0, newlineIdx).trim();
    // Move buffer forward
    buffer = buffer.substring(newlineIdx + 1);

    if (!line) continue;

    try {
      // Validate JSON. If strictly JSON-RPC, it must be an object.
      // We don't parse fully to save perf, but JSON.parse ensures validity.
      JSON.parse(line);

      // If valid, pass to stdout with a clean newline
      process.stdout.write(line + "\n");
    } catch (e) {
      // If not JSON, it is likely a log message or noise.
      // Redirect to stderr so the client (Antigravity/Cursor) doesn't crash.
      log("FILTERED STDOUT: " + line);
      process.stderr.write("[STDOUT_LOG] " + line + "\n");
    }
  }
});

// Handle stderr: Pass through but log
pythonProcess.stderr.on("data", (data) => {
  const msg = data.toString("utf8");
  log("STDERR: " + msg); // Enabled for debugging
  process.stderr.write(data);
});

pythonProcess.on("error", (err) => {
  log("Failed to spawn process: " + err.message);
  process.exit(1);
});

pythonProcess.on("exit", (code) => {
  log("Python process exited with code " + code);
  process.exit(code || 0);
});

// Forward stdin to python process
process.stdin.pipe(pythonProcess.stdin);

// Cleanup on exit
function cleanup() {
  if (pythonProcess) {
    try {
      pythonProcess.kill();
      if (process.platform === "win32") {
        require("child_process").execSync(
          `taskkill /pid ${pythonProcess.pid} /T /F`
        );
      }
    } catch (e) {
      /* ignore */
    }
  }
}

process.on("SIGINT", () => {
  cleanup();
  process.exit();
});
process.on("SIGTERM", () => {
  cleanup();
  process.exit();
});
