import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { defineConfig, type Plugin } from 'vitest/config';

/**
 * Specs run the components in JIT on jsdom (no Karma, no browser). JIT cannot fetch `templateUrl` or
 * `styleUrl(s)`, so this plugin inlines each component's template as `template` and drops its styles
 * (jsdom does not lay anything out, so styles change no assertion). Production builds use the AOT
 * compiler in `ng build` and never see this transform.
 */
const TEMPLATE_URL = /templateUrl\s*:\s*['"]([^'"]+)['"]/g;
const STYLE_URL = /styleUrl\s*:\s*['"][^'"]+['"]\s*,?/g;
const STYLE_URLS = /styleUrls\s*:\s*\[[^\]]*\]\s*,?/g;
const SOURCE_FILE = /[\/]src[\/].+\.ts$/;

function inlineAngularResources(): Plugin {
  return {
    name: 'starter-inline-angular-resources',
    enforce: 'pre',
    transform(code, id) {
      if (!SOURCE_FILE.test(id) || !code.includes('@Component')) {
        return null;
      }
      const folder = dirname(id);
      const inlined = code
        .replace(TEMPLATE_URL, (_match, url: string) =>
          `template: ${JSON.stringify(readFileSync(resolve(folder, url), 'utf8'))}`)
        .replace(STYLE_URL, '')
        .replace(STYLE_URLS, '');
      return inlined === code ? null : { code: inlined, map: null };
    },
  };
}

export default defineConfig({
  plugins: [inlineAngularResources()],
  // Angular's decorators are the legacy (experimentalDecorators) kind; JIT reads their metadata at runtime.
  oxc: { decorator: { legacy: true } },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.spec.ts', 'tools/**/*.spec.ts', 'tools/**/*.test.mjs'],
    setupFiles: ['src/test-setup.ts'],
    restoreMocks: true,
  },
});
