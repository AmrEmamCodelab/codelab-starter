import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { RequesterShellComponent } from '../../layouts/requester-shell/requester-shell.component';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { ShowcasePageId } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import { provideShowcaseNotifications } from './showcase-notifications';
import { provideShowcaseRequesterFrame } from './showcase-session';

/**
 * The signed-in requester shell in a frame: a sample user (user menu), a sample navigation with a submenu, and the
 * notifications bell on the showcase source, all provided on this page only. The shell brings its own `<main>`, so
 * the showcase frame renders no landmark here; the shell's page outlet stays empty.
 */
@Component({
  selector: 'app-showcase-requester-shell-page',
  imports: [RequesterShellComponent, ShowcaseFrameComponent, TranslatePipe],
  providers: [provideShowcaseNotifications(), ...provideShowcaseRequesterFrame()],
  templateUrl: './requester-shell-page.component.html',
  styleUrl: './layouts-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RequesterShellPageComponent {
  protected readonly pageId = ShowcasePageId.RequesterShell;
  protected readonly keys = ShowcaseTranslationKeys;
}
