/** The editable settings, nested as in EditableSettings ("browse.defaultPageSize"...). */
export type SettingsValues = Record<string, unknown>;

/** Where a setting's value comes from: the configuration, the server's settings page, a space, a user. */
export type SettingLevel = 'environment' | 'server' | 'tenant' | 'user';

/** How a setting shows on the settings page of a level. */
export interface SettingState {
    /** The most specific level that may set it. */
    scope: SettingLevel;
    source: SettingLevel;
    editable: boolean;
    /** This level may lock it for the levels below. */
    lockable: boolean;
    /** This level locked it. */
    locked: boolean;
    /** The level above that locked it. */
    lockedBy: SettingLevel | null;
    /** The value of the levels above. */
    inherited: unknown;
}

export interface SettingsView {
    values: SettingsValues;
    customized: boolean;
    level?: SettingLevel;
    /** Per setting key; absent from older servers. */
    keys?: Record<string, SettingState> | null;
}
