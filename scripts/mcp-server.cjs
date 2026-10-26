'use strict';
const { callTool } = require('./bridge.cjs');
const empty = { type: 'object', properties: {}, additionalProperties: false };
const tools = [
  { name: 'pet_show', description: '显示独立的 Aemeath Mini 像素桌宠。', inputSchema: empty },
  { name: 'pet_hide', description: '隐藏桌宠窗口，保留任务状态。', inputSchema: empty },
  { name: 'pet_status', description: '读取桌宠状态，不启动窗口。', inputSchema: empty, annotations: { readOnlyHint: true } },
  { name: 'pet_quit', description: '退出本插件的桌宠进程，不退出 ZCode。', inputSchema: empty },
  { name: 'pet_set_state', description: '临时展示指定动作；到期后恢复 hooks 聚合状态，不代表真实任务状态。', inputSchema: {
    type: 'object', properties: {
      state: { type: 'string', enum: ['idle', 'running-right', 'running-left', 'waving', 'jumping', 'failed', 'waiting', 'running', 'review'] },
      durationMs: { type: 'integer', minimum: 1000, maximum: 30000, default: 5000 },
    }, required: ['state'], additionalProperties: false,
  } },
  { name: 'pet_configure', description: '设置播放速度、大小、鼠标观察或减少动态效果。调速不增加素材帧数。', inputSchema: {
    type: 'object', properties: {
      speed: { type: 'number', minimum: 0.5, maximum: 2 }, scale: { type: 'number', minimum: 0.5, maximum: 2 },
      followPointer: { type: 'boolean' }, reducedMotion: { type: 'boolean' },
    }, minProperties: 1, additionalProperties: false,
  } },
].map(tool => ({ ...tool, annotations: { destructiveHint: false, openWorldHint: false, ...tool.annotations } }));

const reply = value => process.stdout.write(JSON.stringify(value) + '\n');
async function dispatch(message) {
  const idValid = message && (!Object.hasOwn(message, 'id') || message.id === null || typeof message.id === 'string' || (typeof message.id === 'number' && Number.isFinite(message.id)));
  if (!message || Array.isArray(message) || message.jsonrpc !== '2.0' || typeof message.method !== 'string' || !idValid) {
    return { jsonrpc: '2.0', id: idValid ? message?.id ?? null : null, error: { code: -32600, message: 'Invalid Request' } };
  }
  if (!Object.hasOwn(message, 'id')) return null;
  const base = { jsonrpc: '2.0', id: message.id };
  if (message.method === 'initialize') {
    const supported = new Set(['2024-11-05', '2025-03-26', '2025-06-18', '2025-11-25']);
    const asked = message.params?.protocolVersion;
    return { ...base, result: { protocolVersion: supported.has(asked) ? asked : '2024-11-05', capabilities: { tools: {} }, serverInfo: { name: 'zcode-pixel-pet', version: '0.3.6' } } };
  }
  if (message.method === 'ping') return { ...base, result: {} };
  if (message.method === 'tools/list') return { ...base, result: { tools } };
  if (message.method === 'resources/list') return { ...base, result: { resources: [] } };
  if (message.method === 'prompts/list') return { ...base, result: { prompts: [] } };
  if (message.method === 'tools/call') {
    try {
      const result = await callTool(message.params?.name, message.params?.arguments);
      return { ...base, result: { content: [{ type: 'text', text: JSON.stringify(result) }], isError: !result.ok } };
    } catch (error) {
      return { ...base, result: { content: [{ type: 'text', text: error.message }], isError: true } };
    }
  }
  return { ...base, error: { code: -32601, message: 'Method not found' } };
}

if (require.main === module) {
  let pending = '';
  let discarding = false;
  const invalidSize = () => reply({ jsonrpc: '2.0', id: null, error: { code: -32600, message: 'Request too large' } });
  async function processLine(line) {
    try { const result = await dispatch(JSON.parse(line)); if (result) reply(result); }
    catch { reply({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'Parse error' } }); }
  }
  process.stdin.setEncoding('utf8');
  process.stdin.on('data', chunk => {
    if (discarding) {
      const boundary = chunk.indexOf('\n');
      if (boundary < 0) return;
      chunk = chunk.slice(boundary + 1);
      discarding = false;
    }
    pending += chunk;
    let boundary;
    while ((boundary = pending.indexOf('\n')) >= 0) {
      const line = pending.slice(0, boundary);
      pending = pending.slice(boundary + 1);
      if (Buffer.byteLength(line, 'utf8') > 1048576) invalidSize();
      else if (line.trim()) void processLine(line);
    }
    if (Buffer.byteLength(pending, 'utf8') > 1048576) {
      invalidSize(); pending = ''; discarding = true;
    }
  });
  process.stdin.on('end', () => { if (pending.trim()) reply({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'Incomplete JSON-RPC line' } }); });
}
module.exports = { dispatch, tools };
