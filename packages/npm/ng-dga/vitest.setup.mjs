// Runs before every component spec: a zoneless Angular test environment on jsdom. The specs import the components
// compiled ahead of time by scripts/compile-specs.mjs; the JIT compiler is loaded only for TestBed's own modules.
// Vitest runs without globals, so Angular cannot hook its own per-test reset: the module is reset after every test.
import '@angular/compiler';
import { getTestBed } from '@angular/core/testing';
import { BrowserTestingModule, platformBrowserTesting } from '@angular/platform-browser/testing';
import { afterEach } from 'vitest';

getTestBed().initTestEnvironment(BrowserTestingModule, platformBrowserTesting(), {
  teardown: { destroyAfterEach: true },
});

afterEach(() => {
  getTestBed().resetTestingModule();
});
