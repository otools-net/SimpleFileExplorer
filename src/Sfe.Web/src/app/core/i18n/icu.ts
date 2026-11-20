import { IntlMessageFormat } from 'intl-messageformat';

/**
 * Texts with counts or choices, written as ICU messages and marked for translation with `$localize`:
 * `icu($localize`{count, plural, one {# file} other {# files}} deleted.`, { count })`.
 * Plural rules follow the language of the text (`$localize.locale`, English until a translation is loaded); numbers
 * (`#`, `{size, number}`) follow the browser's region, as dates and sizes do (`core/format.ts`).
 * See agents/docs/development/translations.md.
 */
export function icu(message: string, values: Record<string, string | number>): string {
    return compiled(message).format(values) as string;
}

/** The language of the texts shown: the source (UK English) until a translation is loaded. */
export function textLocale(): string {
    return $localize.locale || 'en-GB';
}

const cache = new Map<string, IntlMessageFormat>();

/** Messages are few (they are in the code) and parsing them is the costly part: each is parsed once. */
function compiled(message: string): IntlMessageFormat {
    const key = `${textLocale()}\u0000${message}`;
    let format = cache.get(key);
    if (!format) {
        format = new IntlMessageFormat(message, textLocale(), undefined, { formatters });
        cache.set(key, format);
    }
    return format;
}

/** One formatter per options: numbers and dates in the browser's region, plural rules in the text's language. */
function memo<T>(make: (key: string) => T): (key: string) => T {
    const made = new Map<string, T>();
    return (key) => {
        let value = made.get(key);
        if (value === undefined) made.set(key, (value = make(key)));
        return value;
    };
}

const numberFormat = memo((options) => new Intl.NumberFormat(undefined, JSON.parse(options)));
const dateFormat = memo((options) => new Intl.DateTimeFormat(undefined, JSON.parse(options)));
const pluralRules = memo((key) => {
    const [locales, options] = JSON.parse(key);
    return new Intl.PluralRules(locales, options);
});

const formatters = {
    getNumberFormat: (_locales?: string | string[], options?: Intl.NumberFormatOptions) =>
        numberFormat(JSON.stringify(options ?? {})),
    getDateTimeFormat: (_locales?: string | string[], options?: Intl.DateTimeFormatOptions) =>
        dateFormat(JSON.stringify(options ?? {})),
    getPluralRules: (locales?: string | string[], options?: Intl.PluralRulesOptions) =>
        pluralRules(JSON.stringify([locales, options ?? {}])),
};
