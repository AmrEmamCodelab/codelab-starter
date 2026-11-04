import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/**/*.spec.ts'],
    // Angular's partially compiled packages link their declarations through the JIT compiler facade.
    setupFiles: ['./vitest.setup.mjs'],
  },
});
