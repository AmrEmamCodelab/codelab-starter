#!/usr/bin/env node
// Entry point of the codelab-vectors-sync bin; the logic and its tests live in ../vectors/cli.mjs.
import { main } from '../vectors/cli.mjs';

const ARGUMENTS_START = 2;

process.exitCode = main(process.argv.slice(ARGUMENTS_START));
