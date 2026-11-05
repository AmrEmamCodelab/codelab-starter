// Plans the byte-for-byte copy of third-party assets from their npm source packages (ADR-009): the font files and
// licence texts listed in assets/LICENCES.json, and the icon SVG sources listed in icons/icons.json. The source
// packages are root devDependencies pinned to the manifest versions; nothing reaches the app at runtime from them.
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { findPackageFolder } from './files.mjs';

const SVG_EXTENSION = '.svg';
const PACKAGE_MANIFEST = 'package.json';

/**
 * The copies to make: { target (absolute), bytes, entry? } for each copied asset and icon source, plus errors for a
 * missing or differently versioned source package. `findPackage(name)` returns the installed package folder or null.
 */
export function planAssetImport({ licences, icons, assetsDir, svgDir, findPackage }) {
  const errors = [];
  const folders = new Map();
  for (const source of licences.sources) {
    const folder = findPackage(source.package);
    if (folder === null) {
      errors.push(`${source.package} is not installed; run npm install at the repository root.`);
      continue;
    }
    const installed = JSON.parse(readFileSync(path.join(folder, PACKAGE_MANIFEST), 'utf8')).version;
    if (installed !== source.version) {
      errors.push(`${source.package} ${installed} is installed, but LICENCES.json records ${source.version}.`);
      continue;
    }
    folders.set(source.id, folder);
  }
  const copies = [];
  for (const entry of licences.files.filter((file) => file.from)) {
    const folder = folders.get(entry.source);
    if (!folder) continue;
    copies.push({ entry, target: path.join(assetsDir, entry.path), bytes: readFileSync(path.join(folder, entry.from)) });
  }
  const iconFolder = folders.get(icons.source);
  if (iconFolder) {
    for (const icon of icons.icons) {
      const bytes = readFileSync(path.join(iconFolder, icons.sourceFolder, icon.file));
      copies.push({ target: path.join(svgDir, `${icon.name}${SVG_EXTENSION}`), bytes });
    }
  }
  return { errors, copies };
}

/** The real package lookup: node_modules of this package or the nearest parent (npm workspaces hoist to the root). */
export function packageFinderFrom(startFolder) {
  return (packageName) => findPackageFolder(startFolder, packageName);
}
