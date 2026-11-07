import { ChangeDetectionStrategy, Component, computed, inject, type OnInit } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { DgaButtonComponent, DgaHeaderComponent, DgaIconComponent } from '@codelab/ng-dga';
import { CommonKeys } from '../../core/i18n/common-translation-keys';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { RoutePaths } from '../../core/routing/route-paths';
import { OperatorStore } from '../../core/session/operator.store';
import { CertificateAlertComponent } from '../../shared/certificates/certificate-alert.component';
import { CertificatesStore } from '../../shared/certificates/certificates.store';
import { shellNavItems } from './shell-navigation';

/**
 * The console frame: the DGA header (screens, the signed-in operator, the language switch), the certificate expiry
 * banner and the routed screen. Opening it asks the BFF who the operator is (a 401 or 403 leaves for the access page)
 * and loads the certificates once for the banner.
 */
@Component({
  selector: 'app-shell',
  imports: [CertificateAlertComponent, DgaButtonComponent, DgaHeaderComponent, DgaIconComponent, RouterOutlet, TranslatePipe],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShellComponent implements OnInit {
  private readonly translations = inject(TranslationService);
  protected readonly operator = inject(OperatorStore);
  protected readonly certificates = inject(CertificatesStore);
  protected readonly language = inject(LanguageService);
  protected readonly keys = CommonKeys;
  protected readonly homeLink = `/${RoutePaths.Overview}`;
  readonly navItems = computed(() => shellNavItems((key) => this.translations.translate(key)));

  ngOnInit(): void {
    this.operator.load();
    this.certificates.load();
  }
}
