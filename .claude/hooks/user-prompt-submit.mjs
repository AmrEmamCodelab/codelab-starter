#!/usr/bin/env node
// UserPromptSubmit: adds the intent -> skill routing table to every request, so vague requests still start on the
// right skill; a prompt that matches no keyword is pointed at the default route (build-feature). See README.md.
import { contextOutput, runHook } from './lib/hook-io.mjs';
import { formatRoutingContext, loadRoutingTable } from './lib/routing.mjs';
import { resolveProjectRoot } from './lib/paths.mjs';
import { HookEvents, HookNames } from './lib/constants.mjs';

await runHook(HookNames.UserPromptSubmit, (input) => {
  const projectRoot = resolveProjectRoot(input);
  const { routes, defaultRoute } = loadRoutingTable(projectRoot);
  return contextOutput({
    hookEventName: HookEvents.UserPromptSubmit,
    additionalContext: formatRoutingContext({ routes, defaultRoute, prompt: input.prompt, projectRoot }),
  });
});
