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

// Construct updated PATH with common uv locations
const localAppData = process.env.LOCALAPPDATA || "";
const userProfile = process.env.USERPROFILE || "";
const homeDir = process.env.HOME || process.env.USERPROFILE || "";

const extraPaths =
  process.platform === "win32"
    ? [
        path.join(localAppData, "Programs", "uv"), // uv standalone
        path.join(localAppData, "uv"), // Alternative
        path.join(userProfile, ".cargo", "bin"), // Cargo install
        path.join(localAppData, "bin"),
      ]
    : [
        path.join(homeDir, ".cargo", "bin"),
        path.join(homeDir, ".local", "bin"),
        "/usr/local/bin",
        "/opt/homebrew/bin", // macOS Homebrew
      ];

const newPath =
  extraPaths
    .filter((p) => (p && !p.startsWith(path.sep)) || p.length > 1)
    .join(path.delimiter) +
  path.delimiter +
  (process.env.PATH || "");

// Use uv run with --quiet to minimize noise
const pythonProcess = spawn(
  "uv",
  ["run", "--quiet", "src/main.py", "--transport", "stdio"],
  {
    cwd: serverDir,
    stdio: ["pipe", "pipe", "pipe"],
    shell: true,
    env: {
      ...process.env,
      PATH: newPath, // Inject updated PATH
      PYTHONUNBUFFERED: "1",
      PYTHONIOENCODING: "utf-8",
      ...(process.platform === "win32" && userProfile
        ? { HOME: userProfile }
        : {}),
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
      // Validate JSON by parsing. Result is discarded; we only need to verify it's valid JSON.
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
      if (process.platform === "win32") {
        if (pythonProcess.pid) {
          require("child_process").execSync(
            `taskkill /pid ${pythonProcess.pid} /T /F`
          );
        }
      } else {
        pythonProcess.kill();
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
