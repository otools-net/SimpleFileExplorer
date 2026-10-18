import { LanguageDescription, LanguageSupport, StreamLanguage, StringStream } from '@codemirror/language';
import { languages } from '@codemirror/language-data';

/** Logs: levels, dates and times, quoted strings, links. */
const logMode = StreamLanguage.define<null>({
    name: 'log',
    token(stream: StringStream): string | null {
        if (stream.eatSpace())
            return null;
        if (stream.match(/^\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:[.,]\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?/) || stream.match(/^\d{1,2}:\d{2}:\d{2}(?:[.,]\d+)?/))
            return 'number';
        if (stream.match(/^\[?(?:FATAL|CRITICAL|CRIT|ERROR|ERR|FAIL(?:ED|URE)?|EXCEPTION|fail|error|crit|fatal)\]?(?=\W|$)/))
            return 'invalid';
        if (stream.match(/^\[?(?:WARNING|WARN|warn|warning)\]?(?=\W|$)/))
            return 'keyword';
        if (stream.match(/^\[?(?:INFO|NOTICE|info|notice|INF)\]?(?=\W|$)/))
            return 'atom';
        if (stream.match(/^\[?(?:DEBUG|TRACE|VERBOSE|DBG|TRC|debug|trace|dbug|trce)\]?(?=\W|$)/))
            return 'comment';
        if (stream.match(/^https?:\/\/\S+/))
            return 'link';
        if (stream.match(/^"(?:[^"\\]|\\.)*"?/) || stream.match(/^'(?:[^'\\]|\\.)*'?/))
            return 'string';
        if (stream.match(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i) || stream.match(/^0x[0-9a-f]+/i) || stream.match(/^\d+(?:\.\d+)?(?=\W|$)/))
            return 'number';
        stream.next();
        stream.eatWhile(/[^\s"'\d[]/);
        return null;
    },
});

/** Subtitles (SRT, WebVTT): cue numbers, times, markup. */
const subtitleMode = StreamLanguage.define<null>({
    name: 'subtitles',
    token(stream: StringStream): string | null {
        if (stream.sol() && stream.match(/^\d+\s*$/))
            return 'number';
        if (stream.sol() && stream.match(/^(?:WEBVTT|NOTE|STYLE|REGION)\b.*/))
            return 'keyword';
        if (stream.match(/^(?:\d{1,2}:)?\d{2}:\d{2}[.,]\d{3}/))
            return 'number';
        if (stream.match('-->'))
            return 'operator';
        if (stream.match(/^<\/?[a-z][^>]*>/i) || stream.match(/^\{\\[^}]*\}/))
            return 'tag';
        stream.next();
        stream.eatWhile(/[^\d<{-]/);
        return null;
    },
});

const EXTRA_LANGUAGES: readonly LanguageDescription[] = [
    LanguageDescription.of({ name: 'Log', extensions: ['log', 'err'], support: new LanguageSupport(logMode) }),
    LanguageDescription.of({ name: 'Subtitles', extensions: ['srt', 'vtt'], support: new LanguageSupport(subtitleMode) }),
];

/** Extensions read as another language of language-data (MSBuild files are XML, configuration files key = value). */
const ALIASES: Readonly<Record<string, string>> = {
    csproj: 'XML', vbproj: 'XML', fsproj: 'XML', props: 'XML', targets: 'XML', resx: 'XML', xaml: 'XML', slnx: 'XML', config: 'XML',
    plist: 'XML', xsd: 'XML', xsl: 'XML', xslt: 'XML', webmanifest: 'JSON', jsonc: 'JSON', json5: 'JSON', geojson: 'JSON',
    conf: 'Properties files', cfg: 'Properties files', env: 'Properties files', reg: 'Properties files', inf: 'Properties files',
    editorconfig: 'Properties files', gitattributes: 'Properties files',
};

/** The language of the file `name` (its highlighting), or null for plain text. Loaded on demand. */
export function languageFor(name: string): LanguageDescription | null {
    const lower = name.toLowerCase();
    const extension = /\.([^.]+)$/.exec(lower)?.[1] ?? '';
    const alias = ALIASES[extension];
    if (alias)
        return languages.find(language => language.name === alias) ?? null;
    return LanguageDescription.matchFilename(EXTRA_LANGUAGES, lower) ?? LanguageDescription.matchFilename(languages, name) ?? LanguageDescription.matchFilename(languages, lower);
}
