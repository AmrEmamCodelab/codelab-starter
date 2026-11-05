// `codelab-ng-tools icons-subset --dir <folder>... --map <map.json> [--keep <names>]... [--out <folder> [--stem <name>]
//    [--generator <module> --svg-dir <folder>] [--check]]`
//
// Scans every --dir for the icons in use (see icon-usage.mjs) and fails on literal icon names the map does not know.
// Without --out it reports the used set. With --out it writes `<stem>.json` (the subset code point map) and, with
// --generator and --svg-dir, `<stem>.woff2` (the subset font); --check compares instead of writing. Exit codes: 0 ok,
// 1 an unknown icon or (with --check) a stale output, 2 usage.
import fs from 'node:fs';
import path from 'node:path';
import { ExitCodes, parseArguments, splitList } from '../shared/arguments.mjs';
import { readIconMap } from './icon-map.mjs';
import { collectIconUsage, driftedSubsetOutputs, subsetOutputs } from './subset.mjs';

export const IconOptions = Object.freeze({
  dir: '--dir',
  map: '--map',
  keep: '--keep',
  out: '--out',
  stem: '--stem',
  generator: '--generator',
  svgDir: '--svg-dir',
  check: '--check',
});
/** The file stem of @codelab/ng-dga's icon font, so a subset can stand in for the full font. */
export const DEFAULT_SUBSET_STEM = 'codelab-icons';
export const ICONS_USAGE = `Usage: codelab-ng-tools icons-subset ${IconOptions.dir} <folder>... ${IconOptions.map} <map.json> [${IconOptions.keep} <a,b>]... [${IconOptions.out} <folder> [${IconOptions.stem} <name>] [${IconOptions.generator} <module> ${IconOptions.svgDir} <folder>] [${IconOptions.check}]]`;

const SPEC = Object.freeze({
  values: [IconOptions.map, IconOptions.out, IconOptions.stem, IconOptions.generator, IconOptions.svgDir],
  repeatable: [IconOptions.dir, IconOptions.keep],
  flags: [IconOptions.check],
});

/** Parses the icons-subset arguments; returns the options or `{ error }`. */
export function parseIconArguments(argv) {
  const parsed = parseArguments(argv, SPEC);
  if (parsed.error !== undefined) return parsed;
  const { dir, map, keep, out, stem, generator, check } = parsed.options;
  const svgDir = parsed.options['svg-dir'];
  if (dir.length === 0) return { error: `${IconOptions.dir} is required.` };
  if (map === undefined) return { error: `${IconOptions.map} is required.` };
  if ((generator === undefined) !== (svgDir === undefined)) return { error: `${IconOptions.generator} and ${IconOptions.svgDir} go together.` };
  if (out === undefined && (generator !== undefined || check || stem !== undefined)) return { error: `${IconOptions.generator}, ${IconOptions.stem} and ${IconOptions.check} need ${IconOptions.out}.` };
  return { dirs: dir, map, keep: splitList(keep), out, stem: stem ?? DEFAULT_SUBSET_STEM, generator, svgDir, check };
}

function formatUnknown(unknown) {
  const lines = unknown.map(({ file, line, column, detail }) => `  ${file}:${line}:${column} unknown icon '${detail}'`);
  return `Icon names not in the icon map (${unknown.length}); they render nothing:\n${lines.join('\n')}\nFix: use a name from the map, or add the icon to @codelab/ng-dga (platform owner).`;
}

async function writeOrCheck(options, map, used, io) {
  let outputs;
  try {
    outputs = await subsetOutputs({
      map, used, outDir: path.resolve(options.out), fileStem: options.stem,
      generator: options.generator, svgDir: options.svgDir === undefined ? undefined : path.resolve(options.svgDir),
    });
  } catch (error) {
    io.error(`icon subset: the font could not be built: ${error.message}`);
    return ExitCodes.usage;
  }
  const drifted = driftedSubsetOutputs(outputs);
  if (options.check) {
    for (const { file } of drifted) io.error(`${file} is out of date; rerun icons-subset without ${IconOptions.check}.`);
    return drifted.length === 0 ? ExitCodes.ok : ExitCodes.failure;
  }
  for (const { file, bytes } of drifted) {
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, bytes);
  }
  io.log(`icon subset: ${outputs.length} output(s), ${drifted.length} written to ${options.out}`);
  return ExitCodes.ok;
}

/** Runs icons-subset with argv after the subcommand; resolves to the exit code. */
export async function runIconsSubset(argv, io = console) {
  const options = parseIconArguments(argv);
  if (options.error !== undefined) {
    io.error(`${options.error}\n${ICONS_USAGE}`);
    return ExitCodes.usage;
  }
  const read = readIconMap(path.resolve(options.map));
  if (read.error !== undefined) {
    io.error(read.error);
    return ExitCodes.usage;
  }
  const usage = collectIconUsage(options.dirs.map((dir) => path.resolve(dir)), read.map, { keep: options.keep });
  if (usage.error !== undefined) {
    io.error(usage.error);
    return ExitCodes.usage;
  }
  if (usage.unknown.length > 0) {
    io.error(formatUnknown(usage.unknown));
    return ExitCodes.failure;
  }
  const total = Object.keys(read.map.icons).length;
  io.log(`icons: ${usage.used.length} of ${total} used in ${usage.fileCount} file(s): ${usage.used.join(', ')}`);
  return options.out === undefined ? ExitCodes.ok : writeOrCheck(options, read.map, usage.used, io);
}
