// Lint fixture: a shared building block that only reaches core.
import { ApiService } from '../../core/api/api.service';

export class StatusLabel {
  constructor(readonly api: ApiService) {}
}
