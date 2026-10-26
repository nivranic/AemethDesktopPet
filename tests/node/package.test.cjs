'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');

test('manifest only declares existing components and an argv-based MCP entry', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, '.zcode-plugin/plugin.json')));
  assert.equal(manifest.name, 'zcode-pixel-pet');
  assert.equal(manifest.version, '0.3.6');
  assert.equal(manifest.hooks, './hooks/pet-events.json');
  assert.ok(fs.existsSync(path.join(root, manifest.hooks)));
  assert.ok(fs.existsSync(path.join(root, manifest.skills, 'pixel-pet/SKILL.md')));
  const server = manifest.mcpServers['zcode-pet'];
  assert.equal(server.command, 'node');
  assert.deepEqual(server.args, ['${ZCODE_PLUGIN_ROOT}/scripts/mcp-server.cjs']);
  assert.ok(fs.existsSync(path.join(root, 'scripts/mcp-server.cjs')));
  assert.equal(fs.existsSync(path.join(root, 'hooks/hooks.json')), false);
});

test('all configured hooks use a real file and process mode without intervention', () => {
  const config = JSON.parse(fs.readFileSync(path.join(root, 'hooks/pet-events.json')));
  assert.deepEqual(Object.keys(config.hooks).sort(), ['SessionStart', 'UserPromptSubmit', 'PreToolUse', 'PermissionRequest', 'PostToolUse', 'PostToolUseFailure', 'Stop'].sort());
  for (const entries of Object.values(config.hooks)) {
    const hook = entries[0].hooks[0];
    assert.equal(hook.type, 'process');
    assert.equal(hook.command, 'node');
    assert.deepEqual(hook.args, ['${ZCODE_PLUGIN_ROOT}/scripts/hook.cjs']);
    assert.ok(hook.timeoutMs >= 1500 && hook.timeoutMs <= 8000);
  }
});

test('packaged PNG geometry and preservation report match V2 atlas', () => {
  const png = fs.readFileSync(path.join(root, 'assets/aemeath-mini/spritesheet.png'));
  assert.equal(png.subarray(1, 4).toString(), 'PNG');
  assert.equal(png.readUInt32BE(16), 1536);
  assert.equal(png.readUInt32BE(20), 2288);
  const report = JSON.parse(fs.readFileSync(path.join(root, 'assets/aemeath-mini/conversion-verification.json')));
  assert.equal(report.allRgbaPixelsIdentical, true);
  assert.equal(report.standardFramesPreserved, 57);
  assert.equal(report.lookCellsPreserved, 16);
});

test('native source keeps explorer restart layers and menu dismissal wiring', () => {
  const source = fs.readFileSync(path.join(root, 'native/Companion.cs'), 'utf8');
  assert.ok(source.includes('--explorer-restart-watch'), 'watchdog headless mode flag');
  assert.ok(source.includes('重启资源管理器'), 'menu entry for shell restart');
  assert.ok(source.includes('RegisterHotKey'), 'Ctrl+Alt+R hotkey registration');
  assert.ok(source.includes('m.Msg==0x312'), 'WM_HOTKEY dispatch');
  assert.ok(source.includes('TerminateSessionExplorers'), 'inline TerminateProcess layer');
  assert.ok(source.includes('Win32_Process'), 'service-hosted WMI fallback layer');
  assert.ok(source.includes('menu.AutoClose=true'), 'menu auto-close enabled');
  assert.ok(source.includes('windowMenu.Show(spawn)'), 'explicit ownerless menu show at a pet-avoiding point');
  assert.ok(!source.includes('\n            ContextMenuStrip=menu;'), 'no-activate form must not auto-show the menu');
  assert.ok(source.includes('ModernMenuRenderer'), 'flat modern menu renderer');
  assert.ok(source.includes('MenuSpawnPoint') && source.includes('AvoidPetOverlap'), 'menu placement avoids the topmost pet');
});
