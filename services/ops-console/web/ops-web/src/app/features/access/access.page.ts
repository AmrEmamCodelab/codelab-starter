import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { DgaButtonComponent, DgaCardComponent } from '@codelab/ng-dga';
import { CommonKeys } from '../../core/i18n/common-translation-keys';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { AccessRefusal, RouteDataKeys, RoutePaths } from '../../core/routing/route-paths';

/** Translation keys of the access pages. */
export const AccessKeys = {
  Unauthenticated: {
    Title: 'access.unauthenticated.title',
    Body: 'access.unauthenticated.body',
  },
  Forbidden: {
    Title: 'access.forbidden.title',
    Body: 'access.forbidden.body',
  },
  TryAgain: 'access.tryAgain',
} as const;

/** The title and body keys per refusal. */
export function accessTextKeys(refusal: AccessRefusal): { readonly title: string; readonly body: string } {
  const keys = refusal === AccessRefusal.Forbidden ? AccessKeys.Forbidden : AccessKeys.Unauthenticated;
  return { title: keys.Title, body: keys.Body };
}

/** The refusal a route declares in its data; anything else is treated as "sign-in required". */
export function refusalFrom(data: Readonly<Record<string, unknown>>): AccessRefusal {
  return data[RouteDataKeys.Refusal] === AccessRefusal.Forbidden ? AccessRefusal.Forbidden : AccessRefusal.Unauthenticated;
}

/**
 * The 401 and 403 pages, outside the shell (nothing else of the console is shown). 401: Windows authentication did not
 * reach the console (browser not on the domain, or the site not in the intranet zone). 403: the operator is signed in
 * but not in the operator group. "Try again" returns to the overview, which asks the BFF again.
 */
@Component({
  selector: 'app-access-page',
  imports: [DgaButtonComponent, DgaCardComponent, TranslatePipe],
  templateUrl: './access.page.html',
  styleUrl: './access.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccessPage {
  private readonly router = inject(Router);
  private readonly data = toSignal(inject(ActivatedRoute).data, { initialValue: {} });
  protected readonly language = inject(LanguageService);
  protected readonly common = CommonKeys;
  protected readonly keys = AccessKeys;
  readonly refusal = computed(() => refusalFrom(this.data()));
  readonly text = computed(() => accessTextKeys(this.refusal()));

  tryAgain(): void {
    void this.router.navigate([RoutePaths.Overview]);
  }
}
