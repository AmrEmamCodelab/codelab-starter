// The new-transition plan: reads the record type module's workflow sources, checks the request against them and
// records every write in a ChangeSet (nothing is written until the caller commits, so a refusal changes nothing).
import fs from 'node:fs';
import path from 'node:path';
import { MODULES_ROOT, ModuleFiles } from '../constants.mjs';
import { ScaffoldRefusal, addUsings } from '../csharp-edit.mjs';
import { renderTemplateSet } from '../templates.mjs';
import { existingModules } from '../workspace.mjs';
import {
  AUDIENCES,
  CSHARP_EXTENSION,
  I18N_BUNDLES,
  SKIPPED_FOLDERS,
  SourcePatterns,
  TransitionFlags,
  TransitionPaths,
  TransitionTemplates,
} from './constants.mjs';
import {
  addClassMember,
  isSameOrParentNamespace,
  addListEntry,
  addToAllList,
  classDeclares,
  constantsOf,
  csharpString,
  enclosingClass,
  escapeRegex,
  findClasses,
  listAnchor,
  listRegion,
  nextStatusValue,
  xmlText,
} from './csharp-source.mjs';
import {
  intConstant,
  labelKey,
  notificationExpression,
  ruleEntry,
  ruleSummary,
  statusEntry,
  stringConstant,
  wordingKey,
} from './entries.mjs';
import { addBundleKeys, bundleHas } from './i18n.mjs';
import { toPascalCase } from '../names.mjs';
import { moduleDocLines, addModuleDocNotes } from './module-doc.mjs';
import { addKitReference, ruleTestTokens, statusTestTokens } from './tests.mjs';

const TEST_CLASS = Object.freeze({
  status: (statusConst) => `${statusConst}StatusTests`,
  rule: (actionConst, fromConst) => `${actionConst}From${fromConst}TransitionTests`,
});

function moduleSources(changes, module) {
  const dir = `${MODULES_ROOT}/${module}`;
  const walk = (relative) => fs.readdirSync(path.join(changes.root, relative), { withFileTypes: true })
    .sort((left, right) => left.name.localeCompare(right.name))
    .flatMap((entry) => {
      const child = `${relative}/${entry.name}`;
      if (entry.isDirectory()) return SKIPPED_FOLDERS.includes(entry.name) ? [] : walk(child);
      return entry.name.endsWith(CSHARP_EXTENSION) ? [child] : [];
    });
  return walk(dir).map((file) => ({ path: file, text: changes.read(file) }));
}

function single(found, what, hint) {
  if (found.length === 1) return found[0];
  if (found.length === 0) throw new ScaffoldRefusal(`The module declares no ${what}. ${hint}`);
  throw new ScaffoldRefusal(`The module declares more than one ${what} (${found.map((each) => `${each.name} in ${each.path}`).join(', ')}).`);
}

function optional(found, what) {
  if (found.length > 1) throw new ScaffoldRefusal(`The module declares more than one ${what} (${found.map((each) => each.name).join(', ')}).`);
  return found[0] ?? null;
}

const SETUP_HINT = 'Create the workflow first (docs/recipes/add-workflow-type.md); new-transition only extends one.';

/** The workflow classes of the module, read from its sources. */
function discover(changes, module) {
  const files = moduleSources(changes, module);
  const statuses = single(findClasses(files, SourcePatterns.statusesClass), 'statuses class (static class <Name>Statuses)', SETUP_HINT);
  const definition = single(findClasses(files, SourcePatterns.definitionClass), 'workflow definition (a class implementing IRecordWorkflowDefinition)', SETUP_HINT);
  const listFiles = files.filter((file) => listAnchor(file.text, 'statuses') && listAnchor(file.text, 'rules'));
  if (listFiles.length !== 1) {
    throw new ScaffoldRefusal(listFiles.length === 0
      ? 'No file holds both workflow lists: add `// scaffold:workflow-statuses` and `// scaffold:workflow-rules` inside them, or declare them as `Statuses { get; } = [...]` and `Rules { get; } = [...]`.'
      : `More than one file holds workflow lists (${listFiles.map((file) => file.path).join(', ')}); mark the right ones with // scaffold:workflow-statuses and // scaffold:workflow-rules.`);
  }
  const lists = listFiles[0];
  return {
    files,
    statuses,
    statusKeys: optional(findClasses(files, SourcePatterns.statusKeysClass), 'status keys class'),
    actions: optional(findClasses(files, SourcePatterns.actionsClass), 'actions class'),
    notificationKeys: optional(findClasses(files, SourcePatterns.notificationKeysClass), 'notification keys class'),
    definition,
    lists: { path: lists.path, workflowClass: enclosingClass(lists.text, listAnchor(lists.text, 'rules').index) },
    permissionKeysPath: `${MODULES_ROOT}/${module}/${ModuleFiles.permissionKeys(module)}`,
    permissionKeysNamespace: (changes.read(`${MODULES_ROOT}/${module}/${ModuleFiles.permissionKeys(module)}`) ?? '').match(SourcePatterns.namespace)?.[1] ?? null,
    permissionKeysClass: `${module}PermissionKeys`,
    catalogSourceClass: `${module}PermissionCatalogSource`,
  };
}

function textOf(changes, file) {
  return changes.read(file);
}

function edit(changes, file, transform, failure) {
  const result = transform(textOf(changes, file));
  if (result === null) throw new ScaffoldRefusal(failure);
  changes.write(file, result);
}

/** The constant of `value` in class `info` (existing name, or `name` added with `summary`). */
function ensureStringConstant(changes, info, { name, value, summary }) {
  const constants = constantsOf(textOf(changes, info.path), info.name, SourcePatterns.stringConstant);
  const byValue = constants.find((constant) => constant.value === value);
  if (byValue) return `${info.name}.${byValue.name}`;
  const byName = constants.find((constant) => constant.name === name);
  if (byName) throw new ScaffoldRefusal(`${info.name}.${name} already holds "${byName.value}", not "${value}".`);
  edit(changes, info.path, (text) => addClassMember(text, info.name, stringConstant(name, value, summary)), `${info.name} has no class body to add to.`);
  changes.write(info.path, addToAllList(textOf(changes, info.path), info.name, name));
  return `${info.name}.${name}`;
}

function statusConstants(changes, sources) {
  return constantsOf(textOf(changes, sources.statuses.path), sources.statuses.name, SourcePatterns.intConstant);
}

function statusRefOf(sources, statusConstantsList, key, newStatus) {
  const name = toPascalCase(key);
  if (newStatus?.key === key || statusConstantsList.some((constant) => constant.name === name)) return `${sources.statuses.name}.${name}`;
  throw new ScaffoldRefusal(`The status '${key}' is not declared (${sources.statuses.name}.${name} does not exist). Add it with ${TransitionFlags.status} ${key}.`);
}

function wordingHelperIn(changes, sources) {
  const text = textOf(changes, sources.lists.path);
  return {
    wording: classDeclares(text, sources.lists.workflowClass, SourcePatterns.wordingHelper),
    label: classDeclares(text, sources.lists.workflowClass, SourcePatterns.labelHelper),
  };
}

function planStatus(changes, sources, request, notes) {
  const { status } = request;
  const constants = statusConstants(changes, sources);
  const statusRef = `${sources.statuses.name}.${status.constName}`;
  const existing = constants.find((constant) => constant.name === status.constName);
  const value = existing ? Number(existing.value) : nextStatusValue(constants);
  if (!existing) {
    const summary = xmlText(`${status.wordingEn[0]}${status.terminal ? ' (terminal)' : ''}.`);
    edit(changes, sources.statuses.path, (text) => addClassMember(text, sources.statuses.name, intConstant(status.constName, value, summary)), `${sources.statuses.name} has no class body to add to.`);
  }
  const keyRef = sources.statusKeys
    ? ensureStringConstant(changes, sources.statusKeys, { name: status.constName, value: status.key, summary: `<see cref="${statusRef}"/>.` })
    : csharpString(status.key);
  const region = listRegion(textOf(changes, sources.lists.path), 'statuses');
  const added = !new RegExp(`${escapeRegex(statusRef)}\\b`).test(region);
  if (added) {
    const entry = statusEntry({ statusRef, keyRef, terminal: status.terminal, prefix: request.i18nPrefix, statusKey: status.key, wordingHelper: wordingHelperIn(changes, sources).wording });
    edit(changes, sources.lists.path, (text) => addListEntry(text, 'statuses', entry), 'The Statuses list has no anchor.');
  } else {
    notes.push(`unchanged status ${status.key}: ${statusRef} is already in the Statuses list`);
  }
  return { statusRef, value, added };
}

function permissionRefOf(changes, sources, permissionKey) {
  const text = textOf(changes, sources.permissionKeysPath);
  const constant = text === null ? undefined
    : constantsOf(text, sources.permissionKeysClass, SourcePatterns.stringConstant).find((each) => each.value === permissionKey);
  if (!constant) {
    throw new ScaffoldRefusal(`The permission '${permissionKey}' is not a constant of ${sources.permissionKeysClass}. Add it first: node tools/scaffold/new-permission.mjs --module <Module> --action <kebab>.`);
  }
  return `${sources.permissionKeysClass}.${constant.name}`;
}

function templateRefOf(changes, sources, notification) {
  if (notification.none) return null;
  if (!sources.notificationKeys) return csharpString(notification.templateKey);
  const name = toPascalCase(notification.templateKey.split('.').pop());
  return ensureStringConstant(changes, sources.notificationKeys, { name, value: notification.templateKey, summary: xmlText(`The ${notification.templateKey} template.`) });
}

function reasonFieldRefOf(changes, sources, field) {
  if (field === null) return 'null';
  const constant = constantsOf(textOf(changes, sources.lists.path), sources.lists.workflowClass, SourcePatterns.stringConstant)
    .find((each) => each.value === field);
  return constant ? constant.name : csharpString(field);
}

function ruleExists(region, actionRef, fromRef) {
  const named = new RegExp(`ActionKey:\\s*${escapeRegex(actionRef)}\\s*,\\s*FromStatus:\\s*${escapeRegex(fromRef)}\\s*,`);
  const positional = new RegExp(`${escapeRegex(actionRef)}\\s*,[^\\r\\n]*\\(\\s*${escapeRegex(fromRef)}\\s*,`);
  return named.test(region) || positional.test(region);
}

function actionRefOf(changes, sources, rule) {
  if (!sources.actions) throw new ScaffoldRefusal(`The module declares no actions class (static class <Name>Actions). ${SETUP_HINT}`);
  return ensureStringConstant(changes, sources.actions, { name: rule.actionConst, value: rule.actionKey, summary: xmlText(ruleSummary(rule, rule.fromKeys)) });
}

function planLabels(changes, request) {
  const { rule } = request;
  const key = labelKey(request.i18nPrefix, rule.actionKey);
  const missing = Object.entries(I18N_BUNDLES)
    .filter(([, bundle]) => !bundleHas(changes.read(bundle), key))
    .map(([language]) => language);
  const given = { en: rule.labelEn, ar: rule.labelAr };
  const absent = missing.filter((language) => !given[language]);
  if (absent.length > 0) {
    throw new ScaffoldRefusal(`The action label '${key}' needs ${absent.map((language) => `--label-${language}`).join(' and ')} (never invent Arabic text; ask for it).`);
  }
  return given;
}

function planRule(changes, sources, request, newStatus, notes) {
  const { rule } = request;
  const constants = statusConstants(changes, sources);
  const actionRef = actionRefOf(changes, sources, rule);
  const helpers = wordingHelperIn(changes, sources);
  const shared = {
    actionRef,
    toRef: statusRefOf(sources, constants, rule.toKey, request.status),
    permissionRef: permissionRefOf(changes, sources, rule.permissionKey),
    side: rule.side,
    reason: rule.reason,
    reasonFieldRef: reasonFieldRefOf(changes, sources, rule.reasonField),
    labelRef: helpers.label ? `LabelKey(${actionRef})` : csharpString(labelKey(request.i18nPrefix, rule.actionKey)),
    notification: notificationExpression(rule.notification, templateRefOf(changes, sources, rule.notification)),
    owner: rule.owner,
  };
  const added = [];
  for (const fromKey of rule.fromKeys) {
    const fromRef = statusRefOf(sources, constants, fromKey, request.status);
    if (ruleExists(listRegion(textOf(changes, sources.lists.path), 'rules'), actionRef, fromRef)) {
      notes.push(`unchanged transition ${rule.actionKey} from ${fromKey}: it is already in the Rules list`);
      continue;
    }
    edit(changes, sources.lists.path, (text) => addListEntry(text, 'rules', ruleEntry({ ...shared, fromRef })), 'The Rules list has no anchor.');
    added.push({ fromKey, fromRef });
  }
  return { ...shared, added, terminalTo: Boolean(newStatus && request.status.terminal && rule.toKey === request.status.key) };
}

function addListUsings(changes, sources, namespaces) {
  const listNamespace = (changes.read(sources.lists.path).match(SourcePatterns.namespace) ?? [])[1];
  const wanted = namespaces.filter((ns) => ns && !isSameOrParentNamespace(ns, listNamespace));
  if (wanted.length === 0) return;
  changes.write(sources.lists.path, addUsings(changes.read(sources.lists.path), wanted).text);
}

function planI18n(changes, request, labels) {
  const entries = { en: [], ar: [] };
  if (request.status) {
    AUDIENCES.forEach((audience, index) => {
      const key = wordingKey(request.i18nPrefix, request.status.key, audience);
      entries.en.push({ key, value: request.status.wordingEn[index] });
      entries.ar.push({ key, value: request.status.wordingAr[index] });
    });
  }
  if (request.rule) {
    const key = labelKey(request.i18nPrefix, request.rule.actionKey);
    if (labels.en) entries.en.push({ key, value: labels.en });
    if (labels.ar) entries.ar.push({ key, value: labels.ar });
  }
  const kept = [];
  for (const [language, bundle] of Object.entries(I18N_BUNDLES)) {
    const text = changes.read(bundle);
    if (text === null) throw new ScaffoldRefusal(`The ${language} bundle ${bundle} does not exist.`);
    const result = addBundleKeys(text, entries[language], bundle);
    changes.write(bundle, result.text);
    kept.push(...result.kept.map((key) => `${language} ${key}`));
  }
  return kept;
}

/** Writes the stubs that do not exist yet (never overwrites a test) and the kit reference; returns the stubs written. */
function writeTests(changes, module, tokensList, set) {
  const written = [];
  for (const tokens of tokensList) {
    for (const file of renderTemplateSet(set, tokens)) {
      if (changes.exists(file.path)) continue;
      changes.write(file.path, file.content);
      written.push(file.path);
    }
  }
  if (written.length === 0) return written;
  const project = TransitionPaths.testProject(module);
  const csproj = changes.read(project);
  const referenced = csproj === null ? null : addKitReference(csproj);
  if (referenced === null) throw new ScaffoldRefusal(`The module test project ${project} does not exist or has no ProjectReference to add the test kit next to.`);
  changes.write(project, referenced);
  return written;
}

/** The test stubs of what this run added (an unchanged status or rule gets none). */
function planTests(changes, request, sources, { status, rule }) {
  const context = { module: request.module, sources, changes };
  const written = [];
  if (status?.added) {
    const tokens = statusTestTokens(context, request.status, status, TEST_CLASS.status(request.status.constName));
    written.push(...writeTests(changes, request.module, [tokens], TransitionTemplates.status));
  }
  if (rule && rule.added.length > 0) {
    const tokensList = rule.added.map((from) =>
      ruleTestTokens(context, request, rule, from, TEST_CLASS.rule(request.rule.actionConst, toPascalCase(from.fromKey))));
    written.push(...writeTests(changes, request.module, tokensList, TransitionTemplates.rule));
  }
  return written;
}

function planModuleDoc(changes, request, { status, rule, testFiles }, notes) {
  const docPath = TransitionPaths.moduleDoc(request.module);
  const doc = changes.read(docPath);
  if (doc === null) {
    notes.push(`no ${docPath}: describe the change in the module's docs by hand`);
    return;
  }
  changes.write(docPath, addModuleDocNotes(doc, moduleDocLines(request, status?.added ? status : null, rule, testFiles)));
}

function refuseMissingModule(root, module) {
  if (!existingModules(root).includes(module)) throw new ScaffoldRefusal(`There is no module '${module}' under ${MODULES_ROOT}.`);
}

/**
 * Records every write of the request in `changes`.
 * @returns {{ notes: string[], statusValue: number | null, testFiles: string[] }}
 */
export function planTransition(changes, request) {
  refuseMissingModule(changes.root, request.module);
  const sources = discover(changes, request.module);
  const notes = [];
  const labels = request.rule ? planLabels(changes, request) : {};
  const status = request.status ? planStatus(changes, sources, request, notes) : null;
  const rule = request.rule ? planRule(changes, sources, request, status, notes) : null;
  addListUsings(changes, sources, [sources.statuses.namespace, sources.statusKeys?.namespace, sources.actions?.namespace,
    sources.notificationKeys?.namespace, request.rule ? sources.permissionKeysNamespace : null]);
  notes.push(...planI18n(changes, request, labels).map((kept) => `kept the existing (different) i18n value of ${kept}`));
  const testFiles = planTests(changes, request, sources, { status, rule });
  planModuleDoc(changes, request, { status, rule, testFiles }, notes);
  return { notes, statusValue: status?.value ?? null, testFiles };
}

