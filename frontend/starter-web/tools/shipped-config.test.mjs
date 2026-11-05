// Guards the config.json that every build copies from public/: the showcase is dev/staging only and its routes are
// anonymous, so the shipped file must never enable it. Dev and staging turn it on in their own config.
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const SHIPPED_CONFIG_PATH = resolve(dirname(fileURLToPath(import.meta.url)), '..', 'public', 'config.json');
const SHOWCASE_KEY = 'showcase';

describe('shipped public/config.json', () => {
  it('does not enable the showcase', () => {
    const config = JSON.parse(readFileSync(SHIPPED_CONFIG_PATH, 'utf8'));
    expect(config[SHOWCASE_KEY] === true).toBe(false);
  });
});
