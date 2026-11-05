import { signal } from '@angular/core';
import type { DgaRadioOption, DgaRadioValue } from '@codelab/ng-dga';
import {
  ORGANIZATION_KINDS,
  OrganizationKindLabelKeys,
  isOrganizationKind,
  type CreateOrganizationBody,
  type Organization,
  type OrganizationId,
} from './data/organization.models';

/** Whether the editor creates a new record or edits an existing one. */
export enum EditorMode {
  Create = 1,
  Edit = 2,
}

/**
 * The organization editor's fields as signals the dialog binds two-way. The kind is a radio value (`dga-radio-group`
 * works in `string | number`) and becomes the enum only in {@link toBody}. Validation is the server's: an invalid
 * body comes back as problem+json field errors the dialog shows beside each field.
 */
export class OrganizationForm {
  readonly code = signal('');
  readonly nameEn = signal('');
  readonly nameAr = signal('');
  readonly kind = signal<DgaRadioValue | null>(null);
  /** Edit mode only: the active state to leave the organization in (applied through deactivate / reactivate). */
  readonly isActive = signal(true);

  private constructor(
    readonly mode: EditorMode,
    readonly id: OrganizationId | null,
    readonly wasActive: boolean,
  ) {}

  static blank(): OrganizationForm {
    return new OrganizationForm(EditorMode.Create, null, true);
  }

  static of(organization: Organization): OrganizationForm {
    const form = new OrganizationForm(EditorMode.Edit, organization.id, organization.isActive);
    form.code.set(organization.code);
    form.nameEn.set(organization.nameEn);
    form.nameAr.set(organization.nameAr);
    form.kind.set(organization.kind);
    form.isActive.set(organization.isActive);
    return form;
  }

  /** The create and update body (they share their fields): text trimmed, an unknown kind sent as null. */
  toBody(): CreateOrganizationBody {
    const kind = this.kind();
    return {
      code: this.code().trim(),
      nameEn: this.nameEn().trim(),
      nameAr: this.nameAr().trim(),
      kind: isOrganizationKind(kind) ? kind : null,
    };
  }

  /** True when an edit also changes the active state (a deactivate or reactivate call after the update). */
  activeChanged(): boolean {
    return this.mode === EditorMode.Edit && this.isActive() !== this.wasActive;
  }
}

/** The kind radio options with their (already translated) labels. */
export function organizationKindOptions(translate: (key: string) => string): readonly DgaRadioOption[] {
  return ORGANIZATION_KINDS.map((kind) => ({ value: kind, label: translate(OrganizationKindLabelKeys[kind]) }));
}
