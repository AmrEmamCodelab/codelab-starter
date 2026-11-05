# ADR-009: Font and icon licences

- **Status:** Accepted, pending owner approval (build-plan owner decision 14)
- **Date:** 2026-11-04
- **Deciders:** Amr Emam (platform owner)

## Context

`@codelab/ng-dga` is our own DGA component library (decision D5: no external DGA package). It must work offline and inside a no-egress zone, so it bundles every font and icon it uses. A CDN or a remote stylesheet is not allowed. Any third-party file we ship to clients must have a licence that allows bundling, changing and redistributing it inside closed-source government products. Each file must also be traceable to its source and version.

The DGA visual language needs an Arabic face with a matching Latin face, and a general icon set. The library draws its icons from that set as an owned icon font with stable code points.

## Decision

Only these sources are used. `packages/npm/ng-dga/assets/LICENCES.json` lists the same sources, and a test fails when the two disagree.

| Source | Package | Version | Licence | Use | Copyright |
| --- | --- | --- | --- | --- | --- |
| `ibm-plex-sans-arabic` | `@fontsource/ibm-plex-sans-arabic` | 5.3.0 | OFL-1.1 | Arabic face: weights 400, 500, 600 and 700, Arabic subset, WOFF2 | Copyright 2019 IBM Corp. |
| `ibm-plex-sans` | `@fontsource/ibm-plex-sans` | 5.3.0 | OFL-1.1 | Latin face: weights 400, 500, 600 and 700, Latin subset, WOFF2 | Copyright 2019 IBM Corp. |
| `bootstrap-icons` | `bootstrap-icons` | 1.13.2 | MIT | SVG sources of the owned icon font `Codelab Icons` (`codelab-icons.woff2`) | Copyright (c) 2019-2024 The Bootstrap Authors |

1. **Every asset has a licence entry.** No file may be added under `packages/npm/ng-dga/assets/` without an entry in `assets/LICENCES.json` and, for a new source, a row in the table above. Each entry names its source and is one of two kinds:
   - **copied** (`from`, `sha256`): a file copied byte for byte from the pinned npm package;
   - **generated** (`generatedBy`): a build output from a listed source.

   The licence text of every source ships next to its files, as `fonts/LICENSE-*.txt` and `icons/LICENSE-bootstrap-icons.txt`. `tests/licences.test.mjs` fails on an asset without an entry, an entry without a file, a stale hash, an unknown or unused source, or a mismatch with this table.
2. **Fonts are copied unchanged.** `npm run assets:import -w @codelab/ng-dga` copies the WOFF2 files and licence texts from the source packages, which are exact-pinned root devDependencies, and records each sha256. `assets:check` verifies the copy. The fonts are not renamed or modified, so the OFL Reserved Font Name clause does not apply. They are declared under their own family names in `styles/_fonts.scss`.
3. **The icon font is generated deterministically.** `icons/icons.json` lists each icon's library name, its SVG source file and a fixed code point in the Unicode Private Use Area (U+E001 and up). A released code point never changes. `npm run icons:generate -w @codelab/ng-dga` builds the font with a fixed timestamp through the pinned svgicons2svgfont, svg2ttf and wawoff2 tools. It runs offline and gives byte-identical output. A test regenerates the font and compares it with the committed files. The MIT notice ships beside the font.
4. **No remote resources.** The package build fails when built CSS or JavaScript references an `http(s)` or protocol-relative URL.
5. **Changing a source** (a new font, icon set or version) needs a new row here and in `LICENCES.json`, a re-import, and platform-owner review. CODEOWNERS covers `packages/`. Only licences that allow bundling and redistribution in closed-source products qualify, such as OFL-1.1, MIT or Apache-2.0. Copyleft licences that would bind the product (GPL, AGPL) and non-commercial licences are not allowed.

## Consequences

- Apps get the fonts and icons from the package itself, through `@use '@codelab/ng-dga/styles'` and the `angular.json` asset glob. No CDN is needed, and the no-egress zone works.
- Adding an icon means adding its SVG name to `icons/icons.json` at the next free code point, then running `assets:import` and `icons:generate`. The tests catch a forgotten step.
- The font and icon choice is recorded here and is pending formal owner approval (build-plan owner decision 14). Until then, a change of font or icon source replaces only these assets. Components use the `--dga-font-family-*` tokens and the icon names, never the source files.
- Updating a source package version means updating this table, `LICENCES.json` and the root devDependency pin together.
