import { signal } from '@angular/core';
import type { DgaRadioOption, DgaRadioValue } from '@codelab/ng-dga';
import type { OrganizationId, OrganizationOption } from '../organizations/data/organization.models';
import { EditorMode } from '../organizations/organization-form';
import {
  ACCOUNT_BINDINGS,
  AccountBinding,
  AccountBindingLabelKeys,
  USER_KINDS,
  UserKind,
  UserKindLabelKeys,
  isAccountBinding,
  isUserKind,
  organizationKindFor,
  type AdminUser,
  type CreateUserBody,
  type UpdateUserBody,
  type UserId,
} from './data/user.models';

/**
 * The user editor's fields as signals the dialog binds two-way. Kind and binding are radio values
 * (`dga-radio-group` works in `string | number`) and become the enums only in the bodies. Validation is the
 * server's (one owner): an invalid body comes back as problem+json field errors shown beside each field.
 *
 * A new account starts as an internal, password-bound user without an organization.
 */
export class UserForm {
  readonly userName = signal('');
  readonly displayName = signal('');
  readonly email = signal('');
  readonly loginName = signal('');
  readonly upn = signal('');
  readonly kind = signal<DgaRadioValue | null>(UserKind.InternalUser);
  readonly binding = signal<DgaRadioValue | null>(AccountBinding.Password);
  readonly organizationId = signal<OrganizationId | null>(null);

  private constructor(
    readonly mode: EditorMode,
    readonly id: UserId | null,
    /** The organization the account already belongs to (kept selectable even when it is deactivated). */
    readonly currentOrganization: OrganizationOption | null,
  ) {}

  static blank(): UserForm {
    return new UserForm(EditorMode.Create, null, null);
  }

  static of(user: AdminUser): UserForm {
    const current =
      user.organizationId === null
        ? null
        : { id: user.organizationId, name: user.organizationName ?? '', kind: organizationKindFor(asUserKind(user.kind)) };
    const form = new UserForm(EditorMode.Edit, user.id, current);
    form.userName.set(user.userName);
    form.displayName.set(user.displayName);
    form.email.set(user.email ?? '');
    form.loginName.set(user.loginName ?? '');
    form.upn.set(user.upn ?? '');
    form.kind.set(user.kind);
    form.binding.set(user.binding);
    form.organizationId.set(user.organizationId);
    return form;
  }

  toCreateBody(): CreateUserBody {
    return { userName: this.userName().trim(), ...this.toUpdateBody() };
  }

  /** The fields an edit sends (the user name never changes): optional text trimmed, blank as null. */
  toUpdateBody(): UpdateUserBody {
    const kind = this.kind();
    const binding = this.binding();
    return {
      displayName: this.displayName().trim(),
      email: optionalText(this.email()),
      loginName: optionalText(this.loginName()),
      upn: optionalText(this.upn()),
      kind: isUserKind(kind) ? kind : null,
      binding: isAccountBinding(binding) ? binding : null,
      organizationId: this.organizationId(),
    };
  }

  /** True when the account signs in through a directory or identity provider (no activation link). */
  usesDirectory(): boolean {
    return this.binding() !== AccountBinding.Password;
  }
}

/** The organizations a user of the form's kind may pick: matching kind, plus the one it already belongs to. */
export function organizationChoices(form: UserForm, organizations: readonly OrganizationOption[]): readonly OrganizationOption[] {
  const kind = organizationKindFor(asUserKind(form.kind()));
  const matching = organizations.filter((organization) => organization.kind === kind);
  const current = form.currentOrganization;
  if (current === null || current.kind !== kind || matching.some((organization) => organization.id === current.id)) {
    return matching;
  }
  return [current, ...matching];
}

/** The kind radio options with their (already translated) labels. */
export function userKindOptions(translate: (key: string) => string): readonly DgaRadioOption[] {
  return USER_KINDS.map((kind) => ({ value: kind, label: translate(UserKindLabelKeys[kind]) }));
}

/** The binding radio options with their (already translated) labels. */
export function bindingOptions(translate: (key: string) => string): readonly DgaRadioOption[] {
  return ACCOUNT_BINDINGS.map((binding) => ({ value: binding, label: translate(AccountBindingLabelKeys[binding]) }));
}

function asUserKind(value: unknown): UserKind {
  return isUserKind(value) ? value : UserKind.InternalUser;
}

function optionalText(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}
