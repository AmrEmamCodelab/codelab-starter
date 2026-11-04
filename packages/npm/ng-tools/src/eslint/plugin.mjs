// The `codelab` ESLint plugin: the custom rules @codelab/ng-tools ships.

import { readFileSync } from 'node:fs';
import { wireIdCastRule } from './rules/wire-id-cast.mjs';

const manifest = JSON.parse(readFileSync(new URL('../../package.json', import.meta.url), 'utf8'));

/** The plugin namespace the presets register it under: rules are `codelab/<rule>`. */
export const PLUGIN_NAMESPACE = 'codelab';

export const codelabPlugin = Object.freeze({
  meta: { name: manifest.name, version: manifest.version },
  rules: Object.freeze({ 'wire-id-cast': wireIdCastRule }),
});
