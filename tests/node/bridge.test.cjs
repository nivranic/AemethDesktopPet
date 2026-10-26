'use strict';
const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const net = require('node:net');
const path = require('node:path');
const os = require('node:os');
const { spawn } = require('node:child_process');
const crypto = require('node:crypto');
const fs = require('node:fs');
process.env.ZCODE_PET_PIPE = `ZCodePet-test-${process.pid}-${crypto.randomBytes(5).toString('hex')}`;
process.env.ZCODE_PET_NO_SPAWN = '1';
process.env.ZCODE_PET_DATA_DIR = path.join(os.tmpdir(), process.env.ZCODE_PET_PIPE);
const bridge = require('../../scripts/bridge.cjs');
const { dispatch, tools } = require('../../scripts/mcp-server.cjs');
const seen = [];
const sockets = new Set();
const server = net.createServer(socket => {
  sockets.add(socket);
  socket.on('close', () => sockets.delete(socket));
  let data = '';
  socket.setEncoding('utf8');
  socket.on('data', chunk => {
    data += chunk;
    if (!data.includes('\n')) return;
    const message = JSON.parse(data.slice(0, data.indexOf('\n')));
    seen.push(message);
    if (message.fixture === 'oversized') return socket.end('x'.repeat(17000) + '\n');
    if (message.fixture === 'invalid') return socket.end('{not-json}\n');
    if (message.fixture === 'empty') return socket.end();
    socket.end(JSON.stringify({ ok: true, state: message.state || 'idle', visible: message.op !== 'hide', receivedOp: message.op }) + '\n');
  });
});
before(() => new Promise(resolve => server.listen(bridge.pipePath, resolve)));
after(() => new Promise(resolve => { for (const socket of sockets) socket.destroy(); server.close(resolve); }));

test('hook normalization maps real ZCode aliases and removes all user content', () => {
  const input = { hook_event_name: 'PermissionRequest', session_id: 'private-session-id', prompt: 'sensitive prompt', toolInput: { secret: 'token' }, transcript_path: 'private/path', cwd: 'private-project' };
  const output = bridge.hookState(input);
  assert.equal(output.message.state, 'waiting');
  assert.match(output.message.sessionId, /^[a-f0-9]{64}$/);
  for (const content of ['private-session-id', 'sensitive', 'token', 'private/path', 'private-project']) assert.ok(!JSON.stringify(output).includes(content));
  assert.deepEqual(Object.keys(output.message).sort(), ['op', 'sessionId', 'state']);
  assert.equal(bridge.hookState({ ...input, hook_event_name: 'SessionEnd' }), null);
  assert.equal(bridge.hookState({ hookEventName: 'Stop' }), null);
});

test('wait input, tool recovery, tool failure and normal stop remain distinct', () => {
  const map = event => bridge.hookState({ hookEventName: event, sessionId: 's', toolName: 'AskUserQuestion' }).message.state;
  assert.equal(map('PreToolUse'), 'waiting');
  assert.equal(map('PostToolUse'), 'running');
  assert.equal(map('PostToolUseFailure'), 'failed');
  assert.equal(map('Stop'), 'review');
});

test('tool validation rejects executable/path inputs and invalid settings before IPC', async () => {
  const beforeCount = seen.length;
  for (const args of [{ speed: 0 }, { speed: NaN }, { scale: 3 }, { followPointer: 'true' }, { speed: null }, { executable: 'elsewhere.exe' }, {}]) {
    await assert.rejects(bridge.callTool('pet_configure', args));
  }
  await assert.rejects(bridge.callTool('pet_set_state', { state: 'unknown' }));
  await assert.rejects(bridge.callTool('pet_set_state', { state: 'running', durationMs: 999 }));
  await assert.rejects(bridge.callTool('pet_set_state', { state: 'running', durationMs: null }));
  await assert.rejects(bridge.callTool('pet_show', null));
  await assert.rejects(bridge.callTool('pet_quit', { pid: 123 }));
  assert.equal(seen.length, beforeCount);
});

test('IPC performs a roundtrip and rejects oversized/malformed/incomplete replies', async () => {
  assert.equal((await bridge.request({ op: 'status' })).ok, true);
  await assert.rejects(bridge.request({ fixture: 'oversized' }), /too large/);
  await assert.rejects(bridge.request({ fixture: 'invalid' }));
  await assert.rejects(bridge.request({ fixture: 'empty' }), /without a response/);
  assert.equal((await bridge.request({ op: 'status' })).ok, true);
});

test('MCP dispatch negotiates version, advertises tools and distinguishes invalid requests', async () => {
  const initialized = await dispatch({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-03-26' } });
  assert.equal(initialized.result.protocolVersion, '2025-03-26');
  assert.equal(tools.length, 6);
  assert.equal((await dispatch({ jsonrpc: '2.0', id: 2, method: 'tools/list' })).result.tools.length, 6);
  assert.equal(await dispatch({ jsonrpc: '2.0', method: 'notifications/initialized' }), null);
  assert.equal((await dispatch({ jsonrpc: '2.0', id: 3, method: 'tools/call', params: { name: 'pet_configure', arguments: { speed: 900 } } })).result.isError, true);
  assert.equal((await dispatch({ jsonrpc: '2.0', id: 4, method: 'unknown' })).error.code, -32601);
  assert.equal((await dispatch({ jsonrpc: '2.0', id: { invalid: true }, method: 'ping' })).error.code, -32600);
  assert.equal((await dispatch({ jsonrpc: '2.0', id: 5, method: 'tools/call', params: { name: 'pet_status' } })).result.isError, false);
});

function runChild(script, input) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [path.join(bridge.root, 'scripts', script)], { env: process.env, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    let stdout = '', stderr = '';
    const timer = setTimeout(() => { child.kill(); reject(new Error('Child did not exit')); }, 6000);
    child.stdout.on('data', chunk => stdout += chunk);
    child.stderr.on('data', chunk => stderr += chunk);
    child.on('error', error => { clearTimeout(timer); reject(error); });
    child.on('close', code => { clearTimeout(timer); resolve({ code, stdout, stderr }); });
    child.stdin.end(input);
  });
}

test('actual hook subprocess sends only normalized state and leaves stdout empty', async () => {
  const result = await runChild('hook.cjs', JSON.stringify({ hookEventName: 'PermissionRequest', sessionId: 'secret-id', toolInput: { password: 'DO_NOT_PERSIST' } }) + '\n');
  assert.equal(result.code, 0);
  assert.equal(result.stdout, '');
  assert.equal(result.stderr, '');
  assert.equal(seen.at(-1).state, 'waiting');
  assert.ok(!JSON.stringify(seen).includes('DO_NOT_PERSIST'));
});

test('hook malformed and too-large input fail open without output or side effects', async () => {
  const beforeCount = seen.length;
  for (const input of ['bad json\n', 'x'.repeat(280000)]) {
    const result = await runChild('hook.cjs', input);
    assert.equal(result.code, 0);
    assert.equal(result.stdout, '');
  }
  assert.equal(seen.length, beforeCount);
});

test('actual MCP stdio subprocess answers JSON-RPC and emits no logging', async () => {
  const input = [
    { jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2024-11-05' } },
    { jsonrpc: '2.0', id: 2, method: 'tools/list' },
    { jsonrpc: '2.0', id: 3, method: 'tools/call', params: { name: 'pet_status' } },
  ].map(message => JSON.stringify(message)).join('\n') + '\n';
  const result = await runChild('mcp-server.cjs', input);
  assert.equal(result.code, 0);
  assert.equal(result.stderr, '');
  const responses = result.stdout.trim().split('\n').map(line => JSON.parse(line));
  assert.equal(responses.length, 3);
  assert.equal(responses.find(response => response.id === 2).result.tools.length, 6);
  assert.equal(responses.find(response => response.id === 3).result.isError, false);
});

test('explicit hide/quit prevent subsequent automatic show or relaunch probes', async () => {
  await bridge.callTool('pet_hide');
  assert.equal(bridge.autoStartEnabled(), false);
  let count = seen.length;
  await bridge.notifyHook({ hookEventName: 'SessionStart', sessionId: 'new-session' });
  assert.deepEqual(seen.slice(count).map(message => message.op), ['session']);
  await bridge.callTool('pet_quit');
  count = seen.length;
  await bridge.notifyHook({ hookEventName: 'UserPromptSubmit', sessionId: 'new-session' });
  assert.deepEqual(seen.slice(count).map(message => message.op), ['session']);
  await bridge.callTool('pet_show');
  assert.equal(bridge.autoStartEnabled(), true);
});

test('state demonstrations supply the advertised five-second default', async () => {
  const result = await bridge.callTool('pet_set_state', { state: 'running' });
  assert.equal(result.ok, true);
  assert.deepEqual(seen.at(-1), { op: 'state', durationMs: 5000, state: 'running' });
});

test('preference write failure never prevents a hide or quit request', async () => {
  const original = fs.renameSync;
  const temporaryFiles = [];
  fs.renameSync = (source, destination) => {
    if (destination === path.join(bridge.dataDir, 'autostart.json')) {
      temporaryFiles.push(source);
      throw Object.assign(new Error('Locked preference fixture'), { code: 'EACCES' });
    }
    return original(source, destination);
  };
  try {
    for (const name of ['pet_hide', 'pet_quit']) {
      const result = await bridge.callTool(name);
      assert.equal(result.ok, true);
      assert.equal(seen.at(-1).op, name === 'pet_hide' ? 'hide' : 'quit');
    }
  } finally {
    fs.renameSync = original;
    for (const file of temporaryFiles) if (fs.existsSync(file)) fs.unlinkSync(file);
  }
});

test('a prompt shows an existing companion without a launch probe', async () => {
  bridge.setAutoStart(true);
  const count = seen.length;
  await bridge.notifyHook({ hookEventName: 'UserPromptSubmit', sessionId: 'recovery-session' });
  assert.deepEqual(seen.slice(count).map(message => message.op), ['show', 'session']);
  assert.equal(seen.slice(count)[0].automatic, true);
});

test('task hooks never spawn when the companion is absent', async () => {
  const script = `
    const cp = require('node:child_process');
    let launches = 0;
    cp.spawn = () => { launches++; throw new Error('Unexpected launch'); };
    delete process.env.ZCODE_PET_NO_SPAWN;
    process.env.ZCODE_PET_PIPE += '-absent';
    const b = require(${JSON.stringify(path.join(bridge.root, 'scripts/bridge.cjs'))});
    b.setAutoStart(true);
    (async () => {
      for (const event of ['SessionStart', 'UserPromptSubmit']) {
        try { await b.notifyHook({ hookEventName: event, sessionId: 'missing' }); }
        catch (e) { if (!['ENOENT','ECONNREFUSED'].includes(e.code)) throw e; }
      }
      if (launches !== 0) throw new Error('Task hook launched a process');
    })().catch(e => { console.error(e); process.exitCode = 1; });
  `;
  const result = await new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ['-e', script], { env: process.env, windowsHide: true });
    let stderr = '';
    child.stderr.on('data', chunk => stderr += chunk);
    child.on('error', reject);
    child.on('close', code => resolve({ code, stderr }));
  });
  assert.equal(result.code, 0, result.stderr);
});

test('explicit launch reports spawn failure instead of waiting or retrying', async () => {
  const script = `
    const cp = require('node:child_process');
    const { EventEmitter } = require('node:events');
    let launches = 0;
    cp.spawn = (file, args, options) => {
      launches++;
      if (!file.endsWith('ZCodePet.exe') || options.shell !== false || !Array.isArray(args)) throw new Error('Unexpected launch contract');
      const child = new EventEmitter();
      setImmediate(() => child.emit('error', Object.assign(new Error('Launch denied fixture'), { code: 'EACCES' })));
      return child;
    };
    delete process.env.ZCODE_PET_NO_SPAWN;
    process.env.ZCODE_PET_PIPE += '-launch-failure';
    const b = require(${JSON.stringify(path.join(bridge.root, 'scripts/bridge.cjs'))});
    b.ensureRunning().then(() => { throw new Error('Expected failure'); }, e => {
      if (e.code !== 'EACCES' || launches !== 1) throw e;
    }).catch(e => { console.error(e); process.exitCode = 1; });
  `;
  const result = await new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ['-e', script], { env: process.env, windowsHide: true });
    let stderr = '';
    child.stderr.on('data', chunk => stderr += chunk);
    child.on('error', reject);
    child.on('close', code => resolve({ code, stderr }));
  });
  assert.equal(result.code, 0, result.stderr);
});

test('MCP limits unfinished lines and recovers at the next newline', async () => {
  const input = 'x'.repeat(1100000) + '\n' + JSON.stringify({ jsonrpc: '2.0', id: 99, method: 'ping' }) + '\n';
  const result = await runChild('mcp-server.cjs', input);
  const responses = result.stdout.trim().split('\n').map(line => JSON.parse(line));
  assert.equal(responses[0].error.code, -32600);
  assert.deepEqual(responses.at(-1), { jsonrpc: '2.0', id: 99, result: {} });
});
