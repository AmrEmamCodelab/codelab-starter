import { Pipe, inject, type PipeTransform } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { TranslationService } from '../../core/i18n/translation.service';
import { fieldErrorsFrom, firstFieldError, type FieldErrorMap } from './field-errors';

/**
 * The translated first error of one field, or null: what a DGA form control's `error` input takes. Takes the failed
 * save's `ApiError` (its problem+json field errors) or a {@link FieldErrorMap}.
 *
 * Impure for the same reason as the `t` pipe: the text follows the language.
 *
 * @example
 * <dga-text-input [label]="'admin.users.email' | t" [(value)]="email" [error]="saveError() | fieldError: 'email'" />
 */
@Pipe({ name: 'fieldError', pure: false })
export class FieldErrorPipe implements PipeTransform {
  private readonly translations = inject(TranslationService);

  transform(errors: ApiError | FieldErrorMap | null | undefined, field: string): string | null {
    const key = errors ? firstFieldError(toFieldErrorMap(errors), field) : null;
    return key === null ? null : this.translations.translate(key);
  }
}

function isApiError(errors: ApiError | FieldErrorMap): errors is ApiError {
  return Array.isArray((errors as ApiError).fieldErrors) && typeof (errors as ApiError).status === 'number';
}

function toFieldErrorMap(errors: ApiError | FieldErrorMap): FieldErrorMap {
  return isApiError(errors) ? fieldErrorsFrom(errors) : errors;
}
