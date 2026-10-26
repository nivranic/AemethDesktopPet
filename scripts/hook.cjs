'use strict';
const { notifyHook } = require('./bridge.cjs');
let chunks = [];
let bytes = 0;
let finished = false;
const deadline = setTimeout(() => { finished = true; process.exit(0); }, 7000);
process.stdin.on('data', chunk => {
  bytes += chunk.length;
  if (bytes > 262144) { finished = true; clearTimeout(deadline); process.exit(0); }
  chunks.push(chunk);
});
process.stdin.on('end', async () => {
  if (finished) return;
  finished = true;
  try { await notifyHook(JSON.parse(Buffer.concat(chunks).toString('utf8'))); }
  catch { /* A companion failure must never block an agent task. */ }
  finally { chunks = []; clearTimeout(deadline); }
});
process.stdin.on('error', () => { clearTimeout(deadline); process.exit(0); });
