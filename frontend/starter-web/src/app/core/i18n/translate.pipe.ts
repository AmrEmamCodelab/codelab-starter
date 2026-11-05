import { Pipe, inject, type PipeTransform } from '@angular/core';
import { TranslationService, type TranslationParams } from './translation.service';

/**
 * `{{ 'dashboard.title' | t }}` or `{{ 'greeting' | t: { name: user.displayName } }}`.
 *
 * Impure on purpose: the text changes when the language or a merged bundle changes, and the catalogue
 * reads are signal reads, so change detection re-evaluates only what depends on them.
 */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly translations = inject(TranslationService);

  transform(key: string, params?: TranslationParams): string {
    return this.translations.translate(key, params);
  }
}
