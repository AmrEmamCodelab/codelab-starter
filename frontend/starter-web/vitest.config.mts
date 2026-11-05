import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { constructorParametersDownlevelTransform } from '@angular/compiler-cli/private/tooling';
import ts from 'typescript';
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

/**
 * Signal inputs, models, outputs and signal queries (`input()`, `model()`, `output()`, `viewChild()`...) exist only
 * through the Angular compiler: plain JIT sees no input and silently ignores the binding. This plugin runs the
 * Angular CLI's own JIT transform (`@angular/compiler-cli/private/tooling`, the one `ng test` uses) on each source
 * file that calls one of those APIs, which adds the matching `propDecorators` metadata. TypeScript in, TypeScript out
 * (oxc still compiles it), one file per program with no type resolution, so it stays fast.
 */
const SIGNAL_INITIALIZER_API = /\b(?:input|model|output|viewChild|viewChildren|contentChild|contentChildren)\s*[<(.]/;
const JIT_TRANSFORM_OPTIONS: ts.CompilerOptions = {
  target: ts.ScriptTarget.ES2022,
  module: ts.ModuleKind.Preserve,
  experimentalDecorators: true,
  isolatedModules: true,
  noResolve: true,
  noLib: true,
  types: [],
};

function angularSignalMetadata(): Plugin {
  return {
    name: 'starter-angular-signal-metadata',
    enforce: 'pre',
    transform(code, id) {
      if (!SOURCE_FILE.test(id) || !code.includes('@angular/core') || !SIGNAL_INITIALIZER_API.test(code)) {
        return null;
      }
      const source = ts.createSourceFile(id, code, ts.ScriptTarget.ES2022, true, ts.ScriptKind.TS);
      const host = ts.createCompilerHost(JIT_TRANSFORM_OPTIONS);
      host.getSourceFile = () => source;
      host.fileExists = () => true;
      host.readFile = () => code;
      const program = ts.createProgram({ rootNames: [id], options: JIT_TRANSFORM_OPTIONS, host });
      const transform = constructorParametersDownlevelTransform(program);
      const result = ts.transform(program.getSourceFile(id) ?? source, [transform], JIT_TRANSFORM_OPTIONS);
      const transformed = ts.createPrinter().printFile(result.transformed[0]);
      result.dispose();
      return { code: transformed, map: null };
    },
  };
}

export default defineConfig({
  plugins: [inlineAngularResources(), angularSignalMetadata()],
  // Angular's decorators are the legacy (experimentalDecorators) kind; JIT reads their metadata at runtime.
  oxc: { decorator: { legacy: true } },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.spec.ts', 'tools/**/*.spec.ts', 'tools/**/*.test.mjs', 'e2e/**/*.test.mjs'],
    // The template-inlining transform reads each component's .html, which the module cache does not key on: a cached
    // module would keep an old template after an .html-only edit, so the file-system module cache stays off.
    fsModuleCache: false,
    setupFiles: ['src/test-setup.ts'],
    restoreMocks: true,
  },
});
