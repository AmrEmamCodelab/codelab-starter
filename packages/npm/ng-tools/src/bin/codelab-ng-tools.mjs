#!/usr/bin/env node
// Entry point of the codelab-ng-tools bin (subcommands permission-gates, rtl-lint, icons-subset, i18n-check); the
// dispatcher and its tests live in ../cli/main.mjs.
import { main } from '../cli/main.mjs';

const ARGUMENTS_START = 2;

process.exitCode = await main(process.argv.slice(ARGUMENTS_START));
