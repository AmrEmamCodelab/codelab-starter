# Phase 4 evidence

| Folder | What it holds |
| --- | --- |
| `baselines/` | Visual baselines (`<route>.<lang>.<width>.png`, ar and en at 1440 and 390 px) for the showcase routes (`showcase/routes.json`) and the public portal routes (`frontend/starter-web/e2e/routes/p4-public.json`) |
| `showcase/runs/` | Record (`--update`) and rerun (compare) reports for the showcase routes |
| `portal/runs/` | Record and rerun reports for the public portal routes |
| `showcase/i18n/` | The showcase `ar` and `en` key files |

## How the runs were recorded

All four runs come from one production build (`ng build --configuration production`) of the current tree, written
outside the repository. Only the build copy of `config.json` had `"showcase": true`, so the committed config keeps
the showcase off. The build was served from a local static server. The harness was `frontend/starter-web/e2e/visual.mjs`
(see its README).

1. `--update` against `showcase/routes.json`, then against `e2e/routes/p4-public.json`. Every changed image was
   looked at first. The old baselines differed for two reasons, and both are intended: the 390 px showcase pages had
   a horizontal overflow from the hijri date (416 and 432 px wide, now 390), and the field-label colour got darker
   in the contrast fix (0.11 to 0.16 % on the 390 px detail pages).
2. A compare run without `--update` against the same baselines. Both route files pass: showcase 52/52 and portal
   28/28 screenshots, worst difference 0.0000 %, and **0 serious or critical axe violations**.

Rerun after any UI change: start the app, then run the two compare commands from `frontend/starter-web` (the second
one with `--routes e2e/routes/p4-public.json`):

```powershell
node e2e/visual.mjs --base-url http://localhost:4280 --routes ../../docs/evidence/P4/showcase/routes.json --baseline-dir ../../docs/evidence/P4/baselines
```

The showcase routes need `"showcase": true` in the served `config.json`.

## Not here yet

`design-proof/` (the design-agent loop to at most 5 % against a target, with iterations and the dga-reviewer
sign-off) is not recorded. It waits for the platform owner to choose how a target may be used: a de-branded target,
masking written into the exit criterion, or targets kept outside the repository with only the reports in it.
