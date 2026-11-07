// Full rename on a real repo subset (init Step 1, ADR-003): copies backend/src/Starter.Api, the solution file, the
// web app's package.json and config.json and the API deploy kit into a temp folder, applies the default renameMap of
// a multi-word product, and checks that no Starter identifier is left (allow-listed manifest names and non-identifier
// words aside) and that the renamed solution and project files still parse and point at existing projects.
// Run: node --test tools/fork/tests/rename-full-tree.e2e.test.mjs
import assert from 'node:assert/strict';
import { cpSync, existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { after, before, describe, it } from 'node:test';
import { isBinary } from '../lib/hashing.mjs';
import { defaultRenameMap } from '../lib/rename-defaults.mjs';
import { createRenamer } from '../lib/rename.mjs';
import { listTreeFiles, renameWorkingTree } from '../lib/rename-tree.mjs';
import { makeTempDir } from './support/powershell.mjs';
import { write } from './support/synthetic-starter.mjs';

const REPO_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const SUBSET = Object.freeze({
  apiFolder: 'backend/src/Starter.Api',
  files: Object.freeze([
    'codelab-starter.slnx',
    'frontend/starter-web/package.json',
    'frontend/starter-web/public/config.json',
    'deploy/kits/Install-Api.ps1',
  ]),
});
const PRODUCT = Object.freeze({ prefix: 'MOHPERMITS', nameEn: 'Moh Permits', nameAr: 'تصاريح الوزارة' });
const RENAMED = Object.freeze({
  solution: 'codelab-moh-permits.slnx',
  apiProject: 'backend/src/MohPermits.Api/MohPermits.Api.csproj',
  apiRules: 'backend/src/MohPermits.Api/module.rules.yaml',
  webManifest: 'frontend/moh-permits-web/package.json',
  webConfig: 'frontend/moh-permits-web/public/config.json',
  apiKit: 'deploy/kits/Install-Api.ps1',
});
/** Any word that contains the starter token, with the separators an identifier, path or package name may hold. */
const STARTER_WORD = /[A-Za-z0-9_.-]*starter[A-Za-z0-9_-]*/gi;
/** Words allowed to keep the token: the protected manifest names and English words that are not identifiers. */
const ALLOWED_WORD = /^(?:.*\.)?(?:codelab\.starter(?:\.json|\.schema\.json)?|starterBase|starterVersion|[Rr]estarter\w*|[Ss]tarters|STARTERS)$/;
const PROJECT_PATH = /<Project\s+Path="([^"]+)"/g;
const XML_COMMENT = /<!--[\s\S]*?-->/g;
const XML_DECLARATION = /<\?[\s\S]*?\?>/g;
const XML_TAG = /<(\/?)([A-Za-z_][\w.:-]*)(?:\s[^<>]*?)?(\/?)>/g;
const WINDOWS_SEPARATOR = /\\/g;

/** Parses tag nesting; returns null when balanced, otherwise the first problem. */
function xmlNestingProblem(text) {
  const stack = [];
  for (const [, closing, name, selfClosing] of text.replace(XML_COMMENT, '').replace(XML_DECLARATION, '').matchAll(XML_TAG)) {
    if (selfClosing) continue;
    if (!closing) {
      stack.push(name);
      continue;
    }
    const open = stack.pop();
    if (open !== name) return `</${name}> closes <${open}>`;
  }
  return stack.length === 0 ? null : `unclosed <${stack.at(-1)}>`;
}

function leftoverStarterWords(root) {
  const leftovers = new Set();
  for (const file of listTreeFiles(root)) {
    for (const [word] of file.matchAll(STARTER_WORD)) if (!ALLOWED_WORD.test(word)) leftovers.add(`path ${file}`);
    const buffer = readFileSync(join(root, file));
    if (isBinary(buffer)) continue;
    for (const [word] of buffer.toString('utf8').matchAll(STARTER_WORD)) if (!ALLOWED_WORD.test(word)) leftovers.add(`${file}: ${word}`);
  }
  return [...leftovers];
}

describe('full rename of a repo subset with the default renameMap', () => {
  const temp = makeTempDir();
  const root = join(temp.path, 'fork');
  let result;

  before(() => {
    for (const file of listTreeFiles(join(REPO_ROOT, SUBSET.apiFolder))) {
      cpSync(join(REPO_ROOT, SUBSET.apiFolder, file), join(root, SUBSET.apiFolder, file));
    }
    for (const file of SUBSET.files) cpSync(join(REPO_ROOT, file), join(root, file));
    write(root, `${SUBSET.apiFolder}/obj/project.assets.json`, '{ "project": "Starter.Api" }\n');
    result = renameWorkingTree(root, createRenamer(defaultRenameMap(PRODUCT)));
  });
  after(() => temp.cleanup());

  it('renames folders and files, and carries the build output along', () => {
    for (const file of Object.values(RENAMED)) assert.ok(existsSync(join(root, file)), file);
    assert.equal(existsSync(join(root, SUBSET.apiFolder)), false);
    assert.equal(existsSync(join(root, 'frontend/starter-web')), false);
    assert.ok(existsSync(join(root, 'backend/src/MohPermits.Api/obj/project.assets.json')));
    assert.ok(result.moves.length > SUBSET.files.length);
  });

  it('leaves no Starter identifier outside the allow-list', () => {
    assert.deepEqual(leftoverStarterWords(root), []);
  });

  it('keeps the solution and project files well-formed and pointing at the renamed project', () => {
    const solution = readFileSync(join(root, RENAMED.solution), 'utf8');
    assert.equal(xmlNestingProblem(solution), null);
    const projects = [...solution.matchAll(PROJECT_PATH)].map(([, projectPath]) => projectPath.replace(WINDOWS_SEPARATOR, '/'));
    assert.ok(projects.includes(RENAMED.apiProject), `${RENAMED.apiProject} listed in the solution`);
    for (const project of projects.filter((candidate) => candidate.startsWith('backend/src/MohPermits.Api/'))) {
      assert.ok(existsSync(join(root, project)), project);
    }
    const apiProject = readFileSync(join(root, RENAMED.apiProject), 'utf8');
    assert.equal(xmlNestingProblem(apiProject), null);
    assert.match(apiProject, /MohPermits\./);
  });

  it('renames the npm app name, display names, protocol prefix, rule ids and IIS names', () => {
    assert.equal(JSON.parse(readFileSync(join(root, RENAMED.webManifest), 'utf8')).name, 'moh-permits-web');
    const config = JSON.parse(readFileSync(join(root, RENAMED.webConfig), 'utf8'));
    assert.equal(config.security.protocolPrefix, PRODUCT.prefix);
    assert.deepEqual(config.branding.appName, { ar: PRODUCT.nameAr, en: PRODUCT.nameEn });
    const rules = readFileSync(join(root, RENAMED.apiRules), 'utf8');
    assert.match(rules, /^module: MohPermitsApi$/m);
    assert.match(rules, /- id: mohpermitsapi\./);
    assert.match(readFileSync(join(root, RENAMED.apiKit), 'utf8'), /DefaultAppPoolName\s+= 'MohPermitsApi'/);
  });
});
