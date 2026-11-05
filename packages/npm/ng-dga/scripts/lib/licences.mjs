// The licence rules of ADR-009 as code: every file under assets/ has a manifest entry (assets/LICENCES.json), every
// entry names a known source whose licence file ships too, copied files match their recorded sha256, and the
// manifest sources agree with the ADR-009 sources table. Pure functions; the CLI and the tests supply the inputs.

const SOURCE_KEYS = Object.freeze(['id', 'title', 'package', 'version', 'licence', 'copyright', 'licenceFile']);
const ADR_TABLE_COLUMNS = Object.freeze(['source', 'package', 'version', 'licence']);
const TABLE_ROW = /^\|(.+)\|\s*$/;
const TABLE_DIVIDER = /^\|[\s:|-]+\|\s*$/;
const BACKTICKS = /`/g;
const CELL_SEPARATOR = '|';
const FIRST_DATA_ROW = 2;

/** Every problem with the manifest against the files on disk. `hashOf(path)` returns a file's sha256. */
export function validateLicences({ manifest, assetFiles, hashOf }) {
  const errors = [];
  const sources = new Map((manifest.sources ?? []).map((source) => [source.id, source]));
  for (const source of manifest.sources ?? []) {
    for (const key of SOURCE_KEYS) {
      if (!source[key]) errors.push(`source '${source.id ?? '?'}' has no ${key}.`);
    }
  }
  const entries = new Map();
  for (const entry of manifest.files ?? []) {
    if (entries.has(entry.path)) errors.push(`${entry.path} is listed twice.`);
    entries.set(entry.path, entry);
    errors.push(...validateEntry(entry, sources, assetFiles, hashOf));
  }
  for (const file of assetFiles) {
    if (!entries.has(file)) errors.push(`${file} has no entry in LICENCES.json (ADR-009: no asset without a licence entry).`);
  }
  for (const source of sources.values()) {
    if (!entries.has(source.licenceFile)) errors.push(`source '${source.id}' licence file ${source.licenceFile} is not a listed asset.`);
    if (![...entries.values()].some((entry) => entry.source === source.id)) errors.push(`source '${source.id}' is used by no asset.`);
  }
  return errors;
}

function validateEntry(entry, sources, assetFiles, hashOf) {
  const errors = [];
  if (!sources.has(entry.source)) errors.push(`${entry.path} names unknown source '${entry.source}'.`);
  if (!assetFiles.includes(entry.path)) errors.push(`${entry.path} is listed but missing from assets/.`);
  const copied = Boolean(entry.from);
  const generated = Boolean(entry.generatedBy);
  if (copied === generated) errors.push(`${entry.path} needs exactly one of 'from' (copied) or 'generatedBy' (built).`);
  if (copied && assetFiles.includes(entry.path) && hashOf(entry.path) !== entry.sha256) {
    errors.push(`${entry.path} does not match its recorded sha256; run npm run assets:import -w @codelab/ng-dga.`);
  }
  return errors;
}

function cells(line) {
  return TABLE_ROW.exec(line)[1].split(CELL_SEPARATOR).map((cell) => cell.replace(BACKTICKS, '').trim());
}

/**
 * The rows of the ADR-009 sources table: the first Markdown table whose header starts with the columns
 * Source | Package | Version | Licence (case-insensitive). Returns [] when there is none.
 */
export function parseAdrSources(markdown) {
  const lines = markdown.split(/\r?\n/);
  for (let index = 0; index < lines.length; index += 1) {
    if (!TABLE_ROW.test(lines[index]) || !TABLE_DIVIDER.test(lines[index + 1] ?? '')) continue;
    const header = cells(lines[index]).map((cell) => cell.toLowerCase());
    if (!ADR_TABLE_COLUMNS.every((column, position) => header[position] === column)) continue;
    const rows = [];
    for (let row = index + FIRST_DATA_ROW; row < lines.length && TABLE_ROW.test(lines[row]); row += 1) {
      const [source, packageName, version, licence] = cells(lines[row]);
      rows.push({ source, package: packageName, version, licence });
    }
    return rows;
  }
  return [];
}

/** Disagreements between the manifest sources and the ADR-009 table, in both directions. */
export function compareWithAdr(manifest, adrRows) {
  const errors = [];
  const rows = new Map(adrRows.map((row) => [row.source, row]));
  for (const source of manifest.sources ?? []) {
    const row = rows.get(source.id);
    if (!row) {
      errors.push(`source '${source.id}' is in LICENCES.json but not in the ADR-009 sources table.`);
      continue;
    }
    for (const key of ['package', 'version', 'licence']) {
      if (row[key] !== source[key]) errors.push(`source '${source.id}' ${key}: LICENCES.json '${source[key]}', ADR-009 '${row[key]}'.`);
    }
  }
  const known = new Set((manifest.sources ?? []).map((source) => source.id));
  for (const row of adrRows) {
    if (!known.has(row.source)) errors.push(`ADR-009 lists source '${row.source}', which LICENCES.json does not.`);
  }
  return errors;
}
