// Runs before every spec: a zoneless Angular test environment on jsdom. Components compile in JIT, so the
// compiler is loaded here; vitest.config.mts inlines templateUrl files for it.
import '@angular/compiler';
import { getTestBed } from '@angular/core/testing';
import { BrowserTestingModule, platformBrowserTesting } from '@angular/platform-browser/testing';
import { afterEach } from 'vitest';

getTestBed().initTestEnvironment(BrowserTestingModule, platformBrowserTesting(), {
  teardown: { destroyAfterEach: true },
});

// TestBed resets itself through a global afterEach that only Jasmine and Jest globals provide; Vitest runs
// without globals, so the reset is registered here.
afterEach(() => {
  getTestBed().resetTestingModule();
});
