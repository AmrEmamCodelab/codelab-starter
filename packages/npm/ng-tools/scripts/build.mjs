// Builds dist/ from src/: the package is plain ES modules, so the build is a clean copy. It uses Node built-ins only,
// so it also runs as `prepack` on the staged copy tools/build.ps1 packs (no node_modules there).
import { cpSync, rmSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const SOURCE = fileURLToPath(new URL('../src', import.meta.url));
const DIST = fileURLToPath(new URL('../dist', import.meta.url));

rmSync(DIST, { recursive: true, force: true });
cpSync(SOURCE, DIST, { recursive: true });
console.log(`Built ${DIST}`);
