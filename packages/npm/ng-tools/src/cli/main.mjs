// The `codelab-ng-tools` command line: one dispatcher for every subcommand.
//
//   codelab-ng-tools permission-gates --dir <folder> [--fix <text>]
//   codelab-ng-tools rtl-lint <folder> [--icons <map.json>] [--direction-file <glob>]... [--fix <text>]
//   codelab-ng-tools icons-subset --dir <folder>... --map <map.json> [--keep <names>]... [--out <folder> ...]
//   codelab-ng-tools i18n-check --dir <translations> [--src <folder>]... [--allow-latin <key>]... [--pipe <name>]...
//
// Exit codes (every subcommand): 0 clean, 1 a finding, 2 usage.
import { runIconsSubset, ICONS_USAGE } from '../icons/cli.mjs';
import { runI18nCheck, I18N_USAGE } from '../i18n/cli.mjs';
import { USAGE as PERMISSION_GATES_USAGE, main as runPermissionGatesCommand } from '../permission-gates/cli.mjs';
import { RTL_USAGE, runRtlLint } from '../rtl/cli.mjs';
import { ExitCodes } from '../shared/arguments.mjs';

export const Subcommands = Object.freeze({
  permissionGates: 'permission-gates',
  rtlLint: 'rtl-lint',
  iconsSubset: 'icons-subset',
  i18nCheck: 'i18n-check',
});

export const CLI_USAGE = [PERMISSION_GATES_USAGE, RTL_USAGE, ICONS_USAGE, I18N_USAGE].join('\n');

/** Dispatches argv (without node and the script) to a subcommand; resolves to the exit code. */
export async function main(argv, io = console) {
  const [subcommand, ...rest] = argv;
  switch (subcommand) {
    case Subcommands.permissionGates:
      return runPermissionGatesCommand(argv, io);
    case Subcommands.rtlLint:
      return runRtlLint(rest, io);
    case Subcommands.iconsSubset:
      return runIconsSubset(rest, io);
    case Subcommands.i18nCheck:
      return runI18nCheck(rest, io);
    default:
      io.error(`Unknown subcommand: ${subcommand ?? '(none)'}\n${CLI_USAGE}`);
      return ExitCodes.usage;
  }
}
