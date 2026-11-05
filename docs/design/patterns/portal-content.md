# Pattern: portal content page

**Use it for** a public information page: service guide, FAQ, privacy policy, terms of use, and the text around the
contact form. In phase 4 the content comes from static bilingual i18n files; phase 5 moves it to managed settings
data without changing the page frame.

**Do not use it for** pages behind sign-in that show records (use the [landing dashboard](landing-dashboard.md) or
the [work queue](work-queue.md)).

| Part | Selector | Code |
| --- | --- | --- |
| Page frame | `app-portal-content-pattern` | `src/app/patterns/portal-content/portal-content-pattern.component.*` |
| Section marker | `[portalSection]` (`PortalSectionDirective`) | `src/app/patterns/portal-content/portal-section.directive.ts` |
| Table of contents | `PortalSection`, `TocDepth`, `buildToc`, `activeSection` | `src/app/patterns/portal-content/table-of-contents.ts` |
| Keys | `PortalContentTranslationKeys` (`patterns.portal-content.*`) | `src/app/patterns/portal-content/portal-content-translation-keys.ts` |

## Anatomy

```
┌──────────────────────────────────────────────────────────────────┐
│ <dga-portal-page-header> (breadcrumb, title, subtitle)          │  [portalHeader], or titleKey + subtitleKey
├───────────────────┬──────────────────────────────────────────────┤
│ On this page      │ <section portalSection="about">              │  sections: the default slot
│ ▌ About the       │   <dga-card> ...                             │
│   service         │ <section portalSection="documents">          │
│   Documents       │   <dga-accordion> ...                        │
│     Optional      │ ...                                          │
│ (sticky ≥ 64rem)  │ Last updated 1 March 2026 · page feedback    │  [portalLastUpdated]
└───────────────────┴──────────────────────────────────────────────┘
Below 64rem the table of contents sits above the sections.
```

## Build a screen on it

```ts
readonly sections: readonly PortalSection[] = [
  { id: 'about', titleKey: 'portal.service-guide.about.title' },
  { id: 'documents', titleKey: 'portal.service-guide.documents.title' },
  { id: 'documents-optional', titleKey: 'portal.service-guide.documents.optional-title', depth: TocDepth.Subsection },
];
```

```html
<app-portal-content-pattern [sections]="sections">
  <dga-portal-page-header portalHeader [title]="'portal.service-guide.title' | t" [subtitle]="'portal.service-guide.subtitle' | t" />

  <section [portalSection]="'about'">
    <h2>{{ 'portal.service-guide.about.title' | t }}</h2>
    <p>{{ 'portal.service-guide.about.body' | t }}</p>
  </section>
  <section [portalSection]="'documents'">
    <h2>{{ 'portal.service-guide.documents.title' | t }}</h2>
    <dga-accordion>...</dga-accordion>
  </section>

  <p portalLastUpdated>{{ 'portal.common.last-updated' | t: { date: lastUpdated } }}</p>
</app-portal-content-pattern>
```

The pattern depends on no particular header or accordion: anything projected fits, so a page drops in
`dga-portal-page-header`, `dga-accordion`, `dga-card` or `dga-alert` as they ship.

## Contract

| Input | Meaning |
| --- | --- |
| `titleKey`, `subtitleKey` | A plain `h1` (and subtitle) when no `[portalHeader]` is projected; use one or the other |
| `sections` | `PortalSection[]`: `id` (slug `^[a-z][a-z0-9-]*$`), `titleKey`, optional `depth` (`TocDepth.Section = 1`, `Subsection = 2`) |
| `tocLabelKey` | Heading of the table of contents (default "On this page") |

Slots: `[portalHeader]`, default (the sections, each marked `[portalSection]="id"`), `[portalLastUpdated]`.

The table of contents shows from 2 sections. Invalid and repeated ids are left out (the first wins). Each section
gets the anchor id `section-<id>`, so it never clashes with other ids on the page, and `tabindex="-1"`.

## Rules

- Section ids are stable slugs: shared links (`/portal/service-guide#section-documents`) depend on them.
- All text is i18n (or, from phase 5, managed bilingual data); the pattern holds none of the page's text.
- Section headings start at `h2` under the page `h1`; a subsection uses `h3`.

## Accessibility

The table of contents is a `nav` named by its heading, an ordered list of real links (they keep the page path, so
"open in new tab" works). Following one does not navigate: it scrolls to the section (instantly when the reader
prefers reduced motion) and moves focus to it, so keyboard and screen-reader users land where sighted users look.
The entry of the section being read is `aria-current="location"`.
