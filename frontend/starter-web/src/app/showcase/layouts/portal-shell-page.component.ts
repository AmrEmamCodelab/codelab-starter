import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { PortalShellComponent } from '../../layouts/portal-shell/portal-shell.component';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { ShowcasePageId } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';

/**
 * The public portal shell in a frame, as a visitor sees it (signed out, so the sign-in link shows). The shell brings
 * its own `<main>`, so the showcase frame renders no landmark of its own here; the shell's page outlet stays empty.
 */
@Component({
  selector: 'app-showcase-portal-shell-page',
  imports: [PortalShellComponent, ShowcaseFrameComponent, TranslatePipe],
  templateUrl: './portal-shell-page.component.html',
  styleUrl: './layouts-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PortalShellPageComponent {
  protected readonly pageId = ShowcasePageId.PortalShell;
  protected readonly keys = ShowcaseTranslationKeys;
}
