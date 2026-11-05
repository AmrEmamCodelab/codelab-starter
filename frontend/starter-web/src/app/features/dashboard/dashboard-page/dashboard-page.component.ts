import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DgaCardComponent } from '@codelab/ng-dga';
import { AuthService } from '../../../core/auth/auth.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';

/**
 * The landing page inside the shell, gated by `dashboard.view` in `app.routes.ts`. Empty in P2: it greets
 * the user and is where later phases put their summary cards.
 */
@Component({
  selector: 'app-dashboard-page',
  imports: [DgaCardComponent, TranslatePipe],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardPageComponent {
  readonly user = inject(AuthService).user;
}
