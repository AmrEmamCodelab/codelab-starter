import { Pipe, inject, type PipeTransform } from '@angular/core';
import { TranslationService, type TranslationParams } from './translation.service';

/**
 * `{{ keys.Title | t }}` or `{{ keys.Checked | t: { time: value } }}`. Impure on purpose: the text changes when the
 * language changes, and the catalogue reads are signal reads.
 */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly translations = inject(TranslationService);

  transform(key: string, params?: TranslationParams): string {
    return this.translations.translate(key, params);
  }
}
