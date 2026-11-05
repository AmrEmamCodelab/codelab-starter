// tools/remove-module.ps1 -Name <Name>: removes one module at file level (ADR-004). It edits only the solution and
// the host project's ProjectReference list, deletes the module's folders, records the removal in
// codelab.starter.json and regenerates the rules outputs.
import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { API_PROJECT, SOLUTION_FILE } from './constants.mjs';
import { readManifest, updateManifest } from './manifest.mjs';
import { modulePaths, refusalForModuleName, referencesModuleProject, removeProjectReferences, removeSolutionProjects } from './modules.mjs';
import { compileRules, RefusalError } from './repo.mjs';

/** The outcome of a removal. */
export const RemovalStatus = Object.freeze({ removed: 'removed', alreadyRemoved: 'already-removed' });

function readRequired(root, relativePath) {
  const file = path.join(root, relativePath);
  if (!existsSync(file)) throw new RefusalError(`${relativePath} not found; remove-module edits it and cannot run without it.`);
  return readFileSync(file, 'utf8');
}

function planRemoval(root, name) {
  const solution = removeSolutionProjects(readRequired(root, SOLUTION_FILE), (projectPath) => referencesModuleProject(projectPath, name));
  const host = removeProjectReferences(readRequired(root, API_PROJECT), (include) => referencesModuleProject(include, name));
  const folders = Object.values(modulePaths(name)).filter((folder) => existsSync(path.join(root, folder)));
  return { solution, host, folders };
}

/**
 * Removes a module from the repo at `root`.
 * @param {string} root
 * @param {string} name PascalCase module name
 * @param {{ log: (line: string) => void }} io
 * @returns {{ status: string, folders: string[], solutionProjects: string[], hostReferences: string[] }}
 */
export function removeModule(root, name, io) {
  const manifest = readManifest(root);
  const refusal = refusalForModuleName(name, manifest.requiredModules);
  if (refusal) throw new RefusalError(refusal);
  if (manifest.removedModules.includes(name)) {
    io.log(`Module ${name} is already removed (codelab.starter.json removedModules); nothing to do.`);
    return { status: RemovalStatus.alreadyRemoved, folders: [], solutionProjects: [], hostReferences: [] };
  }
  const plan = planRemoval(root, name);
  const found = plan.folders.length + plan.solution.removed.length + plan.host.removed.length;
  if (found === 0) throw new RefusalError(`Module ${name} not found: no folder, solution project or host reference.`);

  writeFileSync(path.join(root, SOLUTION_FILE), plan.solution.text);
  writeFileSync(path.join(root, API_PROJECT), plan.host.text);
  for (const folder of plan.folders) rmSync(path.join(root, folder), { recursive: true, force: true });
  updateManifest(root, (draft) => {
    draft.removedModules = [...draft.removedModules, name];
  });
  for (const project of plan.solution.removed) io.log(`  ${SOLUTION_FILE}: removed ${project}`);
  for (const reference of plan.host.removed) io.log(`  ${API_PROJECT}: removed ${reference}`);
  for (const folder of plan.folders) io.log(`  deleted ${folder}`);
  compileRules(root);
  io.log(`Module ${name} removed and recorded in codelab.starter.json removedModules.`);
  return { status: RemovalStatus.removed, folders: plan.folders, solutionProjects: plan.solution.removed, hostReferences: plan.host.removed };
}
