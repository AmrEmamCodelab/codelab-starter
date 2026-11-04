#!/usr/bin/env node
// UserPromptSubmit: adds the intent -> skill routing table to every request, so vague requests still start on the
// right skill. See README.md.
import { contextOutput, runHook } from './lib/hook-io.mjs';
import { formatRoutingContext, loadRoutes } from './lib/routing.mjs';
import { resolveProjectRoot } from './lib/paths.mjs';
import { HookEvents } from './lib/constants.mjs';

await runHook('user-prompt-submit', (input) => {
  const projectRoot = resolveProjectRoot(input);
  return contextOutput({
    hookEventName: HookEvents.UserPromptSubmit,
    additionalContext: formatRoutingContext({ routes: loadRoutes(projectRoot), prompt: input.prompt, projectRoot }),
  });
});
