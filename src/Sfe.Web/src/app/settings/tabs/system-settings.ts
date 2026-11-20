import type { SettingTab } from '../settings-schema';

/**
 * The Tasks, Logs, Start-up and Keep alive tabs of the settings: the server at work.
 */

export const TASK_SETTINGS: SettingTab = {
    id: 'tasks',
    title: 'Tasks',
    intro:
        'Scans, trickplay, similar-video comparisons and thumbnails run in the background. ' +
        'The other tabs show what runs, when each task runs next, the schedules that start them, ' +
        'and the log of every run.',
    groups: [
        {
            title: 'History',
            fields: [
                {
                    key: 'tasks.logRetentionDays',
                    label: 'Keep runs and logs for (days)',
                    type: 'number',
                    min: 0,
                    max: 3650,
                    hint: 'Runs that finished longer ago are forgotten, with their logs. 0: until cleared by hand.',
                },
            ],
        },
    ],
};

export const LOG_SETTINGS: SettingTab = {
    id: 'logs',
    title: 'Logs',
    intro:
        'What the server logs (its messages, warnings and errors, for every space) is kept for the Logs page. ' +
        "Both settings are the whole server's.",
    groups: [
        {
            title: 'Keeping the log',
            fields: [
                {
                    key: 'logs.retentionDays',
                    label: 'Keep log entries for (days)',
                    type: 'number',
                    min: 0,
                    max: 3650,
                    hint:
                        'Older entries are forgotten, and so are the daily files. 0: ' +
                        'kept forever (the database keeps growing).',
                },
                {
                    key: 'logs.writeFiles',
                    label: 'Also save the log to files',
                    type: 'checkbox',
                    hint:
                        'Writes a copy of every entry to a file per day (sfe-yyyymmdd.log) in the logs folder, ' +
                        "next to the database and the thumbnails (data/logs unless the server's configuration " +
                        "says otherwise). The files are written even while the database can't be.",
                },
            ],
        },
    ],
};

export const STARTUP_SETTINGS: SettingTab = {
    id: 'startup',
    title: 'Start-up',
    intro:
        'What SFE does when it is started by hand from its portable package (a double click on Windows, ' +
        "the AppImage on Linux). A service never opens a browser. Both settings are the whole server's.",
    groups: [
        {
            title: 'When SFE starts',
            fields: [
                {
                    key: 'openBrowser',
                    label: 'Open the browser',
                    type: 'checkbox',
                    hint:
                        "Opens SFE in this computer's default browser once it is ready. " +
                        'Starting SFE with --no-browser keeps it closed whatever this says.',
                },
                {
                    key: 'fastSignIn',
                    label: 'Sign the administrator in at once',
                    type: 'checkbox',
                    hint:
                        "The browser opened at start signs the server's administrator in without a password, " +
                        'with a link that works once, for a minute, from this computer only. ' +
                        'Only while the server has a single administrator. For SFE used on this computer alone: ' +
                        'anyone who can start SFE here gets in as the administrator.',
                },
            ],
        },
    ],
};

export const KEEP_ALIVE_SETTINGS: SettingTab = {
    id: 'keepAlive',
    title: 'Keep alive',
    intro:
        'A file server drops the connection to a share nobody used for a while (Windows file servers after 15 ' +
        'minutes, many NAS sooner), and the drive may be lost with it. Keep alive looks at the library folder ' +
        'every few minutes, a single read of its first entry, so the connection never goes idle. ' +
        "It can't reconnect a drive already lost.",
    groups: [
        {
            title: 'Pings',
            fields: [
                {
                    key: 'keepAlive.mode',
                    label: 'Keep the library folder alive',
                    type: 'select',
                    options: [
                        ['off', 'Off'],
                        ['auto', 'When it is on a network share'],
                        ['always', 'Always'],
                    ],
                    hint:
                        '“When it is on a network share”: a \\\\server\\share folder, a mapped network drive, ' +
                        'or a CIFS, NFS or SSHFS mount, also through a link. “Always”: ' +
                        "also a folder SFE can't tell is on the network.",
                },
                {
                    key: 'keepAlive.intervalSeconds',
                    label: 'Seconds between pings',
                    type: 'number',
                    min: 10,
                    max: 3600,
                    hint:
                        'Shorter than the time the file server waits before dropping an idle connection (240: ' +
                        'every 4 minutes).',
                },
                {
                    key: 'keepAlive.timeoutSeconds',
                    label: 'Time limit (seconds)',
                    type: 'number',
                    min: 1,
                    max: 300,
                    hint: 'A ping taking longer counts as failed; the next one waits until it ends.',
                },
                {
                    key: 'keepAlive.method',
                    label: 'How the library folder is pinged',
                    type: 'select',
                    options: [
                        ['list', 'Read the folder'],
                        ['touch', 'Write and delete a small file'],
                    ],
                    hint:
                        'Reading is enough for most file servers, but the computer may answer it from its own ' +
                        'cache without asking the server. Writing always reaches the server: ' +
                        "a small file is created and deleted in SFE's work folder (.sfe/keep-alive at the " +
                        "library's root). When the work folder is on the server (File management), " +
                        'the folder is read instead. More folders are always read.',
                },
                {
                    key: 'keepAlive.folders',
                    label: 'More folders',
                    type: 'list',
                    placeholder: 'NAS/Photos',
                    hint:
                        'Library folders kept alive too, for folders reached through a link to another share: ' +
                        'paths from the library root, one per line. “When it is on a network share” looks at ' +
                        'each one on its own.',
                },
            ],
        },
    ],
};
