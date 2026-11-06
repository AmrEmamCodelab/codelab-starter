import { signal, computed } from '@angular/core';
import type { AppLanguage } from '../../../core/i18n/language';
import { AppLanguages } from '../../../core/i18n/language';
import { SettingValueType, type RuntimeSetting } from './setting.models';
import { SETTING_GROUP_KEY_PREFIX, SettingsTranslationKeys } from './settings-translation-keys';

/** The invariant text forms of a boolean setting (the backend's `bool` round-trip form, lower case). */
export const BooleanSettingText = {
  True: 'true',
  False: 'false',
} as const;

/** The bounds of an integer setting: the backend reads it as an `int` (32-bit). */
export const INTEGER_SETTING_MIN = -2_147_483_648;
export const INTEGER_SETTING_MAX = 2_147_483_647;
/** The bounds of a duration setting in whole seconds: never negative, at most an `int` of seconds. */
export const DURATION_SETTING_MIN_SECONDS = 0;

const WHOLE_NUMBER = /^-?\d+$/;

/** A setting value as the list shows it: a translation key with `{{value}}`, or the raw text. */
export interface SettingValueLabel {
  /** The key to translate, or null to show `text` as it is. */
  readonly key: string | null;
  readonly text: string;
}

/**
 * The name a setting group shows under in the current language: its `admin.settings.groups.<group>` text when the
 * bundles have one, else the group as the server sent it (a group added later without its texts stays readable).
 */
export function settingGroupName(group: string, translate: (key: string) => string, has: (key: string) => boolean): string {
  const key = `${SETTING_GROUP_KEY_PREFIX}${group}`;
  return has(key) ? translate(key) : group;
}

/** Whether a stored boolean text means "on" (the server writes lower case; any case reads the same). */
export function isOn(value: string): boolean {
  return value.trim().toLowerCase() === BooleanSettingText.True;
}

/** How the list shows `value` of a setting of `valueType`. */
export function settingValueLabel(valueType: SettingValueType, value: string): SettingValueLabel {
  if (valueType === SettingValueType.Boolean) {
    return { key: isOn(value) ? SettingsTranslationKeys.Values.On : SettingsTranslationKeys.Values.Off, text: value };
  }
  if (valueType === SettingValueType.DurationSeconds) {
    return { key: SettingsTranslationKeys.Values.Duration, text: value };
  }
  if (value === '') {
    return { key: SettingsTranslationKeys.Values.Empty, text: value };
  }
  return { key: null, text: value };
}

/** The setting's description in `language`. */
export function settingDescription(setting: RuntimeSetting, language: AppLanguage): string {
  return language === AppLanguages.Arabic ? setting.descriptionAr : setting.descriptionEn;
}

function wholeNumberIn(text: string, min: number, max: number): boolean {
  if (!WHOLE_NUMBER.test(text)) {
    return false;
  }
  const parsed = Number(text);
  return parsed >= min && parsed <= max;
}

/**
 * The client check of a typed value (it saves a round trip; the server checks again and answers 400): the
 * translation key of the problem, or null when `text` fits `valueType`. Text and booleans always fit.
 */
export function settingValueProblem(valueType: SettingValueType, text: string): string | null {
  if (valueType === SettingValueType.Integer) {
    return wholeNumberIn(text, INTEGER_SETTING_MIN, INTEGER_SETTING_MAX) ? null : SettingsTranslationKeys.Editor.IntegerInvalid;
  }
  if (valueType === SettingValueType.DurationSeconds) {
    return wholeNumberIn(text, DURATION_SETTING_MIN_SECONDS, INTEGER_SETTING_MAX)
      ? null
      : SettingsTranslationKeys.Editor.DurationInvalid;
  }
  return null;
}

/** The hint under the value field for each value type. */
export const SettingValueHintKeys: Readonly<Record<SettingValueType, string>> = {
  [SettingValueType.Text]: SettingsTranslationKeys.Editor.TextHint,
  [SettingValueType.Integer]: SettingsTranslationKeys.Editor.IntegerHint,
  [SettingValueType.Boolean]: SettingsTranslationKeys.Editor.TextHint,
  [SettingValueType.DurationSeconds]: SettingsTranslationKeys.Editor.DurationHint,
};

/**
 * The editor's draft of one setting: the text field (text, integer, duration in seconds) or the checkbox (boolean)
 * as signals, the value it would save, and the client check. Number fields are trimmed; a text value is kept exactly
 * as typed (spaces may matter to the consumer of the setting).
 */
export class SettingDraft {
  readonly text = signal('');
  readonly checked = signal(false);

  /** The invariant text the draft saves. */
  readonly value = computed(() => {
    switch (this.setting.valueType) {
      case SettingValueType.Boolean:
        return this.checked() ? BooleanSettingText.True : BooleanSettingText.False;
      case SettingValueType.Text:
        return this.text();
      default:
        return this.text().trim();
    }
  });

  /** The translation key of the client-side problem, or null. */
  readonly problem = computed(() => settingValueProblem(this.setting.valueType, this.value()));
  readonly changed = computed(() => this.value() !== this.setting.value);
  readonly isDefault = computed(() => this.value() === this.setting.defaultValue);

  constructor(readonly setting: RuntimeSetting) {
    this.load(setting.value);
  }

  /** Puts the default value back in the field (saved only on confirm). */
  resetToDefault(): void {
    this.load(this.setting.defaultValue);
  }

  private load(value: string): void {
    this.text.set(value);
    this.checked.set(isOn(value));
  }
}
