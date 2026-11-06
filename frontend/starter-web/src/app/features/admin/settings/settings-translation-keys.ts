/** The prefix of a setting group's name key: `admin.settings.groups.<group>`. */
export const SETTING_GROUP_KEY_PREFIX = 'admin.settings.groups.';

/** Every text of the runtime settings screen (`admin.settings.*`), in `ar.json` and `en.json`. */
export const SettingsTranslationKeys = {
  Title: 'admin.settings.title',
  Intro: 'admin.settings.intro',
  Search: 'admin.settings.search',
  Empty: 'admin.settings.empty',
  Columns: {
    Key: 'admin.settings.columns.key',
    Group: 'admin.settings.columns.group',
    Value: 'admin.settings.columns.value',
    Description: 'admin.settings.columns.description',
    UpdatedAt: 'admin.settings.columns.updated-at',
  },
  /** The name of each seeded setting group (`SettingsSeed.*Group` on the server); key = prefix + group. */
  Groups: {
    Notifications: 'admin.settings.groups.notifications',
    Portal: 'admin.settings.groups.portal',
    Session: 'admin.settings.groups.session',
  },
  Filters: {
    Group: 'admin.settings.filters.group',
    AllGroups: 'admin.settings.filters.all-groups',
  },
  Values: {
    On: 'admin.settings.values.on',
    Off: 'admin.settings.values.off',
    Duration: 'admin.settings.values.duration',
    Empty: 'admin.settings.values.empty',
    NeverChanged: 'admin.settings.values.never-changed',
  },
  Actions: {
    Edit: 'admin.settings.actions.edit',
  },
  Editor: {
    Title: 'admin.settings.editor.title',
    Save: 'admin.settings.editor.save',
    Value: 'admin.settings.editor.value',
    Enabled: 'admin.settings.editor.enabled',
    TextHint: 'admin.settings.editor.text-hint',
    IntegerHint: 'admin.settings.editor.integer-hint',
    DurationHint: 'admin.settings.editor.duration-hint',
    Default: 'admin.settings.editor.default',
    ResetToDefault: 'admin.settings.editor.reset-to-default',
    IntegerInvalid: 'admin.settings.editor.integer-invalid',
    DurationInvalid: 'admin.settings.editor.duration-invalid',
  },
} as const;
