import type { SettingTab } from '../settings-schema';

/**
 * The Tasks, Logs, Start-up and Keep alive tabs of the settings: the server at work.
 */

export const TASK_SETTINGS: SettingTab = {
    id: 'tasks',
    title: $localize`Tasks`,
    intro: $localize`Scans, trickplay, similar-video comparisons and thumbnails run in the background. The other \
tabs show what runs, when each task runs next, the schedules that start them, and the log of every run.`,
    groups: [
        {
            title: $localize`History`,
            fields: [
                {
                    key: 'tasks.logRetentionDays',
                    label: $localize`Keep runs and logs for (days)`,
                    type: 'number',
                    min: 0,
                    max: 3650,
                    hint: $localize`Runs that finished longer ago are forgotten, with their logs. 0: until cleared \
by hand.`,
                },
            ],
        },
    ],
};

export const LOG_SETTINGS: SettingTab = {
    id: 'logs',
    title: $localize`Logs`,
    intro: $localize`What the server logs (its messages, warnings and errors, for every space) is kept for the Logs \
page. Both settings are the whole server's.`,
    groups: [
        {
            title: $localize`Keeping the log`,
            fields: [
                {
                    key: 'logs.retentionDays',
                    label: $localize`Keep log entries for (days)`,
                    type: 'number',
                    min: 0,
                    max: 3650,
                    hint: $localize`Older entries are forgotten, and so are the daily files. 0 keeps them forever \
(the database keeps growing).`,
                },
                {
                    key: 'logs.writeFiles',
                    label: $localize`Also save the log to files`,
                    type: 'checkbox',
                    hint: $localize`Writes a copy of every entry to a file per day (sfe-yyyymmdd.log) in the logs \
folder.`,
                    help: $localize`The logs folder is next to the database and the thumbnails (data/logs unless \
the server's configuration says otherwise). The files are written even while the database can't be.`,
                },
            ],
        },
    ],
};

export const STARTUP_SETTINGS: SettingTab = {
    id: 'startup',
    title: $localize`Start-up`,
    intro: $localize`What SFE does when it is started by hand from its portable package (a double click on Windows, \
the AppImage on Linux). A service never opens a browser. Both settings are the whole server's.`,
    groups: [
        {
            title: $localize`When SFE starts`,
            fields: [
                {
                    key: 'openBrowser',
                    label: $localize`Open the browser`,
                    type: 'checkbox',
                    hint: $localize`Opens SFE in this computer's default browser once it is ready.`,
                    help: $localize`Starting SFE with --no-browser keeps it closed whatever this says.`,
                },
                {
                    key: 'fastSignIn',
                    label: $localize`Sign the administrator in at once`,
                    type: 'checkbox',
                    onlyIn: 'local',
                    hint: $localize`The browser opened at start signs the server's administrator in without a \
password. Only for SFE used on this computer alone.`,
                    help: $localize`It uses a link that works once, for a minute, from this computer only, and only \
while the server has a single administrator. Anyone who can start SFE here gets in as the administrator.`,
                },
            ],
        },
    ],
};

export const KEEP_ALIVE_SETTINGS: SettingTab = {
    id: 'keepAlive',
    title: $localize`Keep alive`,
    intro: $localize`A file server drops the connection to a share nobody used for a while (Windows file servers \
after 15 minutes, many NAS sooner), and the drive may be lost with it. Keep alive looks at the library folder every \
few minutes, a single read of its first item, so the connection never goes idle. It can't reconnect a drive \
already lost.`,
    groups: [
        {
            title: $localize`Pings`,
            fields: [
                {
                    key: 'keepAlive.mode',
                    label: $localize`Keep the library folder alive`,
                    type: 'select',
                    options: [
                        ['off', $localize`:keep alive:Off`],
                        ['auto', $localize`When it is on a network share`],
                        ['always', $localize`:keep alive:Always`],
                    ],
                    hint: $localize`“When it is on a network share”: a \\\\server\\share folder, a mapped network \
drive, or a CIFS, NFS or SSHFS mount, also through a link.`,
                    help: $localize`“Always” also pings a folder that SFE can't tell is on the network.`,
                },
                {
                    key: 'keepAlive.intervalSeconds',
                    label: $localize`Seconds between pings`,
                    type: 'number',
                    min: 10,
                    max: 3600,
                    hint: $localize`Shorter than the time the file server waits before dropping an idle connection \
(240: every 4 minutes).`,
                },
                {
                    key: 'keepAlive.timeoutSeconds',
                    label: $localize`Time limit (seconds)`,
                    type: 'number',
                    min: 1,
                    max: 300,
                    hint: $localize`A ping taking longer counts as failed; the next one waits until it ends.`,
                },
                {
                    key: 'keepAlive.method',
                    label: $localize`How the library folder is pinged`,
                    type: 'select',
                    options: [
                        ['list', $localize`Read the folder`],
                        ['touch', $localize`Write and delete a small file`],
                    ],
                    hint: $localize`Reading is enough for most file servers; writing always reaches the server.`,
                    help: $localize`The computer may answer a read from its own cache without asking the server. \
Writing creates and deletes a small file in SFE's work folder (.sfe/keep-alive at the top of the library). When the \
work folder is on the server (File management), the folder is read instead. More folders are always read.`,
                },
                {
                    key: 'keepAlive.folders',
                    label: $localize`More folders`,
                    type: 'list',
                    placeholder: 'NAS/Photos',
                    hint: $localize`Folders kept alive too, for those reached through a link to another share: \
paths from the top of the library, one per line.`,
                    help: $localize`“When it is on a network share” looks at each one on its own.`,
                },
            ],
        },
    ],
};
