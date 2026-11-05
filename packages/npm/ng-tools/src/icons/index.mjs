// Icon subsetting: the icons an app uses, the subset code point map and font (`codelab-ng-tools icons-subset`).
export { DEFAULT_SUBSET_STEM, ICONS_USAGE, IconOptions, parseIconArguments, runIconsSubset } from './cli.mjs';
export { ICON_ELEMENT, iconNamesOfAttribute, isIconNameInput } from './icon-inputs.mjs';
export { iconNamesOf, readIconMap, validateIconMap } from './icon-map.mjs';
export { GENERATED_SCRIPT_SUFFIX, findUnknownIconNames, findUsedIconNames, isIconSource } from './icon-usage.mjs';
export {
  GENERATOR_EXPORT,
  buildSubsetFont,
  collectIconUsage,
  driftedSubsetOutputs,
  renderIconMap,
  subsetIconMap,
  subsetManifest,
  subsetOutputs,
} from './subset.mjs';
