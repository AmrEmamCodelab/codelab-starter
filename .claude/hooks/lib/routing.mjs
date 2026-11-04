// Formats the intent -> skill routing table (routing-table.json) as context, highlighting likely matches.
// Whether a skill exists is read from disk (.claude/skills/<skill>/SKILL.md), so the table never goes stale.
import fs from 'node:fs';
import path from 'node:path';
import { RepoPaths } from './constants.mjs';

const SKILLS_DIR = '.claude/skills';
const SKILL_FILE = 'SKILL.md';

/** Reads the routing table; a missing or broken file yields no routes. */
export function loadRoutes(projectRoot) {
  try {
    const table = JSON.parse(fs.readFileSync(path.join(projectRoot, RepoPaths.RoutingTable), 'utf8'));
    return Array.isArray(table.routes) ? table.routes : [];
  } catch {
    return [];
  }
}

/** The name a route points to: its skill, else its recipe. */
function targetOf(route) {
  return route.skill ?? route.recipe ?? '';
}

/** Routes whose keywords occur in the prompt (case-insensitive, whole words). */
export function matchRoutes(routes, prompt) {
  const text = String(prompt ?? '').toLowerCase();
  return routes.filter((route) => (route.keywords ?? []).some((keyword) => {
    const escaped = keyword.toLowerCase().replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return new RegExp(`\\b${escaped}\\b`).test(text);
  }));
}

function skillExists(projectRoot, skill) {
  return fs.existsSync(path.join(projectRoot, SKILLS_DIR, skill, SKILL_FILE));
}

function formatRoute(route, projectRoot) {
  const missing = route.skill && !skillExists(projectRoot, route.skill) ? ' (skill not built yet)' : '';
  const kind = route.skill ? 'skill' : 'recipe';
  const also = route.also ? `; see also ${route.also}` : '';
  return `- ${route.intent} -> ${kind} ${targetOf(route)}${missing}${also}`;
}

/** The additionalContext text, or null when there are no routes. */
export function formatRoutingContext({ routes, prompt, projectRoot }) {
  if (routes.length === 0) return null;
  const lines = [
    'Codelab starter routing table (intent -> where to start):',
    ...routes.map((route) => formatRoute(route, projectRoot)),
  ];
  const matches = matchRoutes(routes, prompt);
  if (matches.length > 0) lines.push(`Likely match for this request: ${matches.map(targetOf).join(', ')}.`);
  return lines.join('\n');
}
