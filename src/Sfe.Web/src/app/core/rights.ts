/*
 * The rights the client looks at (the server's Rights, Sfe.Core/Access/RightCatalog.cs): /api/config sends those of
 * the session in its library, and what needs one it doesn't have is hidden. The server checks them anyway.
 */

export const Rights = {
    browse: 'library.browse',
    viewFiles: 'files.view',
    download: 'files.download',
    externalPlayer: 'files.external',
    copyPaths: 'files.copyPaths',
    editImages: 'files.edit',
    viewTags: 'tags.view',
    assignTags: 'tags.assign',
    manageTags: 'tags.manage',
    scan: 'index.scan',
    removeFromIndex: 'index.remove',
    duplicates: 'tracking.duplicates',
    removeDuplicates: 'tracking.duplicatesRemove',
    similarVideos: 'tracking.similar',
    missingFiles: 'tracking.missing',
    manageTrickplay: 'trickplay.manage',
    viewTasks: 'tasks.view',
    runTasks: 'tasks.run',
    scheduleTasks: 'tasks.schedule',
    interfaceSettings: 'settings.interface',
    playbackSettings: 'settings.playback',
    scanningSettings: 'settings.scanning',
    tagSettings: 'settings.tags',
    trackingSettings: 'settings.tracking',
    thumbnailSettings: 'settings.thumbnails',
    trickplaySettings: 'settings.trickplay',
    taskSettings: 'settings.tasks',
    editorSettings: 'settings.editor',
    manageMembers: 'tenant.members',
    ownPreferences: 'account.preferences',
    ownPassword: 'account.password',
    apiDocs: 'system.apiDocs',
    serverSettings: 'server.settings',
    manageTenants: 'server.tenants',
    manageUsers: 'server.users',
    shutdown: 'server.shutdown',
    dismount: 'server.dismount',
} as const;

export type Right = typeof Rights[keyof typeof Rights];

/** The right changing the settings of each tab of settings-schema.ts in a library (the server's need `server.settings`). */
export const SETTINGS_RIGHTS: Record<string, Right> = {
    appearance: Rights.interfaceSettings,
    browsing: Rights.interfaceSettings,
    search: Rights.interfaceSettings,
    playback: Rights.playbackSettings,
    skipped: Rights.scanningSettings,
    fileTypes: Rights.scanningSettings,
    indexing: Rights.scanningSettings,
    thumbnails: Rights.thumbnailSettings,
    trickplay: Rights.trickplaySettings,
    tracking: Rights.trackingSettings,
    tasks: Rights.taskSettings,
    tags: Rights.tagSettings,
    editor: Rights.editorSettings,
};
