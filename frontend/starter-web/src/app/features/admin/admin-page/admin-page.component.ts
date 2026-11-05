import { ChangeDetectionStrategy, Component } from '@angular/core';
import { DgaCardComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';

/**
 * The access administration area, gated by `access.admin.view` in `app.routes.ts`. A placeholder in P2;
 * the user and role screens arrive with the Access module's admin endpoints.
 */
@Component({
  selector: 'app-admin-page',
  imports: [DgaCardComponent, TranslatePipe],
  templateUrl: './admin-page.component.html',
  styleUrl: './admin-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminPageComponent {}
