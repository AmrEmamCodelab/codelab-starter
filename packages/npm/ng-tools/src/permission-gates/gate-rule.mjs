// The template permission gate (base.fe-permission-gated): every action element in a feature template carries
// *hasPermission or an explicit data-public-action. An action element is a <dga-button>; a <button> with any event
// binding ((click), on-click, (keyup.enter)...) or that submits (type="submit", or no type inside a form that binds
// (submit) or (ngSubmit)); any element with role="button" and an event binding; and any element that binds an
// action output, (click) or (confirm) (the editor-dialog pattern's save), in either the (x) or the on-x form. An
// element is also gated when an ancestor element carries *hasPermission, or an <ng-template> ancestor binds
// [hasPermission] (the de-sugared form of the structural directive).
import { scanTemplate } from './template-scanner.mjs';

/** Attribute and element names the rule reads (attribute names are case-sensitive, as in Angular). */
export const GateMarkers = Object.freeze({
  structuralDirective: '*hasPermission',
  templateBinding: '[hasPermission]',
  publicAction: 'data-public-action',
  clickBinding: '(click)',
  dgaButton: 'dga-button',
  nativeButton: 'button',
  templateElement: 'ng-template',
  formElement: 'form',
  typeAttribute: 'type',
  roleAttribute: 'role',
  buttonRole: 'button',
});

/** Outputs that perform an action on any element: a click, and the dialog patterns' confirm. */
export const ACTION_OUTPUTS = Object.freeze(['click', 'confirm']);
/** Outputs of a <form> that submit it; a submit button inside such a form is an action. */
export const SUBMIT_OUTPUTS = Object.freeze(['submit', 'ngSubmit']);
/** `type` values of a <button> that do not submit its form. */
const NON_SUBMIT_BUTTON_TYPES = new Set(['button', 'reset']);
const SUBMIT_BUTTON_TYPE = 'submit';
const EVENT_BINDING = /^\(([^()]+)\)$/;
const EVENT_BINDING_PREFIX = 'on-';
const EVENT_MODIFIER_SEPARATOR = '.';

/** Why a button or gate failed the check. */
export const GateFindingKinds = Object.freeze({
  /** An action button with neither a permission gate nor data-public-action. */
  ungatedButton: 'ungated-button',
  /** A *hasPermission or [hasPermission] value that is a string literal instead of a typed key. */
  literalKey: 'literal-key',
});

export const PERMISSION_GATE_FIX =
  `Add ${GateMarkers.structuralDirective}="PermissionKeys.<Key>" to the button (or to an element around it), or mark an `
  + `action every visitor may take (sign in, language switch, retry) with ${GateMarkers.publicAction}. Keys come from `
  + 'the generated PermissionKeys, never a string literal.';

const LITERAL_START = /^\s*['"`]/;

const hasValue = (attributes, name) => {
  const value = attributes.get(name);
  return typeof value === 'string' && value.trim() !== '';
};

/** The event names an element binds (`(click)` and `on-click` give `click`; `(keyup.enter)` gives `keyup.enter`). */
function boundEvents(attributes) {
  const events = [];
  for (const attribute of attributes.keys()) {
    const match = EVENT_BINDING.exec(attribute);
    if (match) events.push(match[1].trim());
    else if (attribute.startsWith(EVENT_BINDING_PREFIX) && attribute.length > EVENT_BINDING_PREFIX.length) {
      events.push(attribute.slice(EVENT_BINDING_PREFIX.length));
    }
  }
  return events;
}

const baseEvent = (event) => event.split(EVENT_MODIFIER_SEPARATOR)[0];
const bindsAny = (events, outputs) => events.some((event) => outputs.includes(baseEvent(event)));

/** True for a <form> that binds a submit output. */
function isSubmittingForm(name, attributes) {
  return name === GateMarkers.formElement && bindsAny(boundEvents(attributes), SUBMIT_OUTPUTS);
}

/** True for a <button> that submits: `type="submit"`, or no type (the HTML default) inside a submitting form. */
function isSubmitButton(attributes, insideSubmittingForm) {
  const type = (attributes.get(GateMarkers.typeAttribute) ?? '').trim().toLowerCase();
  if (type === SUBMIT_BUTTON_TYPE) return true;
  return insideSubmittingForm && !NON_SUBMIT_BUTTON_TYPES.has(type) && !attributes.has(`[${GateMarkers.typeAttribute}]`);
}

/**
 * True for the elements the rule applies to.
 * @param {string} name the lower-cased tag name
 * @param {ReadonlyMap<string, string | null>} attributes
 * @param {boolean} [insideSubmittingForm] the element sits inside a <form> that binds (submit) or (ngSubmit)
 */
export function isActionButton(name, attributes, insideSubmittingForm = false) {
  if (name === GateMarkers.dgaButton) return true;
  const events = boundEvents(attributes);
  if (bindsAny(events, ACTION_OUTPUTS)) return true;
  if (name === GateMarkers.nativeButton) return events.length > 0 || isSubmitButton(attributes, insideSubmittingForm);
  return attributes.get(GateMarkers.roleAttribute) === GateMarkers.buttonRole && events.length > 0;
}

/** True when the element itself gates its subtree. */
function grantsSubtree(name, attributes) {
  if (hasValue(attributes, GateMarkers.structuralDirective)) return true;
  return name === GateMarkers.templateElement && hasValue(attributes, GateMarkers.templateBinding);
}

/** The gate attribute of an element whose value is a string literal (`*hasPermission="'a.b'"`), else null. */
function literalGate(attributes) {
  for (const name of [GateMarkers.structuralDirective, GateMarkers.templateBinding]) {
    if (LITERAL_START.test(attributes.get(name) ?? '')) return name;
  }
  return null;
}

/**
 * Every ungated action button and every string-literal permission key of one template, as
 * `{ kind, line, column, element }` (1-based; `element` is the tag name, `kind` a {@link GateFindingKinds} value).
 * @param {string} text the template source
 */
export function findUngatedButtons(text) {
  const findings = [];
  const open = [];
  for (const event of scanTemplate(text)) {
    if (event.type === 'close') {
      const index = open.findLastIndex((element) => element.name === event.name);
      if (index !== -1) open.length = index;
      continue;
    }
    const position = { line: event.line, column: event.column, element: event.name };
    if (literalGate(event.attributes) !== null) findings.push({ kind: GateFindingKinds.literalKey, ...position });
    const grants = grantsSubtree(event.name, event.attributes);
    const gated = grants || event.attributes.has(GateMarkers.publicAction) || open.some((element) => element.grants);
    const insideSubmittingForm = open.some((element) => element.submits);
    if (isActionButton(event.name, event.attributes, insideSubmittingForm) && !gated) {
      findings.push({ kind: GateFindingKinds.ungatedButton, ...position });
    }
    if (!event.selfClosing) open.push({ name: event.name, grants, submits: isSubmittingForm(event.name, event.attributes) });
  }
  return findings;
}
