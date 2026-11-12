/** Some settings of a level to save (`PATCH api/settings`): the others keep what the level has. */
export interface SettingChanges {
    /** New values by setting key (`player.seekSeconds`). */
    values?: Record<string, unknown>;
    /** Settings the level starts locking for the levels below. */
    lock?: string[];
    /** Settings the level stops locking. */
    unlock?: string[];
}
