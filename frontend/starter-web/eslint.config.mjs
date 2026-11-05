// ESLint for the starter web app (`npm run lint`). The rules live in tools/lint/starter-eslint-config.mjs, shared
// with the lint-fixture spec; see tools/README.md.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createStarterWebEslintConfig } from './tools/lint/starter-eslint-config.mjs';

const rootDir = path.dirname(fileURLToPath(import.meta.url));

export default createStarterWebEslintConfig({ rootDir });
