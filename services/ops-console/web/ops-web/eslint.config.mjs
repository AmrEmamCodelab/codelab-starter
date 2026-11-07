// ESLint for the Ops Console web app (`npm run lint`): the starter web app's flat config, unchanged (boundaries,
// HTTP only through core/api, no UI library, no inline styles, no magic numbers, int enums). It is reused by import,
// never copied, so both apps follow one rule set; see frontend/starter-web/tools/lint/starter-eslint-config.mjs.
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createStarterWebEslintConfig } from '../../../../frontend/starter-web/tools/lint/starter-eslint-config.mjs';

const rootDir = path.dirname(fileURLToPath(import.meta.url));

export default createStarterWebEslintConfig({ rootDir });
