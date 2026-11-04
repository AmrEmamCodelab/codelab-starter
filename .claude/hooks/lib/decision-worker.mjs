// Worker side of budgeted-decision.mjs: computes the PreToolUse decision and posts it back.
import { parentPort, workerData } from 'node:worker_threads';
import { decidePreToolUse } from './pre-tool-use-decision.mjs';

parentPort.postMessage(decidePreToolUse(workerData.input));
