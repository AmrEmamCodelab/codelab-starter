// Lint fixture: a clean page pattern that reaches core, shared and @codelab/ng-dga (the target of the
// feature-imports-patterns-layouts and showcase cases).
import { DgaButton } from '@codelab/ng-dga';
import { ApiService } from '../../core/api/api.service';
import { StatusLabel } from '../../shared/status-label/status-label';

export class RecordListPattern {
  readonly button = DgaButton;

  constructor(readonly api: ApiService, readonly label: StatusLabel) {}
}
