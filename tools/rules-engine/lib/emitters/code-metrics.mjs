// CodeMetricsConfig.txt: the threshold file read by the CA1502 (cyclomatic complexity) analyzer.
// The analyzer reads it only when it is passed as an AdditionalFile (Codelab.Build.Sdk wiring).
import { CYCLOMATIC_COMPLEXITY_RULE, GENERATED_MARKER, NEWLINE } from '../constants.mjs';

export function emitCodeMetrics(module) {
  return [
    `# ${GENERATED_MARKER} from ${module.rulesFile} (thresholds.cyclomaticComplexity). Do not edit.`,
    `${CYCLOMATIC_COMPLEXITY_RULE}: ${module.thresholds.cyclomaticComplexity}`,
  ].join(NEWLINE) + NEWLINE;
}
