// The init.ps1 branding parameters (-PrimaryColor, -LogoPath, -NameEn, -NameAr): they write the "branding" block of
// frontend/starter-web/public/config.json, keep the rest of the file byte for byte, copy the logo, refuse invalid
// values without changing anything, and, with -Minimal, run only after the fork step succeeds and commit separately.
// Each test runs the real init.ps1 in a scratch copy holding only what the branding step needs.
// Run: node --test tools/fork/tests/branding.test.mjs
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, it } from 'node:test';
import { makeTempDir, runScript, testEnvironment } from './support/powershell.mjs';

const REPO_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const CONFIG = 'frontend/starter-web/public/config.json';
const LOGO_DIR = 'frontend/starter-web/public/branding';
const COPIED_FILES = ['init.ps1', 'tools/fork/lib/ForkCommon.ps1', 'tools/fork/lib/prefix.mjs', 'tools/fork/lib/constants.mjs'];
const EXIT_OK = 0;
const EXIT_REFUSED = 1;
const ARABIC_NAME = 'بوابة أكمي';

/** A config.json without branding, in the starter's formatting (two-space indent, final newline). */
const BASE_CONFIG = `${JSON.stringify(
  {
    apiBaseUrl: 'http://localhost:5180',
    origins: { api: ['http://localhost:5180'], attachment: [] },
    security: { protocolPrefix: 'STARTER' },
    defaultLanguage: 'ar',
    permissionRefreshSeconds: 30,
    showcase: true,
  },
  null,
  2,
)}\n`;

function scratchRepo({ forkStub } = {}) {
  const temp = makeTempDir();
  for (const file of COPIED_FILES) {
    mkdirSync(dirname(join(temp.path, file)), { recursive: true });
    copyFileSync(join(REPO_ROOT, file), join(temp.path, file));
  }
  mkdirSync(dirname(join(temp.path, CONFIG)), { recursive: true });
  writeFileSync(join(temp.path, CONFIG), BASE_CONFIG);
  if (forkStub !== undefined) writeFileSync(join(temp.path, 'tools/fork/fork.mjs'), forkStub);
  return temp;
}

function init(temp, args) {
  return runScript(join(temp.path, 'init.ps1'), args, { cwd: temp.path });
}

function readConfig(temp) {
  return JSON.parse(readFileSync(join(temp.path, CONFIG), 'utf8'));
}

function git(cwd, args) {
  const result = spawnSync('git', args, { cwd, encoding: 'utf8', env: testEnvironment() });
  assert.equal(result.status, 0, result.stderr);
  return result.stdout.trim();
}

const FULL_BRANDING = ['-PrimaryColor', '#0A5C8F', '-NameEn', 'Acme Portal', '-NameAr', ARABIC_NAME];

describe('init.ps1 branding without -Minimal', () => {
  it('writes the branding block, lower-cases the colour and keeps the rest of the file', () => {
    const temp = scratchRepo();
    try {
      const result = init(temp, FULL_BRANDING);
      assert.equal(result.code, EXIT_OK, result.output);
      const config = readConfig(temp);
      assert.deepEqual(config.branding, { primaryColor: '#0a5c8f', logoSrc: '', appName: { ar: ARABIC_NAME, en: 'Acme Portal' } });
      const { branding, ...rest } = config;
      assert.deepEqual(rest, JSON.parse(BASE_CONFIG));
      const text = readFileSync(join(temp.path, CONFIG), 'utf8');
      assert.ok(text.startsWith(BASE_CONFIG.slice(0, BASE_CONFIG.indexOf('"showcase"'))), 'the existing keys keep their formatting');
      assert.ok(text.endsWith('}\n'));
      assert.equal(branding.appName.ar, ARABIC_NAME);
    } finally {
      temp.cleanup();
    }
  });

  it('copies the logo into the web app and points logoSrc at it', () => {
    const temp = scratchRepo();
    try {
      const logo = join(temp.path, 'source-logo.SVG');
      writeFileSync(logo, '<svg xmlns="http://www.w3.org/2000/svg"/>');
      const result = init(temp, [...FULL_BRANDING, '-LogoPath', logo]);
      assert.equal(result.code, EXIT_OK, result.output);
      assert.equal(readConfig(temp).branding.logoSrc, 'branding/logo.svg');
      assert.equal(readFileSync(join(temp.path, LOGO_DIR, 'logo.svg'), 'utf8'), '<svg xmlns="http://www.w3.org/2000/svg"/>');
    } finally {
      temp.cleanup();
    }
  });

  it('keeps the values a later run leaves out', () => {
    const temp = scratchRepo();
    try {
      assert.equal(init(temp, FULL_BRANDING).code, EXIT_OK);
      const result = init(temp, ['-PrimaryColor', '#abc']);
      assert.equal(result.code, EXIT_OK, result.output);
      assert.deepEqual(readConfig(temp).branding, { primaryColor: '#abc', logoSrc: '', appName: { ar: ARABIC_NAME, en: 'Acme Portal' } });
    } finally {
      temp.cleanup();
    }
  });

  it('refuses a first run that misses a required value, changing nothing', () => {
    const temp = scratchRepo();
    try {
      const result = init(temp, ['-PrimaryColor', '#0a5c8f', '-NameEn', 'Acme Portal']);
      assert.equal(result.code, EXIT_REFUSED, result.output);
      assert.match(result.output, /-NameAr/);
      assert.equal(readFileSync(join(temp.path, CONFIG), 'utf8'), BASE_CONFIG);
    } finally {
      temp.cleanup();
    }
  });

  const refusals = [
    ['a colour that is not hex', ['-PrimaryColor', 'green', '-NameEn', 'A', '-NameAr', 'B'], /not a hex colour/],
    ['a short hex colour without #', ['-PrimaryColor', '0a5c8f', '-NameEn', 'A', '-NameAr', 'B'], /not a hex colour/],
    ['a blank name', ['-PrimaryColor', '#0a5c8f', '-NameEn', '   ', '-NameAr', 'B'], /-NameEn must be/],
    ['a name that is too long', ['-PrimaryColor', '#0a5c8f', '-NameEn', 'n'.repeat(121), '-NameAr', 'B'], /-NameEn must be/],
    ['a missing logo file', [...FULL_BRANDING, '-LogoPath', 'C:\\no\\such\\logo.svg'], /is not a file/],
  ];
  for (const [name, args, message] of refusals) {
    it(`refuses ${name}, changing nothing`, () => {
      const temp = scratchRepo();
      try {
        const result = init(temp, args);
        assert.equal(result.code, EXIT_REFUSED, result.output);
        assert.match(result.output, message);
        assert.equal(readFileSync(join(temp.path, CONFIG), 'utf8'), BASE_CONFIG);
      } finally {
        temp.cleanup();
      }
    });
  }

  it('refuses a logo that is not an image type', () => {
    const temp = scratchRepo();
    try {
      const logo = join(temp.path, 'logo.gif');
      writeFileSync(logo, 'GIF89a');
      const result = init(temp, [...FULL_BRANDING, '-LogoPath', logo]);
      assert.equal(result.code, EXIT_REFUSED, result.output);
      assert.match(result.output, /-LogoPath must be one of/);
      assert.equal(existsSync(join(temp.path, LOGO_DIR)), false);
    } finally {
      temp.cleanup();
    }
  });
});

describe('init.ps1 branding with -Minimal', () => {
  /** The fork step stub leaves a marker inside .git, so the work tree stays clean. */
  const FORK_MARKER = '.git/fork-ran';
  const SUCCEEDING_FORK = `import { writeFileSync } from 'node:fs';\nwriteFileSync('${FORK_MARKER}', '');\n`;
  const FAILING_FORK = "console.error('fork refused'); process.exitCode = 1;\n";

  function committedRepo(forkStub) {
    const temp = scratchRepo({ forkStub });
    git(temp.path, ['init', '--quiet']);
    git(temp.path, ['add', '-A']);
    git(temp.path, ['commit', '--quiet', '-m', 'starter']);
    return temp;
  }

  it('applies and commits the branding after the fork step succeeds', () => {
    const temp = committedRepo(SUCCEEDING_FORK);
    try {
      const result = init(temp, ['-Minimal', '-ProductPrefix', 'ACME', ...FULL_BRANDING]);
      assert.equal(result.code, EXIT_OK, result.output);
      assert.equal(existsSync(join(temp.path, FORK_MARKER)), true);
      assert.equal(readConfig(temp).branding.primaryColor, '#0a5c8f');
      assert.equal(git(temp.path, ['log', '-1', '--format=%s']), 'init: branding (Acme Portal)');
      assert.equal(git(temp.path, ['status', '--porcelain']), '');
    } finally {
      temp.cleanup();
    }
  });

  it('leaves the branding untouched when the fork step fails', () => {
    const temp = committedRepo(FAILING_FORK);
    try {
      const result = init(temp, ['-Minimal', '-ProductPrefix', 'ACME', ...FULL_BRANDING]);
      assert.equal(result.code, EXIT_REFUSED, result.output);
      assert.equal(readFileSync(join(temp.path, CONFIG), 'utf8'), BASE_CONFIG);
      assert.equal(git(temp.path, ['log', '-1', '--format=%s']), 'starter');
    } finally {
      temp.cleanup();
    }
  });

  it('refuses invalid branding before the fork step runs', () => {
    const temp = committedRepo("import { writeFileSync } from 'node:fs';\nwriteFileSync('fork-ran', '');\n");
    try {
      const result = init(temp, ['-Minimal', '-ProductPrefix', 'ACME', '-PrimaryColor', 'green', '-NameEn', 'A', '-NameAr', 'B']);
      assert.equal(result.code, EXIT_REFUSED, result.output);
      assert.equal(existsSync(join(temp.path, FORK_MARKER)), false);
    } finally {
      temp.cleanup();
    }
  });

  it('runs the fork step alone when no branding is given', () => {
    const temp = committedRepo(SUCCEEDING_FORK);
    try {
      const result = init(temp, ['-Minimal', '-ProductPrefix', 'ACME']);
      assert.equal(result.code, EXIT_OK, result.output);
      assert.equal(existsSync(join(temp.path, FORK_MARKER)), true);
      assert.equal(readFileSync(join(temp.path, CONFIG), 'utf8'), BASE_CONFIG);
      assert.equal(git(temp.path, ['log', '-1', '--format=%s']), 'starter');
    } finally {
      temp.cleanup();
    }
  });
});
