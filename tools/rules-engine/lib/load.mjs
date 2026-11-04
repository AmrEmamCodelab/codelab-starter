// Reads rules files and validates them against rules/rules.schema.json.
import { Ajv2020 } from 'ajv/dist/2020.js';
import { parseDocument } from 'yaml';
import { readText } from './files.mjs';

const BASE_DEF = 'baseRules';
const MODULE_DEF = 'moduleRules';

/** Compiles the base and module validators from the schema JSON text. */
export function createValidators(schemaText) {
  const schema = JSON.parse(schemaText);
  const ajv = new Ajv2020({ allErrors: true, strict: true, allowUnionTypes: true });
  ajv.addSchema(schema);
  const ref = (definition) => ajv.getSchema(`${schema.$id}#/$defs/${definition}`);
  return { base: ref(BASE_DEF), module: ref(MODULE_DEF) };
}

/**
 * Parses YAML text. Syntax errors are reported to `problems` with line numbers and yield undefined.
 * @param {string} text @param {string} file repo-relative path for messages @param {import('./errors.mjs').ProblemList} problems
 */
export function parseYaml(text, file, problems) {
  const document = parseDocument(text, { prettyErrors: true, uniqueKeys: true });
  if (document.errors.length > 0) {
    for (const error of document.errors) {
      problems.add(file, `YAML ${error.message.split('\n')[0]}`);
    }
    return undefined;
  }
  return document.toJS();
}

/** Validates `data` with an Ajv validator and reports each schema error to `problems`. Returns true when valid. */
export function validateAgainstSchema(validator, data, file, problems) {
  if (validator(data)) {
    return true;
  }
  for (const error of validator.errors) {
    const where = error.instancePath === '' ? '(root)' : error.instancePath;
    const detail = error.params?.additionalProperty ? ` '${error.params.additionalProperty}'` : '';
    problems.add(file, `schema: ${where} ${error.message}${detail}`);
  }
  return false;
}

/** Reads, parses and schema-validates one rules file. Returns the data, or undefined when invalid. */
export function loadRulesFile(absolutePath, file, validator, problems) {
  const data = parseYaml(readText(absolutePath), file, problems);
  if (data === undefined) {
    return undefined;
  }
  return validateAgainstSchema(validator, data, file, problems) ? data : undefined;
}
