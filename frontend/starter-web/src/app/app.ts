import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ErrorDialogComponent } from './layouts/error-dialog/error-dialog.component';

/**
 * The root component: the router outlet and the app-wide error dialog. Pages and layouts are chosen by
 * `app.routes.ts`; the root renders nothing else.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ErrorDialogComponent],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {}
