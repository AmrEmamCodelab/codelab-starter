import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../../core/api/api.service';
import { API_PREFIX } from '../../../core/api/clients/api-paths';
import { quietContext } from '../../../core/http/http-context';
import type { RuntimeSetting, UpdateSettingBody } from './setting.models';

const SETTINGS_ROOT = `${API_PREFIX}/admin/settings`;

/** The runtime settings endpoints, relative to the API root (no leading slash). */
export const SettingsApiPaths = {
  Settings: SETTINGS_ROOT,
  setting: (key: string): string => `${SETTINGS_ROOT}/${encodeURIComponent(key)}`,
} as const;

/**
 * The runtime settings client (`settings.view` / `settings.manage`), a thin wrapper over `ApiService`. Every call is
 * quiet (no error dialog): the list shows its own error state with the reference code and the editor shows its
 * refusal inline (a type mismatch is a 400 with a `value` field error, an unknown key a 404). A 401 still ends the
 * session.
 */
@Injectable({ providedIn: 'root' })
export class AdminSettingsClient {
  private readonly api = inject(ApiService);

  /** Every setting, with its current and default value. */
  list(): Observable<readonly RuntimeSetting[]> {
    return this.api.get<readonly RuntimeSetting[]>(SettingsApiPaths.Settings, { context: quietContext() });
  }

  /** Sets a setting's value (its text form); answers the updated setting. */
  update(key: string, body: UpdateSettingBody): Observable<RuntimeSetting> {
    return this.api.put<RuntimeSetting>(SettingsApiPaths.setting(key), body, { context: quietContext() });
  }
}
