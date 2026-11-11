import { describe, expect, test } from 'bun:test';
import { type CheckInput, checkClaims, type ScannedFile } from './core';

const decompileFiles = [
  'src/Game/Game.SceneFlow/GameManager.cs',
  'src/Game/DebugCamera.cs',
  'src/Colossal.Logging/Colossal.Logging/Level.cs',
  'src/Game/Game.Simulation/Level.cs'
];

function check(overrides: Partial<CheckInput>): ReturnType<typeof checkClaims> {
  return checkClaims({
    scanned: [],
    decompileFiles,
    changedFiles: new Set(),
    findDeclarations: () => [],
    ...overrides
  });
}

describe('checkClaims', () => {
  test(`extracts a marker from a Markdown line and reports an untouched claim clean`, () => {
    const claims = check({
      scanned: [
        {
          path: 'skills/cs2-modding/SKILL.md',
          text: `# Traps\n\n- Wrong default, right move. [symbol: Game.SceneFlow.GameManager]\n`
        }
      ]
    });

    expect(claims).toEqual([
      {
        file: 'skills/cs2-modding/SKILL.md',
        line: 3,
        symbol: 'Game.SceneFlow.GameManager',
        state: 'clean',
        files: ['src/Game/Game.SceneFlow/GameManager.cs']
      }
    ]);
  });

  test(`flags a claim whose file the update changed`, () => {
    const claims = check({
      scanned: [markdown('Game.SceneFlow.GameManager')],
      changedFiles: new Set(['src/Game/Game.SceneFlow/GameManager.cs'])
    });

    expect(claims.map(claim => claim.state)).toEqual(['changed']);
  });

  test(`reports a claim resting on no symbol unchecked`, () => {
    const claims = check({ scanned: [markdown('none')] });

    expect(claims).toEqual([
      { file: 'SKILL.md', line: 1, symbol: 'none', state: 'unchecked', files: [] }
    ]);
  });

  test(`reports a claim missing when neither the layout nor a declaration finds its symbol`, () => {
    const claims = check({ scanned: [markdown('Game.SceneFlow.GameMode')] });

    expect(claims.map(claim => [claim.state, claim.files])).toEqual([['missing', []]]);
  });

  test(`resolves a root-namespace type to the file directly under its assembly`, () => {
    const claims = check({ scanned: [markdown('DebugCamera')] });

    expect(claims.map(claim => claim.files)).toEqual([['src/Game/DebugCamera.cs']]);
  });

  test(`resolves a nested type to the file of its outermost type`, () => {
    const claims = check({
      scanned: [markdown('Game.SceneFlow.GameManager.State'), markdown('DebugCamera.Mode')]
    });

    expect(claims.map(claim => claim.files)).toEqual([
      ['src/Game/Game.SceneFlow/GameManager.cs'],
      ['src/Game/DebugCamera.cs']
    ]);
  });

  test(`resolves a name several files share to the file of its own namespace`, () => {
    const claims = check({ scanned: [markdown('Colossal.Logging.Level')] });

    expect(claims.map(claim => claim.files)).toEqual([
      ['src/Colossal.Logging/Colossal.Logging/Level.cs']
    ]);
  });

  test(`flags a type declared across assemblies when any of its files changed`, () => {
    const claims = check({
      scanned: [markdown('Game.Simulation.Level')],
      decompileFiles: [...decompileFiles, 'src/Game.Mods/Game.Simulation/Level.cs'],
      changedFiles: new Set(['src/Game.Mods/Game.Simulation/Level.cs'])
    });

    expect(claims.map(claim => [claim.state, claim.files])).toEqual([
      ['changed', ['src/Game/Game.Simulation/Level.cs', 'src/Game.Mods/Game.Simulation/Level.cs']]
    ]);
  });

  test(`extracts a marker from a source comment`, () => {
    const claims = check({
      scanned: [
        {
          path: 'mcp/src/cycle.ts',
          text: `/**\n * Printed by [symbol: Game.SceneFlow.GameManager].\n */\nconst line = '';\n`
        }
      ]
    });

    expect(claims.map(claim => [claim.file, claim.line, claim.symbol])).toEqual([
      ['mcp/src/cycle.ts', 2, 'Game.SceneFlow.GameManager']
    ]);
  });

  test(`falls back on a declaration in the symbol's namespace folder`, () => {
    const claims = check({
      scanned: [markdown('Game.Prefabs.Handler'), markdown('Handler')],
      findDeclarations: type =>
        type == 'Handler'
          ? ['src/Game/Game.Prefabs/Helpers.cs', 'src/Game/Game.Simulation/Systems.cs']
          : []
    });

    expect(claims.map(claim => [claim.state, claim.files])).toEqual([
      ['clean', ['src/Game/Game.Prefabs/Helpers.cs']],
      ['missing', []]
    ]);
  });
});

function markdown(symbol: string): ScannedFile {
  return { path: 'SKILL.md', text: `- A trap. [symbol: ${symbol}]` };
}
