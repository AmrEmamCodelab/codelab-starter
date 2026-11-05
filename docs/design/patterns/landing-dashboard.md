# Pattern: landing dashboard

**Use it for** the first page after sign-in: a greeting or hero, a row of key figures (KPI tiles) that link to the
lists behind them, quick actions, and the user's recent items. Requesters and staff each get their own screen on it.

**Do not use it for** the public home page content (use the [portal content page](portal-content.md) frame or the
portal landing) or for working through a list (use the [work queue](work-queue.md)).

| Part | Selector | Code |
| --- | --- | --- |
| Dashboard | `app-landing-dashboard-pattern` | `src/app/patterns/landing-dashboard/landing-dashboard-pattern.component.*` |
| Tiles and actions | `KpiTile`, `QuickAction`, `formatKpiValue`, `hasKpiValue`, `uniqueTiles` | `src/app/patterns/landing-dashboard/kpi-tiles.ts` |
| Keys | `LandingDashboardTranslationKeys` (`patterns.landing-dashboard.*`) | `src/app/patterns/landing-dashboard/landing-dashboard-translation-keys.ts` |

## Anatomy

```
┌──────────────────────────────────────────────────────────────────┐
│ Good morning, Sara                                               │  greetingKey + greetingParams (h1)
│ <dga-hero> or a welcome card                                     │  [landingHero]
├──────────────────────────────────────────────────────────────────┤
│ At a glance                                                      │
│ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐              │  tiles: KpiTile[] (link → whole tile is a link)
│ │ (icon)   │ │ 1,250    │ │ 98%      │ │ Loading  │              │
│ │ 12       │ │ Closed   │ │ On time  │ │ Pending  │              │
│ └──────────┘ └──────────┘ └──────────┘ └──────────┘              │
│ Quick actions                                                    │
│ [+ Submit a new request            ›] [Track my requests      ›] │  quickActions: QuickAction[]
│ Recent                                                           │
│ <the screen's short list>                                        │  [landingRecent]
└──────────────────────────────────────────────────────────────────┘
```

## Build a screen on it

```ts
readonly tiles = computed<readonly KpiTile[]>(() => [
  { labelKey: 'home.kpis.open', value: this.store.summary()?.open ?? null, icon: 'file', link: this.requestsLink },
  { labelKey: 'home.kpis.closed', value: this.store.summary()?.closed ?? null, icon: 'check' },
]);
readonly quickActions = computed<readonly QuickAction[]>(() =>
  ALL_QUICK_ACTIONS.filter((action) => this.permissions.has(action.permission)));   // the screen filters by permission
```

```html
<app-landing-dashboard-pattern
  greetingKey="home.greeting" [greetingParams]="{ name: session.displayName() }"
  [tiles]="tiles()" [quickActions]="quickActions()" [loading]="store.loading()">
  <dga-card landingHero [heading]="'home.welcome.title' | t">...</dga-card>
  <ul landingRecent>...</ul>
</app-landing-dashboard-pattern>
```

Links are router paths built from the route constants (`RoutePaths`), never literals.

## Contract

| Input | Meaning |
| --- | --- |
| `greetingKey`, `greetingParams` | A plain `h1`; leave `greetingKey` null when the hero carries the page heading |
| `tiles` | `KpiTile[]`: `labelKey` (unique; tracks the tile), `value` (number formatted for the language, string as given, null = not loaded), optional `icon`, `link` |
| `quickActions` | `QuickAction[]`: `labelKey`, `link`, optional `icon`; none hides the section |
| `loading` | Every tile shows the "loading" placeholder and the row is `aria-busy` |
| `kpisLabelKey`, `quickActionsLabelKey`, `recentLabelKey` | Section headings; `recentLabelKey = null` hides the recent section |

Slots: `[landingHero]`, `[landingRecent]`.

## Rules

- Figures come from one summary endpoint of the screen's module; the pattern only formats them.
- Tiles and quick actions are filtered by permission in the screen before binding; a tile links only to a page the
  user may open.
- No raw text and no translated text in data: labels are translation keys.

## Accessibility

Each section is named by its `h2`; tiles and actions are lists; a linked tile is one link whose text reads the
figure and its label together; icons are decorative; the forward arrow of a quick action mirrors in RTL through the
icon manifest. While loading, the tile row is `aria-busy`.
