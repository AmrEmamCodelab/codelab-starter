/** How a runtime setting's value is read, as the backend `SettingValueType` (integers on the wire). */
export enum SettingValueType {
  Text = 1,
  Integer = 2,
  Boolean = 3,
  DurationSeconds = 4,
}

/** Every value type, for parsing a wire value. */
export const SETTING_VALUE_TYPES: readonly SettingValueType[] = [
  SettingValueType.Text,
  SettingValueType.Integer,
  SettingValueType.Boolean,
  SettingValueType.DurationSeconds,
];

/**
 * One runtime setting as `GET api/admin/settings` sends it. `value` and `defaultValue` are the invariant text form
 * (`true` / `false` for a boolean, whole seconds for a duration); `updatedAt` is an ISO 8601 UTC instant, null while
 * the setting still has its default.
 */
export interface RuntimeSetting {
  readonly key: string;
  readonly group: string;
  readonly valueType: SettingValueType;
  readonly value: string;
  readonly defaultValue: string;
  readonly descriptionAr: string;
  readonly descriptionEn: string;
  readonly updatedAt: string | null;
}

/** The body of `PUT api/admin/settings/{key}`. */
export interface UpdateSettingBody {
  readonly value: string;
}
