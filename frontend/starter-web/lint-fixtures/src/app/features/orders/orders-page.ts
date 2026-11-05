// Lint fixture: a clean feature file (the target of the shared-imports-feature case).
import { StatusLabel } from '../../shared/status-label/status-label';

export class OrdersPage {
  constructor(readonly label: StatusLabel) {}
}
