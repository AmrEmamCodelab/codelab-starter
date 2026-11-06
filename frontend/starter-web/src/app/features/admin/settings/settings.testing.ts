import { SettingValueType, type RuntimeSetting } from './setting.models';

/** Fixtures shared by the settings specs. Spec-only: nothing in the app imports this file. */
export function runtimeSetting(overrides: Partial<RuntimeSetting> = {}): RuntimeSetting {
  return {
    key: 'portal.contact.daily-limit',
    group: 'portal',
    valueType: SettingValueType.Integer,
    value: '20',
    defaultValue: '20',
    descriptionAr: 'الحد اليومي',
    descriptionEn: 'Daily limit',
    updatedAt: null,
    ...overrides,
  };
}

/** One setting of each value type, in two groups, deliberately out of order. */
export function settingsOfEveryType(): RuntimeSetting[] {
  return [
    runtimeSetting({ key: 'security.session-idle', group: 'security', valueType: SettingValueType.DurationSeconds, value: '900', defaultValue: '900' }),
    runtimeSetting({ key: 'portal.welcome-text', group: 'portal', valueType: SettingValueType.Text, value: 'Hello', defaultValue: '' }),
    runtimeSetting({
      key: 'security.lockout-enabled',
      group: 'security',
      valueType: SettingValueType.Boolean,
      value: 'true',
      defaultValue: 'false',
      updatedAt: '2026-11-05T09:30:00Z',
    }),
    runtimeSetting(),
  ];
}
