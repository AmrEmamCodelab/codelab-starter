import { describe, expect, it } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import {
  BooleanSettingText,
  INTEGER_SETTING_MAX,
  INTEGER_SETTING_MIN,
  SettingDraft,
  isOn,
  settingDescription,
  settingGroupName,
  settingValueLabel,
  settingValueProblem,
} from './setting-value';
import { SettingValueType } from './setting.models';
import { SettingsTranslationKeys } from './settings-translation-keys';
import { runtimeSetting } from './settings.testing';

const Keys = SettingsTranslationKeys;

describe('settingGroupName', () => {
  const translate = (key: string): string => `t:${key}`;

  it('names a group by its translation when the bundles have one', () => {
    expect(settingGroupName('session', translate, (key) => key === SettingsTranslationKeys.Groups.Session)).toBe(
      `t:${SettingsTranslationKeys.Groups.Session}`,
    );
  });

  it('shows a group without a translation as the server sent it', () => {
    expect(settingGroupName('reports', translate, () => false)).toBe('reports');
  });
});

describe('settingValueLabel', () => {
  it('shows booleans as on / off, whatever the case', () => {
    expect(settingValueLabel(SettingValueType.Boolean, 'true').key).toBe(Keys.Values.On);
    expect(settingValueLabel(SettingValueType.Boolean, 'True').key).toBe(Keys.Values.On);
    expect(settingValueLabel(SettingValueType.Boolean, 'false').key).toBe(Keys.Values.Off);
  });

  it('shows a duration as seconds and an empty text as empty', () => {
    expect(settingValueLabel(SettingValueType.DurationSeconds, '900')).toEqual({ key: Keys.Values.Duration, text: '900' });
    expect(settingValueLabel(SettingValueType.Text, '')).toEqual({ key: Keys.Values.Empty, text: '' });
  });

  it('shows text and integers as they are', () => {
    expect(settingValueLabel(SettingValueType.Text, 'Hello')).toEqual({ key: null, text: 'Hello' });
    expect(settingValueLabel(SettingValueType.Integer, '-3')).toEqual({ key: null, text: '-3' });
  });
});

describe('settingValueProblem', () => {
  it('accepts whole numbers in the int range for an integer', () => {
    expect(settingValueProblem(SettingValueType.Integer, '0')).toBeNull();
    expect(settingValueProblem(SettingValueType.Integer, '-12')).toBeNull();
    expect(settingValueProblem(SettingValueType.Integer, String(INTEGER_SETTING_MAX))).toBeNull();
    expect(settingValueProblem(SettingValueType.Integer, String(INTEGER_SETTING_MIN))).toBeNull();
  });

  it.each(['', '1.5', 'abc', '1e3', ' 1', String(INTEGER_SETTING_MAX + 1)])('refuses %j as an integer', (text) => {
    expect(settingValueProblem(SettingValueType.Integer, text)).toBe(Keys.Editor.IntegerInvalid);
  });

  it('accepts zero and positive whole seconds for a duration, never a negative', () => {
    expect(settingValueProblem(SettingValueType.DurationSeconds, '0')).toBeNull();
    expect(settingValueProblem(SettingValueType.DurationSeconds, '3600')).toBeNull();
    expect(settingValueProblem(SettingValueType.DurationSeconds, '-1')).toBe(Keys.Editor.DurationInvalid);
    expect(settingValueProblem(SettingValueType.DurationSeconds, '')).toBe(Keys.Editor.DurationInvalid);
  });

  it('accepts any text and any boolean', () => {
    expect(settingValueProblem(SettingValueType.Text, '')).toBeNull();
    expect(settingValueProblem(SettingValueType.Boolean, 'true')).toBeNull();
  });
});

describe('isOn and settingDescription', () => {
  it('reads the invariant boolean text', () => {
    expect(isOn(BooleanSettingText.True)).toBe(true);
    expect(isOn(' TRUE ')).toBe(true);
    expect(isOn(BooleanSettingText.False)).toBe(false);
    expect(isOn('yes')).toBe(false);
  });

  it('picks the description of the language', () => {
    const setting = runtimeSetting();
    expect(settingDescription(setting, AppLanguages.Arabic)).toBe(setting.descriptionAr);
    expect(settingDescription(setting, AppLanguages.English)).toBe(setting.descriptionEn);
  });
});

describe('SettingDraft', () => {
  it('starts from the current value and is unchanged', () => {
    const draft = new SettingDraft(runtimeSetting({ value: '20' }));
    expect(draft.text()).toBe('20');
    expect(draft.value()).toBe('20');
    expect(draft.changed()).toBe(false);
    expect(draft.problem()).toBeNull();
  });

  it('trims a number and checks it', () => {
    const draft = new SettingDraft(runtimeSetting({ value: '20' }));
    draft.text.set(' 30 ');
    expect(draft.value()).toBe('30');
    expect(draft.changed()).toBe(true);
    draft.text.set('thirty');
    expect(draft.problem()).toBe(Keys.Editor.IntegerInvalid);
  });

  it('keeps a text value exactly as typed', () => {
    const draft = new SettingDraft(runtimeSetting({ valueType: SettingValueType.Text, value: 'a' }));
    draft.text.set('  spaced  ');
    expect(draft.value()).toBe('  spaced  ');
  });

  it('saves a boolean as its invariant text from the checkbox', () => {
    const draft = new SettingDraft(runtimeSetting({ valueType: SettingValueType.Boolean, value: 'false', defaultValue: 'false' }));
    expect(draft.checked()).toBe(false);
    draft.checked.set(true);
    expect(draft.value()).toBe(BooleanSettingText.True);
    expect(draft.changed()).toBe(true);
    expect(draft.isDefault()).toBe(false);
  });

  it('puts the default back on reset', () => {
    const draft = new SettingDraft(
      runtimeSetting({ valueType: SettingValueType.Boolean, value: 'true', defaultValue: 'false' }),
    );
    expect(draft.isDefault()).toBe(false);
    draft.resetToDefault();
    expect(draft.checked()).toBe(false);
    expect(draft.value()).toBe(BooleanSettingText.False);
    expect(draft.isDefault()).toBe(true);
  });
});
