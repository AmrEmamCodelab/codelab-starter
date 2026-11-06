import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DgaButtonComponent, DgaCheckboxComponent, DgaTextInputComponent } from '@codelab/ng-dga';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import {
  AdminListPatternComponent,
  EditorDialogPatternComponent,
  FieldErrorPipe,
  ListCellDirective,
  ListRowActionsDirective,
  type ColumnDef,
} from '../../../patterns';
import { HasPermissionDirective } from '../../../shared/permissions/has-permission.directive';
import { ADMIN_TRANSLATED_CODES } from '../organizations/data/admin-api-error';
import { selectValue } from '../organizations/data/paged-list';
import { formatInstant } from '../users/activation-link/format-instant';
import { SettingValueHintKeys, settingDescription, settingGroupName, settingValueLabel } from './setting-value';
import { SettingValueType, type RuntimeSetting } from './setting.models';
import { ALL_GROUPS, SettingsPageStore } from './settings-page.store';
import { SettingsPermissionKeys } from './settings-permissions';
import { SettingsTranslationKeys } from './settings-translation-keys';

/** The name of the value field in the server's problem+json `errors` (the `PUT` body's property). */
const VALUE_FIELD = 'value';

/**
 * The runtime settings screen (`/admin/settings`, `settings.view`): the admin-list pattern over every setting, grouped
 * by group (ordered by group, then key) with a group filter and search, and the editor dialog (`settings.manage`)
 * whose field follows the setting's value type: text, whole number, on/off checkbox or duration in seconds. The
 * component renders the store's state and forwards intent.
 */
@Component({
  selector: 'app-settings-page',
  imports: [
    AdminListPatternComponent,
    DgaButtonComponent,
    DgaCheckboxComponent,
    DgaTextInputComponent,
    EditorDialogPatternComponent,
    FieldErrorPipe,
    HasPermissionDirective,
    ListCellDirective,
    ListRowActionsDirective,
    TranslatePipe,
  ],
  providers: [SettingsPageStore],
  templateUrl: './settings-page.component.html',
  styleUrl: './settings-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsPageComponent {
  private readonly language = inject(LanguageService);
  private readonly translations = inject(TranslationService);
  protected readonly store = inject(SettingsPageStore);
  protected readonly keys = SettingsTranslationKeys;
  protected readonly translatedCodes = ADMIN_TRANSLATED_CODES;
  protected readonly permissions = SettingsPermissionKeys;
  protected readonly ValueType = SettingValueType;
  protected readonly allGroups = ALL_GROUPS;
  protected readonly hintKeys = SettingValueHintKeys;
  protected readonly valueField = VALUE_FIELD;
  protected readonly selectValue = selectValue;

  protected readonly columns: readonly ColumnDef<RuntimeSetting>[] = [
    { key: 'group', headerKey: SettingsTranslationKeys.Columns.Group },
    { key: 'key', headerKey: SettingsTranslationKeys.Columns.Key },
    { key: 'value', headerKey: SettingsTranslationKeys.Columns.Value },
    { key: 'descriptionEn', headerKey: SettingsTranslationKeys.Columns.Description },
    { key: 'updatedAt', headerKey: SettingsTranslationKeys.Columns.UpdatedAt },
  ];
  protected readonly rowId = (row: RuntimeSetting): string => row.key;

  constructor() {
    void this.store.load();
  }

  /** A value as the list shows it, in the current language (on/off, "N seconds", the text itself). */
  protected valueText(valueType: SettingValueType, value: string): string {
    const label = settingValueLabel(valueType, value);
    return label.key === null ? label.text : this.translations.translate(label.key, { value: label.text });
  }

  /** A group's name in the current language (the filter options and the Group column). */
  protected groupName(group: string): string {
    return settingGroupName(
      group,
      (key) => this.translations.translate(key),
      (key) => this.translations.has(key),
    );
  }

  protected description(setting: RuntimeSetting): string {
    return settingDescription(setting, this.language.current());
  }

  protected updatedAt(iso: string): string {
    return formatInstant(iso, this.language.current());
  }
}
