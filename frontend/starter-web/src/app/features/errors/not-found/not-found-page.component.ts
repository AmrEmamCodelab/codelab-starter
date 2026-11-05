import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaCardComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { RoutePaths } from '../../../core/routing/route-paths';
import { appLink } from '../../../layouts/app-shell/shell-nav';

/** The 404 page: every URL no route claims (`**` in `app.routes.ts`). Renders without a session. */
@Component({
  selector: 'app-not-found-page',
  imports: [DgaCardComponent, RouterLink, TranslatePipe],
  templateUrl: './not-found-page.component.html',
  styleUrl: '../error-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFoundPageComponent {
  readonly homeLink = appLink(RoutePaths.Dashboard);
}
