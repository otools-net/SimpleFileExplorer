/*
 * The settings' tabs of grouped fields (one file per section in tabs/), each shown as a sub-page of a section of the
 * administration page (see admin/admin-sections.ts). Each field maps to a key of the API's settings (e.g.
 * "browse.defaultPageSize"); validation errors come back keyed the same way, or by a group's `key` for errors about
 * several of its fields. The library root, the cache folder, the database, ffmpeg and the system commands are left out
 * on purpose: they are only set in the server's configuration files.
 */

import { CONNECTION_SETTINGS } from '../connections/connection-settings';
import type { ServerMode } from '../core/access.models';
import { TOKEN_SETTINGS } from '../tokens/token-settings';
import { APPEARANCE_SETTINGS } from './tabs/appearance-settings';
import { BROWSING_SETTINGS, SEARCH_SETTINGS } from './tabs/browsing-settings';
import { EDITOR_SETTINGS, FILE_SETTINGS } from './tabs/files-settings';
import { TAG_SETTINGS, TRACKING_SETTINGS } from './tabs/library-settings';
import { PLAYER_SETTINGS, STREAMING_SETTINGS } from './tabs/playback-settings';
import { THUMBNAIL_SETTINGS, TRICKPLAY_SETTINGS } from './tabs/preview-settings';
import { FILE_TYPE_SETTINGS, INDEXING_SETTINGS, SKIPPED_SETTINGS } from './tabs/scanning-settings';
import { KEEP_ALIVE_SETTINGS, LOG_SETTINGS, STARTUP_SETTINGS, TASK_SETTINGS } from './tabs/system-settings';

export type GroupAction = 'clearHistory' | 'clearSearchHistory' | 'trickplayStatus' | 'browserTheme';

/**
 * Texts are marked for translation (`$localize`) and follow agents/docs/development/ui-texts.md: the `hint` under a
 * field keeps to a sentence or two (its effect, unit and range); the why, examples and exceptions go in `help`, shown
 * behind a "?" (`sfe-help`).
 */
interface FieldBase {
    key: string;
    label: string;
    hint: string;
    help?: string;
    placeholder?: string;
    /** Used only in this mode: greyed out, with why, on a server in the other one. */
    onlyIn?: ServerMode;
}

export type SettingField = FieldBase &
    (
        | { type: 'number'; min: number; max: number }
        | { type: 'checkbox' | 'text' | 'list' | 'extensions' }
        /** `numeric`: its values are numbers (sent as numbers). */
        | { type: 'select'; options: [string, string][]; numeric?: boolean }
        /** An unsaved choice filling the `fills` fields from `presets` ([label, name, link]). */
        | { type: 'preset'; presets: [string, string, string][]; fills: { url: string; name: string } }
    );

export interface SettingGroup {
    title: string;
    /** Key of the errors about several of its fields. */
    key?: string;
    hint?: string;
    action?: GroupAction;
    fields: SettingField[];
}

export interface SettingTab {
    id: string;
    title: string;
    intro?: string;
    groups: SettingGroup[];
}

// Settings shown in the simple view, the default until "Show all settings" is ticked (remembered per browser). Each
// stays in its own group and tab; groups with none of these are hidden. Add or remove keys of TABS freely.
export const SIMPLE_SETTINGS: ReadonlySet<string> = new Set([
    'appearance.theme',
    'reading.theme',
    'reading.code.font',
    'reading.code.size',
    'reading.prose.font',
    'reading.prose.size',
    'browse.defaultPageSize',
    'browse.folderPictures',
    'browse.liveChanges',
    'browse.showResolution',
    'browse.showDuration',
    'browse.resolutionMark',
    'browse.imageHighResolutionPixels',
    'browse.videoHighResolutionPixels',
    'search.defaultPageSize',
    'search.historySize',
    'showHiddenItems',
    'player.seekSeconds',
    'player.loop',
    'player.externalPreset',
    'streaming.mode',
    'scan.ignoredDirectories',
    'scan.ignoredFiles',
    'scan.excludedFolders',
    'fileTypes.images',
    'fileTypes.videos',
    'fileTypes.audio',
    'fileTypes.text',
    'scan.computeHashes',
    'scan.readResolution',
    'thumbnails.enabled',
    'thumbnails.animatedHover',
    'thumbnails.excludedFolders',
    'trickplay.enabled',
    'trickplay.folders',
    'trickplay.excludedFolders',
    'trickplay.hoverPreview',
    'fileTracking.relink.enabled',
    'tags.recordHistory',
    'tasks.logRetentionDays',
    'logs.retentionDays',
    'logs.writeFiles',
    'editor.outputFolder',
    'files.portableNames',
    'files.workFolder',
    'files.deletePermanently',
    'files.trash.retentionDays',
    'keepAlive.mode',
    'keepAlive.intervalSeconds',
    'openBrowser',
    'fastSignIn',
    'security.accessTokens.enabled',
]);

/** The tabs, in the order of the administration page; each one's file says what it holds. */
export const TABS: SettingTab[] = [
    APPEARANCE_SETTINGS,
    BROWSING_SETTINGS,
    SEARCH_SETTINGS,
    PLAYER_SETTINGS,
    STREAMING_SETTINGS,
    SKIPPED_SETTINGS,
    FILE_TYPE_SETTINGS,
    INDEXING_SETTINGS,
    THUMBNAIL_SETTINGS,
    TRICKPLAY_SETTINGS,
    TRACKING_SETTINGS,
    TASK_SETTINGS,
    LOG_SETTINGS,
    STARTUP_SETTINGS,
    CONNECTION_SETTINGS,
    TAG_SETTINGS,
    EDITOR_SETTINGS,
    FILE_SETTINGS,
    KEEP_ALIVE_SETTINGS,
    TOKEN_SETTINGS,
];

export function getValue(values: unknown, key: string): unknown {
    return key
        .split('.')
        .reduce<unknown>((value, part) => (value as Record<string, unknown> | undefined)?.[part], values);
}

export function setValue(values: Record<string, unknown>, key: string, value: unknown): void {
    const parts = key.split('.');
    const last = parts.pop()!;
    const parent = parts.reduce<unknown>((target, part) => (target as Record<string, unknown>)[part], values);
    (parent as Record<string, unknown>)[last] = value;
}
