import { defineConfig } from 'vitest/config';

// Two projects: the Angular components (compiled ahead of time into .angular/spec by `npm test`, run under jsdom),
// and the package checks in tests/ (licences, icon font regeneration, styles, manifest), run under Node.
export default defineConfig({
  test: {
    restoreMocks: true,
    projects: [
      {
        test: {
          name: 'components',
          environment: 'jsdom',
          include: ['.angular/spec/**/*.spec.js'],
          setupFiles: ['./vitest.setup.mjs'],
        },
      },
      {
        test: {
          name: 'package',
          environment: 'node',
          include: ['tests/**/*.test.mjs'],
          testTimeout: 60000,
        },
      },
    ],
  },
});
