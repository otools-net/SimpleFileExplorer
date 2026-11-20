import type { SettingTab } from '../settings/settings-schema';

/**
 * The settings of connections (Connections:*), a tab of settings-schema.ts kept here: the whole server's, for its
 * administrators; Sfe:Locked (connections.*) in the configuration fixes them. Its cache of previews:
 * agents/docs/features/connection-previews.md.
 */
export const CONNECTION_SETTINGS: SettingTab = {
    id: 'connections',
    title: $localize`Connections`,
    intro: $localize`Connections to storage outside the libraries (Dropbox). The server itself calls the provider, \
so these are the whole server's settings; Sfe:Locked (connections.*) in the configuration fixes them.`,
    groups: [
        {
            title: $localize`What is allowed`,
            fields: [
                {
                    key: 'connections.enabled',
                    label: $localize`Allow connections`,
                    type: 'checkbox',
                    hint: $localize`Off: no connection is listed, opened or made, whatever the rights say. Those \
already made are kept.`,
                },
                {
                    key: 'connections.providers',
                    label: $localize`Kinds allowed`,
                    type: 'list',
                    placeholder: 'dropbox',
                    hint: $localize`One per line. Empty: every kind this server knows.`,
                },
                {
                    key: 'connections.levels',
                    label: $localize`Levels allowed`,
                    type: 'list',
                    placeholder: 'account\nlibrary\nserver',
                    hint: $localize`One per line: account (Only me), library (This space), server (The server).`,
                    help: $localize`“Only me” is private to the account that made it, “This space” is shared with a \
space and its groups, and “The server” is offered to spaces. Connections of a level taken out are kept but can't be \
used.`,
                },
            ],
        },
        {
            title: 'Dropbox',
            fields: [
                {
                    key: 'connections.dropbox.appKey',
                    label: $localize`App key`,
                    type: 'text',
                    hint: $localize`The key of the app you registered in Dropbox's App Console. Empty: Dropbox \
isn't offered.`,
                    help: $localize`The app's secret, optional, is set apart and never shown again.`,
                },
            ],
        },
        {
            title: $localize`Cache of previews`,
            fields: [
                {
                    key: 'connections.cache.maxSizeMegabytes',
                    label: $localize`Cache size (MB)`,
                    type: 'number',
                    min: 0,
                    max: 1048576,
                    hint: $localize`Thumbnails and files of connections kept on this server for previews, for \
every space. 0 turns it off.`,
                    help: $localize`It holds videos being converted, and pictures the provider doesn't make \
thumbnails of.`,
                },
                {
                    key: 'connections.cache.maxFileMegabytes',
                    label: $localize`Largest file downloaded (MB)`,
                    type: 'number',
                    min: 1,
                    max: 1048576,
                    hint: $localize`A larger file only plays when the browser can play it, and only gets the \
provider's thumbnail.`,
                },
                {
                    key: 'connections.cache.maxAgeDays',
                    label: $localize`Days kept unused`,
                    type: 'number',
                    min: 1,
                    max: 3650,
                    hint: $localize`What nobody looked at for this long is removed.`,
                },
            ],
        },
    ],
};
