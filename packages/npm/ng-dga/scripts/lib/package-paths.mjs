// Every path the package scripts and tests use, resolved from this file (never from the working directory).
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const REPO_DEPTH = ['..', '..', '..'];

export const PACKAGE_ROOT = fileURLToPath(new URL('../../', import.meta.url));
export const REPO_ROOT = path.resolve(PACKAGE_ROOT, ...REPO_DEPTH);

export const ASSETS_DIR = path.join(PACKAGE_ROOT, 'assets');
export const LICENCES_FILE_NAME = 'LICENCES.json';
export const LICENCES_FILE = path.join(ASSETS_DIR, LICENCES_FILE_NAME);
export const FONTS_STYLESHEET = path.join(PACKAGE_ROOT, 'styles', '_fonts.scss');
export const STYLES_DIR = path.join(PACKAGE_ROOT, 'styles');
export const STYLES_ENTRY = path.join(STYLES_DIR, '_index.scss');

export const ICON_MANIFEST_FILE = path.join(PACKAGE_ROOT, 'icons', 'icons.json');
export const ICON_SVG_DIR = path.join(PACKAGE_ROOT, 'icons', 'svg');
export const ICON_OUTPUT_DIR = path.join(ASSETS_DIR, 'icons');
export const ICON_TYPESCRIPT_FILE = path.join(PACKAGE_ROOT, 'src', 'lib', 'icon', 'dga-icon-names.generated.ts');

export const SOURCE_DIR = path.join(PACKAGE_ROOT, 'src');
export const NG_PACKAGE_FILE = path.join(SOURCE_DIR, 'ng-package.json');
export const LIBRARY_TSCONFIG = path.join(PACKAGE_ROOT, 'tsconfig.lib.json');
export const SPEC_TSCONFIG = path.join(PACKAGE_ROOT, 'tsconfig.spec.json');
export const SPEC_OUTPUT_DIR = path.join(PACKAGE_ROOT, '.angular', 'spec');
export const DIST_DIR = path.join(PACKAGE_ROOT, 'dist');
export const PREBUILT_CSS_FILE = path.join(DIST_DIR, 'ng-dga.css');

export const ADR_FILE = path.join(REPO_ROOT, 'docs', 'adr', 'ADR-009-font-and-icon-licences.md');
