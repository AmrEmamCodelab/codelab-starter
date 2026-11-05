import { HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../api.service';
import { quietContext } from '../../http/http-context';
import type { AppLanguage } from '../../i18n/language';
import { ApiPaths, ApiQueryParams } from './api-paths';
import type { TranslationTree } from './api.models';

/** The session-gated half of the translation catalogue. */
@Injectable({ providedIn: 'root' })
export class TranslationsClient {
  private readonly api = inject(ApiService);

  protectedBundle(language: AppLanguage): Observable<TranslationTree> {
    return this.api.get<TranslationTree>(ApiPaths.ProtectedTranslations, {
      params: new HttpParams().set(ApiQueryParams.Language, language),
      context: quietContext(),
    });
  }
}
