// Removes the previous build output so a deleted source file never survives into dist/ (and the package).
import { rm } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const DIST = fileURLToPath(new URL('../dist', import.meta.url));

await rm(DIST, { recursive: true, force: true });
