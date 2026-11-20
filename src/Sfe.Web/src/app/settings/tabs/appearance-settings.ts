import type { SettingTab } from '../settings-schema';

/**
 * The Appearance tab of the settings (Interface › Appearance): the theme and how text files read.
 */

/** The fonts of the reading settings (formats/reading/reading.ts READER_FONTS), for both kinds of text. */
const READING_FONTS: [string, string][] = [
    ['literata', $localize`Literata (serif, for reading)`],
    ['atkinson', $localize`Atkinson Hyperlegible (sans-serif)`],
    ['jetbrains-mono', $localize`JetBrains Mono (monospace)`],
    ['system-serif', $localize`Serif of the device`],
    ['system-sans', $localize`Sans-serif of the device`],
    ['system-mono', $localize`Monospace of the device`],
    ['custom', $localize`The font named below`],
];

const LINE_HEIGHTS: [string, string][] = ['1.2', '1.35', '1.5', '1.65', '1.8', '2'].map((value) => [value, value]);

const WIDTHS: [string, string][] = [
    ['0', $localize`Whole width`],
    ...['60', '72', '80', '100', '120'].map((value): [string, string] => [
        value,
        $localize`:longest line of text:${value}:count: characters`,
    ]),
];

const MARGINS: [string, string][] = [
    ['0', $localize`:no margin:None`],
    ...['8', '16', '24', '32', '48', '64'].map((value): [string, string] => [
        value,
        $localize`:a margin in pixels:${value}:count: px`,
    ]),
];

const FONT_HINT = $localize`The bundled fonts are downloaded from this server the first time they are used.`;
const SIZE_HINT = $localize`In pixels, from 10 to 32.`;
const LINE_HEIGHT_HINT = $localize`Space between lines, as a multiple of the text size.`;
const WIDTH_HINT = $localize`The longest a line gets when long lines wrap. About 70 characters is easiest to read.`;
const MARGIN_HINT = $localize`Space left around the text, on every side.`;
const WRAP_HINT = $localize`Long lines continue on the next line instead of going off the side.`;
const LINE_NUMBERS_HINT = $localize`Shows the number of each line on the left.`;

export const APPEARANCE_SETTINGS: SettingTab = {
    id: 'appearance',
    title: $localize`Appearance`,
    intro: $localize`Changes apply as soon as you save them, for everyone using this space (on the server's \
settings: every space). A setting locked above says why it can't change here. The library's folder, the cache \
folder, the database, ffmpeg and the server's commands are set in the server's configuration file.`,
    groups: [
        {
            title: $localize`Theme`,
            action: 'browserTheme',
            fields: [
                {
                    key: 'appearance.theme',
                    label: $localize`Default theme`,
                    type: 'select',
                    options: [
                        ['system', $localize`Follow the device`],
                        ['dark', $localize`:theme:Dark`],
                        ['light', $localize`:theme:Light`],
                    ],
                    hint: $localize`The colours of the pages, in browsers that haven't picked their own below. \
“Follow the device” uses the light or dark mode of the computer or phone.`,
                },
            ],
        },
        {
            title: $localize`Reading text files`,
            hint: $localize`How text files read in the viewer, before each browser's own changes (“Aa” in the \
text's bar).`,
            fields: [
                {
                    key: 'reading.theme',
                    label: $localize`Colours`,
                    type: 'select',
                    options: [
                        ['app', $localize`As the app (light or dark)`],
                        ['light', $localize`:theme:Light`],
                        ['sepia', $localize`:theme:Sepia`],
                        ['dark', $localize`:theme:Dark`],
                    ],
                    hint: $localize`The colours of the text and of the page behind it.`,
                },
                {
                    key: 'reading.customFont',
                    label: $localize`Font of the device`,
                    type: 'text',
                    placeholder: $localize`e.g. Fira Code`,
                    hint: $localize`The name of a font installed on the reader's computer or phone, used where the \
font is “The font named below”.`,
                },
            ],
        },
        {
            title: $localize`Reading: notes and documents`,
            hint: $localize`Text meant to be read: .txt, .md, README, LICENSE…`,
            fields: [
                {
                    key: 'reading.prose.font',
                    label: $localize`Font`,
                    type: 'select',
                    options: READING_FONTS,
                    hint: FONT_HINT,
                },
                {
                    key: 'reading.prose.size',
                    label: $localize`Text size`,
                    type: 'number',
                    min: 10,
                    max: 32,
                    hint: SIZE_HINT,
                },
                {
                    key: 'reading.prose.lineHeight',
                    label: $localize`Line height`,
                    type: 'select',
                    numeric: true,
                    options: LINE_HEIGHTS,
                    hint: LINE_HEIGHT_HINT,
                },
                {
                    key: 'reading.prose.width',
                    label: $localize`Width`,
                    type: 'select',
                    numeric: true,
                    options: WIDTHS,
                    hint: WIDTH_HINT,
                },
                {
                    key: 'reading.prose.padding',
                    label: $localize`Margins`,
                    type: 'select',
                    numeric: true,
                    options: MARGINS,
                    hint: MARGIN_HINT,
                },
                {
                    key: 'reading.prose.wrap',
                    label: $localize`Wrap long lines`,
                    type: 'checkbox',
                    hint: WRAP_HINT,
                },
                {
                    key: 'reading.prose.lineNumbers',
                    label: $localize`Line numbers`,
                    type: 'checkbox',
                    hint: LINE_NUMBERS_HINT,
                },
            ],
        },
        {
            title: $localize`Reading: code, logs and settings`,
            hint: $localize`Scripts, code, logs, configuration and data files.`,
            fields: [
                {
                    key: 'reading.code.font',
                    label: $localize`Font`,
                    type: 'select',
                    options: READING_FONTS,
                    hint: FONT_HINT,
                },
                {
                    key: 'reading.code.size',
                    label: $localize`Text size`,
                    type: 'number',
                    min: 10,
                    max: 32,
                    hint: SIZE_HINT,
                },
                {
                    key: 'reading.code.lineHeight',
                    label: $localize`Line height`,
                    type: 'select',
                    numeric: true,
                    options: LINE_HEIGHTS,
                    hint: LINE_HEIGHT_HINT,
                },
                {
                    key: 'reading.code.width',
                    label: $localize`Width`,
                    type: 'select',
                    numeric: true,
                    options: WIDTHS,
                    hint: WIDTH_HINT,
                },
                {
                    key: 'reading.code.padding',
                    label: $localize`Margins`,
                    type: 'select',
                    numeric: true,
                    options: MARGINS,
                    hint: MARGIN_HINT,
                },
                {
                    key: 'reading.code.wrap',
                    label: $localize`Wrap long lines`,
                    type: 'checkbox',
                    hint: WRAP_HINT,
                },
                {
                    key: 'reading.code.lineNumbers',
                    label: $localize`Line numbers`,
                    type: 'checkbox',
                    hint: LINE_NUMBERS_HINT,
                },
            ],
        },
    ],
};
