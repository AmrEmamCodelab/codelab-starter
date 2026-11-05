// `codelab-ng-tools rtl-lint <folder> [--icons <map.json>] [--direction-file <glob>]... [--fix <text>]`
//
// Checks every template and TypeScript file under <folder> (see check.mjs). `--icons` is the icon code point map
// (@codelab/ng-dga/assets/icons/codelab-icons.json): its mirrored icons drive the icon rule and its directional icons
// must mirror. `--direction-file` replaces the default files that may fix the direction (index.html). Exit codes:
// 0 clean, 1 a finding, 2 usage.
import path from 'node:path';
import { readIconMap } from '../icons/icon-map.mjs';
import { ExitCodes, parseArguments } from '../shared/arguments.mjs';
import { DEFAULT_DIRECTION_FILES, checkRtl, formatRtlFindings } from './check.mjs';

export const RtlOptions = Object.freeze({ icons: '--icons', directionFile: '--direction-file', fix: '--fix' });
export const RTL_USAGE = `Usage: codelab-ng-tools rtl-lint <folder> [${RtlOptions.icons} <map.json>] [${RtlOptions.directionFile} <glob>]... [${RtlOptions.fix} <text>]`;

const SPEC = Object.freeze({ values: [RtlOptions.icons, RtlOptions.fix], repeatable: [RtlOptions.directionFile], positionals: 1 });

/** Parses the rtl-lint arguments; returns `{ dir, icons?, directionFiles, fix? }` or `{ error }`. */
export function parseRtlArguments(argv) {
  const parsed = parseArguments(argv, SPEC);
  if (parsed.error !== undefined) return parsed;
  const [dir] = parsed.positionals;
  if (dir === undefined) return { error: 'the folder to check is required.' };
  const { icons, fix } = parsed.options;
  const directionFiles = parsed.options['direction-file'];
  return { dir, icons, fix, directionFiles: directionFiles.length > 0 ? directionFiles : [...DEFAULT_DIRECTION_FILES] };
}

/** Runs rtl-lint with argv after the subcommand; returns the exit code. */
export function runRtlLint(argv, io = console) {
  const options = parseRtlArguments(argv);
  if (options.error !== undefined) {
    io.error(`${options.error}\n${RTL_USAGE}`);
    return ExitCodes.usage;
  }
  let iconMap = null;
  if (options.icons !== undefined) {
    const read = readIconMap(path.resolve(options.icons));
    if (read.error !== undefined) {
      io.error(read.error);
      return ExitCodes.usage;
    }
    iconMap = read.map;
  }
  const result = checkRtl(path.resolve(options.dir), { iconMap, iconMapFile: options.icons, directionFiles: options.directionFiles });
  if (result.error !== undefined) {
    io.error(result.error);
    return ExitCodes.usage;
  }
  if (result.findings.length > 0) {
    io.error(formatRtlFindings(result.findings, options.fix));
    return ExitCodes.failure;
  }
  io.log(`rtl lint: ${result.fileCount} file(s), no physical direction`);
  return ExitCodes.ok;
}
