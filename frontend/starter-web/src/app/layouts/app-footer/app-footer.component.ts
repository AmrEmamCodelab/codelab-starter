import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { APP_CLOCK } from '../../core/session/clock';
import { copyrightYear, FOOTER_LINKS } from './footer-links';
import { FooterTranslationKeys } from './footer-translation-keys';
import { LayoutBrand } from './layout-brand';

/**
 * The site footer both frames render below the page: the project's logo and name (from the branding block, see
 * {@link LayoutBrand}), links to the portal's privacy, terms and contact pages, and the copyright line. The year comes
 * from the injected `APP_CLOCK`, so specs pin it. Input-free: everything it shows is app-wide.
 */
@Component({
  selector: 'app-footer',
  imports: [RouterLink, TranslatePipe],
  templateUrl: './app-footer.component.html',
  styleUrl: './app-footer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppFooterComponent {
  protected readonly brand = inject(LayoutBrand);
  protected readonly keys = FooterTranslationKeys;
  protected readonly links = FOOTER_LINKS;
  readonly year = copyrightYear(inject(APP_CLOCK)());
}
