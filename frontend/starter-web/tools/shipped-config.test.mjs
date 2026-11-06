// Guards the config.json that every build copies from public/: the showcase is dev/staging only and its routes are
// anonymous, so the shipped file must never enable it. Dev and staging turn it on in their own config. The public
// contact form ships on (its endpoint lands in P5; the backend setting portal.contact-enabled decides at run time).
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const SHIPPED_CONFIG_PATH = resolve(dirname(fileURLToPath(import.meta.url)), '..', 'public', 'config.json');
const SHOWCASE_KEY = 'showcase';
const CONTACT_FORM_KEY = 'portalContactForm';

describe('shipped public/config.json', () => {
  it('does not enable the showcase', () => {
    const config = JSON.parse(readFileSync(SHIPPED_CONFIG_PATH, 'utf8'));
    expect(config[SHOWCASE_KEY] === true).toBe(false);
  });

  it('turns the public contact form on (the contact endpoint ships in P5)', () => {
    const config = JSON.parse(readFileSync(SHIPPED_CONFIG_PATH, 'utf8'));
    expect(config[CONTACT_FORM_KEY]).toBe(true);
  });
});
