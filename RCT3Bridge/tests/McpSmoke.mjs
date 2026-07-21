import { spawn } from "node:child_process";
import { mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import readline from "node:readline";

const root = path.resolve(import.meta.dirname, "..", "..");
const outputDirectory = path.join(root, "RCT3Bridge", "build", "mcp-smoke");
const retailExecutable = process.env.RCT3_RETAIL_EXE_PATH ??
  "E:\\Games\\SteamLibrary\\steamapps\\common\\RollerCoaster Tycoon 3 Complete Edition\\RCT3.exe";
const comparisonMap = process.env.RCT3_COMPARISON_MAP_PATH ?? path.join(
  path.dirname(retailExecutable),
  "Campaigns",
  "Base",
  "Wild",
  "RaidersOfTheLostCoaster.dat"
);
const renderWaitMilliseconds = Number.parseInt(
  process.env.RCT3_COMPARISON_WAIT_MS ?? "15000",
  10
);
if (!Number.isSafeInteger(renderWaitMilliseconds) || renderWaitMilliseconds < 0 ||
    renderWaitMilliseconds > 120000) {
  throw new Error("RCT3_COMPARISON_WAIT_MS must be an integer from 0 to 120000.");
}
await mkdir(outputDirectory, { recursive: true });

const server = spawn("dotnet", [
  "run",
  "--no-build",
  "--project",
  "OpenRCT3.Mcp/OpenRCT3.Mcp.csproj"
], {
  cwd: root,
  stdio: ["pipe", "pipe", "pipe"],
  windowsHide: true
});
server.stderr.on("data", data => process.stderr.write(data));

let nextId = 1;
const pending = new Map();
readline.createInterface({ input: server.stdout }).on("line", line => {
  let message;
  try {
    message = JSON.parse(line);
  } catch {
    return;
  }
  if (message.id == null || !pending.has(message.id)) return;
  const { resolve, reject } = pending.get(message.id);
  pending.delete(message.id);
  if (message.error) reject(new Error(JSON.stringify(message.error)));
  else resolve(message.result);
});

function notify(method, params = {}) {
  server.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", method, params })}\n`);
}

function request(method, params = {}) {
  const id = nextId++;
  server.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id, method, params })}\n`);
  return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
}

async function callTool(name, args = {}) {
  const result = await request("tools/call", { name, arguments: args });
  if (result.isError) {
    const message = result.content.find(item => item.type === "text")?.text ??
      `MCP tool failed: ${name}`;
    throw new Error(message);
  }
  return result;
}

function images(result) {
  return result.content.filter(item => item.type === "image");
}

let openLaunched = false;
let retailLaunched = false;
try {
  await request("initialize", {
    protocolVersion: "2025-06-18",
    capabilities: {},
    clientInfo: { name: "rct3-bridge-smoke", version: "1" }
  });
  notify("notifications/initialized");
  const listed = await request("tools/list");
  const names = new Set(listed.tools.map(tool => tool.name));
  for (const required of [
    "openrct3_launch",
    "retail_rct3_launch",
    "retail_rct3_get_state",
    "retail_rct3_click",
    "retail_rct3_screenshot",
    "rct3_compare_frames",
    "retail_rct3_shutdown"
  ]) {
    if (!names.has(required)) throw new Error(`MCP tool is missing: ${required}`);
  }

  await callTool("openrct3_launch", {
    mapPath: comparisonMap,
    build: false,
    hideUi: true,
    viewportWidth: 1280,
    viewportHeight: 720
  });
  openLaunched = true;
  await callTool("retail_rct3_launch", { buildBridge: false });
  retailLaunched = true;
  await new Promise(resolve => setTimeout(resolve, renderWaitMilliseconds));

  const state = await callTool("retail_rct3_get_state");
  const screenshot = images(await callTool("retail_rct3_screenshot"));
  if (screenshot.length !== 1) throw new Error("Retail screenshot did not return one image.");
  await writeFile(path.join(outputDirectory, "retail.png"), screenshot[0].data, "base64");

  const comparison = images(await callTool("rct3_compare_frames"));
  if (comparison.length !== 4) throw new Error("Comparison did not return four images.");
  for (const [index, name] of ["retail", "openrct3", "side-by-side", "difference"].entries()) {
    await writeFile(path.join(outputDirectory, `${name}.png`), comparison[index].data, "base64");
  }
  console.log(JSON.stringify({
    tools: names.size,
    retail_state: state,
    artifacts: outputDirectory
  }));
} finally {
  if (retailLaunched) await callTool("retail_rct3_shutdown").catch(() => {});
  if (openLaunched) await callTool("openrct3_shutdown").catch(() => {});
  server.stdin.end();
  await new Promise(resolve => {
    server.once("exit", resolve);
    setTimeout(() => {
      server.kill();
      resolve();
    }, 10000).unref();
  });
}
