import type { SettingTab } from '../settings-schema';

/**
 * The Appearance tab of the settings (Interface › Appearance): the theme and how text files read.
 */

/** The fonts of the reading settings (formats/reading/reading.ts READER_FONTS), for both kinds of text. */
const READING_FONTS: [string, string][] = [
    ['literata', 'Literata (serif, for reading)'],
    ['atkinson', 'Atkinson Hyperlegible (sans-serif)'],
    ['jetbrains-mono', 'JetBrains Mono (monospace)'],
    ['system-serif', 'Serif of the device'],
    ['system-sans', 'Sans-serif of the device'],
    ['system-mono', 'Monospace of the device'],
    ['custom', 'The font named below'],
];

const LINE_HEIGHTS: [string, string][] = ['1.2', '1.35', '1.5', '1.65', '1.8', '2'].map((value) => [value, value]);

const WIDTHS: [string, string][] = [
    ['0', 'Whole width'],
    ['60', '60 characters'],
    ['72', '72 characters'],
    ['80', '80 characters'],
    ['100', '100 characters'],
    ['120', '120 characters'],
];

const MARGINS: [string, string][] = [
    ['0', 'None'],
    ...['8', '16', '24', '32', '48', '64'].map((value): [string, string] => [value, `${value} px`]),
];

export const APPEARANCE_SETTINGS: SettingTab = {
    id: 'appearance',
    title: 'Appearance',
    intro:
        "Changes apply as soon as you save them, for everyone using the library (the server's settings: " +
        'every library); a setting locked above says why it cannot change here. The library location, ' +
        "the cache folder, the database, ffmpeg and the server commands are set in the server's configuration " +
        'file.',
    groups: [
        {
            title: 'Theme',
            action: 'browserTheme',
            fields: [
                {
                    key: 'appearance.theme',
                    label: 'Default theme',
                    type: 'select',
                    options: [
                        ['system', 'Follow the device'],
                        ['dark', 'Dark'],
                        ['light', 'Light'],
                    ],
                    hint:
                        'Colors of the pages, for the browsers that have not picked their own below. ' +
                        '“Follow the device” uses the light or dark mode of the computer or phone.',
                },
            ],
        },
        {
            title: 'Reading text files',
            hint: "How text files read in the viewer, before each browser's own changes (“Aa” in the text's bar).",
            fields: [
                {
                    key: 'reading.theme',
                    label: 'Colours',
                    type: 'select',
                    options: [
                        ['app', 'As the app (light or dark)'],
                        ['light', 'Light'],
                        ['sepia', 'Sepia'],
                        ['dark', 'Dark'],
                    ],
                    hint: 'The colours of the text and its page.',
                },
                {
                    key: 'reading.customFont',
                    label: 'Font of the device',
                    type: 'text',
                    placeholder: 'e.g. Fira Code',
                    hint:
                        "The name of a font installed on the reader's computer or phone, " +
                        'used where the font is “The font named below”.',
                },
            ],
        },
        {
            title: 'Reading: notes and documents',
            hint: 'Text to read: .txt, .md, README, LICENSE…',
            fields: [
                {
                    key: 'reading.prose.font',
                    label: 'Font',
                    type: 'select',
                    options: READING_FONTS,
                    hint: 'The bundled fonts are downloaded from this server the first time they are used.',
                },
                {
                    key: 'reading.prose.size',
                    label: 'Text size',
                    type: 'number',
                    min: 10,
                    max: 32,
                    hint: 'In pixels, from 10 to 32.',
                },
                {
                    key: 'reading.prose.lineHeight',
                    label: 'Line height',
                    type: 'select',
                    numeric: true,
                    options: LINE_HEIGHTS,
                    hint: 'The room between lines, as a multiple of the text size.',
                },
                {
                    key: 'reading.prose.width',
                    label: 'Width',
                    type: 'select',
                    numeric: true,
                    options: WIDTHS,
                    hint: 'The longest line when long lines wrap; about 70 characters are easiest to read.',
                },
                {
                    key: 'reading.prose.padding',
                    label: 'Margins',
                    type: 'select',
                    numeric: true,
                    options: MARGINS,
                    hint: 'The room around the text.',
                },
                {
                    key: 'reading.prose.wrap',
                    label: 'Wrap long lines',
                    type: 'checkbox',
                    hint: 'Long lines continue on the next line instead of going off the side.',
                },
                {
                    key: 'reading.prose.lineNumbers',
                    label: 'Line numbers',
                    type: 'checkbox',
                    hint: 'Numbers the lines at the left.',
                },
            ],
        },
        {
            title: 'Reading: code, logs and settings',
            hint: 'Scripts, code, logs, configuration and data files.',
            fields: [
                {
                    key: 'reading.code.font',
                    label: 'Font',
                    type: 'select',
                    options: READING_FONTS,
                    hint: 'The bundled fonts are downloaded from this server the first time they are used.',
                },
                {
                    key: 'reading.code.size',
                    label: 'Text size',
                    type: 'number',
                    min: 10,
                    max: 32,
                    hint: 'In pixels, from 10 to 32.',
                },
                {
                    key: 'reading.code.lineHeight',
                    label: 'Line height',
                    type: 'select',
                    numeric: true,
                    options: LINE_HEIGHTS,
                    hint: 'The room between lines, as a multiple of the text size.',
                },
                {
                    key: 'reading.code.width',
                    label: 'Width',
                    type: 'select',
                    numeric: true,
                    options: WIDTHS,
                    hint: 'The longest line when long lines wrap; about 70 characters are easiest to read.',
                },
                {
                    key: 'reading.code.padding',
                    label: 'Margins',
                    type: 'select',
                    numeric: true,
                    options: MARGINS,
                    hint: 'The room around the text.',
                },
                {
                    key: 'reading.code.wrap',
                    label: 'Wrap long lines',
                    type: 'checkbox',
                    hint: 'Long lines continue on the next line instead of going off the side.',
                },
                {
                    key: 'reading.code.lineNumbers',
                    label: 'Line numbers',
                    type: 'checkbox',
                    hint: 'Numbers the lines at the left.',
                },
            ],
        },
    ],
};
