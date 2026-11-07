// Formats the intent -> skill routing table (routing-table.json) as context, highlighting likely matches.
// Whether a skill or recipe exists is read from disk (.claude/skills/<skill>/SKILL.md, the recipe path), so the table
// never goes stale. A prompt that matches no keyword gets the default route (build-feature), so a plain business
// scenario still starts on the pipeline's entry point (build plan phase 9, "the developer never types a command").
import fs from 'node:fs';
import path from 'node:path';
import { DefaultRouteSkill, RepoPaths } from './constants.mjs';

const SKILLS_DIR = '.claude/skills';
const SKILL_FILE = 'SKILL.md';
const REGEX_SPECIAL = /[.*+?^${}()|[\]\\]/g;
const CASE_INSENSITIVE = 'i';

function readTable(projectRoot) {
  try {
    return JSON.parse(fs.readFileSync(path.join(projectRoot, RepoPaths.RoutingTable), 'utf8'));
  } catch {
    return null;
  }
}

function routesOf(table) {
  return Array.isArray(table?.routes) ? table.routes : [];
}

/** Reads the routing table; a missing or broken file yields no routes. */
export function loadRoutes(projectRoot) {
  return routesOf(readTable(projectRoot));
}

/**
 * The routes plus the default route (the table's "default" entry, else DefaultRouteSkill): the one to start on when
 * no keyword matches. A missing or broken file yields no routes and the built-in default.
 */
export function loadRoutingTable(projectRoot) {
  const table = readTable(projectRoot);
  const configured = table?.default;
  const defaultRoute = configured?.skill ? configured : { skill: DefaultRouteSkill };
  return { routes: routesOf(table), defaultRoute };
}

/** The name a route points to: its skill, else its recipe. */
function targetOf(route) {
  return route.skill ?? route.recipe ?? '';
}

function keywordMatches(keyword, text) {
  const escaped = keyword.toLowerCase().replace(REGEX_SPECIAL, '\\$&');
  return new RegExp(`\\b${escaped}\\b`).test(text);
}

function patternMatches(pattern, prompt) {
  try {
    return new RegExp(pattern, CASE_INSENSITIVE).test(prompt);
  } catch {
    return false;
  }
}

/**
 * Routes whose keywords occur in the prompt (case-insensitive, whole words) or whose patterns (regex sources,
 * case-insensitive) match it, in table order.
 */
export function matchRoutes(routes, prompt) {
  const raw = String(prompt ?? '');
  const text = raw.toLowerCase();
  return routes.filter((route) => (route.keywords ?? []).some((keyword) => keywordMatches(keyword, text))
    || (route.patterns ?? []).some((pattern) => patternMatches(pattern, raw)));
}

/** The routes to start on: the matched ones; usedDefault when nothing matched and a default route exists. */
export function pickRoutes({ routes, defaultRoute, prompt }) {
  const matches = matchRoutes(routes, prompt);
  return { matches, usedDefault: matches.length === 0 && Boolean(defaultRoute?.skill) };
}

function skillExists(projectRoot, skill) {
  return fs.existsSync(path.join(projectRoot, SKILLS_DIR, skill, SKILL_FILE));
}

function recipeExists(projectRoot, recipe) {
  return fs.existsSync(path.join(projectRoot, recipe));
}

function missingNote(route, projectRoot) {
  if (route.skill) return skillExists(projectRoot, route.skill) ? '' : ' (skill not built yet)';
  return route.recipe && !recipeExists(projectRoot, route.recipe) ? ' (recipe not written yet)' : '';
}

function formatRoute(route, projectRoot) {
  const kind = route.skill ? 'skill' : 'recipe';
  const also = route.also ? `; see also ${route.also}` : '';
  return `- ${route.intent} -> ${kind} ${targetOf(route)}${missingNote(route, projectRoot)}${also}`;
}

function defaultLine(defaultRoute) {
  return `No keyword matched: default route ${defaultRoute.skill}. If this request describes a business scenario `
    + `(what requesters, staff or the system must do), start with the ${defaultRoute.skill} skill, the default entry `
    + 'point: it writes the feature spec and calls the other skills after the spec is approved. Otherwise no skill '
    + 'is required; follow coding-standards.';
}

/** The additionalContext text, or null when there are no routes. */
export function formatRoutingContext({ routes, prompt, projectRoot, defaultRoute = null }) {
  if (routes.length === 0) return null;
  const lines = [
    'Codelab starter routing table (intent -> where to start; describe the work, the skills run themselves):',
    ...routes.map((route) => formatRoute(route, projectRoot)),
  ];
  const { matches, usedDefault } = pickRoutes({ routes, defaultRoute, prompt });
  if (matches.length > 0) lines.push(`Likely match for this request: ${matches.map(targetOf).join(', ')}.`);
  else if (usedDefault) lines.push(defaultLine(defaultRoute));
  return lines.join('\n');
}
