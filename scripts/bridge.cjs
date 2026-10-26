'use strict';
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const net = require('node:net');
const crypto = require('node:crypto');
const { spawn } = require('node:child_process');

const root = path.resolve(__dirname, '..');
const STATES = new Set(['idle', 'running-right', 'running-left', 'waving', 'jumping', 'failed', 'waiting', 'running', 'review']);
const EVENTS = new Set(['SessionStart', 'UserPromptSubmit', 'PreToolUse', 'PermissionRequest', 'PostToolUse', 'PostToolUseFailure', 'Stop']);
const hash = value => crypto.createHash('sha256').update(value).digest('hex');
const dataDir = path.resolve(process.env.ZCODE_PET_DATA_DIR || path.join(process.env.LOCALAPPDATA || os.tmpdir(), 'ZCodePixelPet'));
const pipeName = process.env.ZCODE_PET_PIPE || `ZCodePet-${hash(dataDir.toLowerCase()).slice(0, 20)}`;
if (!/^[A-Za-z0-9_-]{1,80}$/.test(pipeName)) throw new Error('Invalid companion pipe name');
const pipePath = process.platform === 'win32' ? `\\\\.\\pipe\\${pipeName}` : path.join(os.tmpdir(), `${pipeName}.sock`);

function autoStartEnabled() {
  try { return JSON.parse(fs.readFileSync(path.join(dataDir, 'autostart.json'), 'utf8')).autoStart === true; }
  catch (error) { return error.code === 'ENOENT'; }
}

function setAutoStart(enabled) {
  fs.mkdirSync(dataDir, { recursive: true });
  const destination = path.join(dataDir, 'autostart.json');
  const temporary = `${destination}.${process.pid}.${crypto.randomBytes(4).toString('hex')}.tmp`;
  try {
    fs.writeFileSync(temporary, JSON.stringify({ autoStart: enabled }) + '\n', { mode: 0o600 });
    fs.renameSync(temporary, destination);
  } finally {
    try { fs.unlinkSync(temporary); } catch { /* Already renamed, or cleanup is unavailable. */ }
  }
}

function request(message, timeoutMs = 1200) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(pipePath);
    let response = '';
    let settled = false;
    const finish = (error, value) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      socket.destroy();
      if (error) reject(error); else resolve(value);
    };
    const timer = setTimeout(() => finish(new Error('Companion response timed out')), timeoutMs);
    socket.setEncoding('utf8');
    socket.on('error', error => finish(error));
    socket.on('connect', () => socket.write(JSON.stringify(message) + '\n'));
    socket.on('data', chunk => {
      response += chunk;
      if (Buffer.byteLength(response, 'utf8') > 16384) return finish(new Error('Companion response too large'));
      const lineEnd = response.indexOf('\n');
      if (lineEnd < 0) return;
      try {
        const result = JSON.parse(response.slice(0, lineEnd));
        if (!result || typeof result.ok !== 'boolean') throw new Error('Invalid companion response');
        finish(null, result);
      } catch (error) { finish(error); }
    });
    socket.on('end', () => { if (!settled) finish(new Error('Companion closed without a response')); });
  });
}

let launchPromise;
function spawnCompanion(executable, atlas) {
  // Only explicit MCP commands may launch. Login/desktop launch is independent of
  // the host's process lifetime; do not bypass host cleanup through WMI or a watchdog.
  return new Promise((resolve, reject) => {
    const child = spawn(executable, ['--atlas', atlas, '--pipe', pipeName, '--data-dir', dataDir], {
      detached: true, stdio: 'ignore', shell: false,
    });
    child.once('error', reject);
    child.once('spawn', () => { child.unref(); resolve(); });
  });
}
async function ensureRunning() {
  try { const status = await request({ op: 'status' }, 250); if (status.ok) return status; } catch { /* Probe only. */ }
  if (launchPromise) return launchPromise;
  launchPromise = (async () => {
    if (process.env.ZCODE_PET_NO_SPAWN === '1') throw new Error('Companion is not running');
    if (process.platform !== 'win32') throw new Error('This companion currently supports Windows only');
    const executable = path.join(root, 'bin', 'ZCodePet.exe');
    const atlas = path.join(root, 'assets', 'aemeath-mini', 'spritesheet.png');
    if (!fs.existsSync(executable) || !fs.existsSync(atlas)) throw new Error('Companion binary or atlas missing; build the plugin first');
    fs.mkdirSync(dataDir, { recursive: true });
    await spawnCompanion(executable, atlas);
    const deadline = Date.now() + 5000;
    while (Date.now() < deadline) {
      await new Promise(resolve => setTimeout(resolve, 120));
      try { const status = await request({ op: 'status' }, 300); if (status.ok) return status; } catch { /* Wait for this companion. */ }
    }
    throw new Error('Companion did not respond within five seconds. Check startup-error.json and Windows/security-software notifications; no automatic restart will be attempted.');
  })();
  try { return await launchPromise; } finally { launchPromise = undefined; }
}

function hookState(input) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) return null;
  const event = input.hookEventName || input.hook_event_name;
  const id = input.sessionId || input.session_id;
  if (!EVENTS.has(event) || typeof id !== 'string' || !id || id.length > 1024) return null;
  const tool = input.toolName || input.tool_name;
  const state = {
    SessionStart: 'idle', UserPromptSubmit: 'running', PreToolUse: tool === 'AskUserQuestion' ? 'waiting' : 'running',
    PermissionRequest: 'waiting', PostToolUse: 'running', PostToolUseFailure: 'failed', Stop: 'review',
  }[event];
  // Nothing from prompt/toolInput/toolResponse/cwd is forwarded or persisted.
  return { event, message: { op: 'session', sessionId: hash(id), state } };
}

async function notifyHook(input) {
  const normalized = hookState(input);
  if (!normalized) return { ignored: true };
  if (autoStartEnabled() && (normalized.event === 'SessionStart' || normalized.event === 'UserPromptSubmit')) {
    // A task event must not resurrect a process the user or security software stopped.
    if (autoStartEnabled()) await request({ op: 'show', automatic: true });
  }
  return request(normalized.message, 700);
}

function validateArguments(name, value) {
  const args = value === undefined ? {} : value;
  if (!args || typeof args !== 'object' || Array.isArray(args)) throw new Error('Arguments must be an object');
  const allowed = name === 'pet_set_state' ? ['state', 'durationMs']
    : name === 'pet_configure' ? ['speed', 'scale', 'followPointer', 'reducedMotion'] : [];
  if (Object.keys(args).some(key => !allowed.includes(key))) throw new Error('Unknown argument');
  if (name === 'pet_set_state') {
    if (!STATES.has(args.state)) throw new Error('Unsupported animation state');
    if (Object.hasOwn(args, 'durationMs') && (!Number.isInteger(args.durationMs) || args.durationMs < 1000 || args.durationMs > 30000)) throw new Error('durationMs must be between 1000 and 30000');
  }
  if (name === 'pet_configure') {
    if (!Object.keys(args).length) throw new Error('Provide at least one setting');
    for (const key of ['speed', 'scale']) {
      if (args[key] != null && (typeof args[key] !== 'number' || !Number.isFinite(args[key]) || args[key] < 0.5 || args[key] > 2)) throw new Error(`${key} must be between 0.5 and 2`);
    }
    for (const key of ['followPointer', 'reducedMotion']) {
      if (args[key] != null && typeof args[key] !== 'boolean') throw new Error(`${key} must be a boolean`);
    }
    if (Object.values(args).some(value => value == null)) throw new Error('Settings cannot be null');
  }
  return args;
}

async function callTool(name, value) {
  const names = new Set(['pet_show', 'pet_hide', 'pet_status', 'pet_set_state', 'pet_configure', 'pet_quit']);
  if (!names.has(name)) throw new Error('Unknown pet tool');
  const args = validateArguments(name, value);
  if (name === 'pet_show') setAutoStart(true);
  let preferenceWarning;
  if (name === 'pet_hide' || name === 'pet_quit') {
    try { setAutoStart(false); }
    catch { preferenceWarning = 'Display preference could not be saved; automatic display may remain enabled.'; }
  }
  if (name === 'pet_show' || name === 'pet_set_state' || name === 'pet_configure') await ensureRunning();
  if (name === 'pet_status') {
    try { return await request({ op: 'status' }); } catch (error) {
      if (error.code === 'ENOENT' || error.code === 'ECONNREFUSED') return { ok: true, running: false, visible: false };
      throw error;
    }
  }
  const op = { pet_show: 'show', pet_hide: 'hide', pet_set_state: 'state', pet_configure: 'configure', pet_quit: 'quit' }[name];
  if (name === 'pet_hide' || name === 'pet_quit') {
    try { return await request({ op }); } catch (error) {
      if (error.code === 'ENOENT' || error.code === 'ECONNREFUSED') return { ok: true, running: false, visible: false, ...(preferenceWarning ? { warning: preferenceWarning } : {}) };
      throw error;
    }
  }
  return request({ op, ...(name === 'pet_set_state' ? { durationMs: 5000 } : {}), ...args });
}

module.exports = { root, dataDir, pipeName, pipePath, request, ensureRunning, hookState, notifyHook, validateArguments, callTool, autoStartEnabled, setAutoStart };
