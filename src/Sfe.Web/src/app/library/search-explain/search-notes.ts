import { phrase, Segment, slot, term } from './search-segments';

/* The notes on terms of a search that don't do what they may seem to (see search-query.ts). */

/** A term the parser leaves out, or reads as text, with examples of what works. */
export type IgnoredTerm = 'tag' | 'size' | 'type' | 'duration' | 'resolution' | 'extension';

export function ignoredNote(kind: IgnoredTerm, token: string, negated: boolean): Segment[] {
    switch (kind) {
        case 'tag':
            return phrase($localize`${slot('typed')}:typed: has no tag name: it is searched as text.`, {
                typed: term(token, 'text'),
            });
        case 'size':
            return phrase(
                $localize`${slot('typed')}:typed: is ignored: it isn't a valid size \
(try ${slot('a')}:a: or ${slot('b')}:b:).`,
                { typed: term(token, 'size', negated), a: term('s:>1gb', 'size'), b: term('s:<=10mb', 'size') },
            );
        case 'type':
            return phrase(
                $localize`${slot('typed')}:typed: is ignored: it isn't a type of file \
(try ${slot('a')}:a:, ${slot('b')}:b:, ${slot('c')}:c: or ${slot('d')}:d:).`,
                {
                    typed: term(token, 'extension', negated),
                    a: term('type:image', 'extension'),
                    b: term('type:video', 'extension'),
                    c: term('type:audio', 'extension'),
                    d: term('type:other', 'extension'),
                },
            );
        case 'duration':
            return phrase(
                $localize`${slot('typed')}:typed: is ignored: it isn't a valid duration \
(try ${slot('a')}:a: or ${slot('b')}:b:).`,
                { typed: term(token, 'size', negated), a: term('d:>10m', 'size'), b: term('d:<=1:30:00', 'size') },
            );
        case 'resolution':
            return phrase(
                $localize`${slot('typed')}:typed: is ignored: it isn't a valid resolution \
(try ${slot('a')}:a: or ${slot('b')}:b:).`,
                { typed: term(token, 'size', negated), a: term('w:>=1920', 'size'), b: term('r:1920x1080', 'size') },
            );
        case 'extension':
            return phrase($localize`${slot('typed')}:typed: is ignored: the extension is missing.`, {
                typed: term(token, 'extension', negated),
            });
    }
}

/** A word that looks like a file name with its extension: names are stored without it. */
export function extensionNote(word: string, extension: string): Segment[] {
    const better = [word.slice(0, -extension.length), `e:${extension.slice(1)}`].filter(Boolean).join(' ');
    return phrase(
        $localize`Names are searched without their extension, so ${slot('typed')}:typed: \
may find nothing: try ${slot('better')}:better:.`,
        { typed: term(word, 'text'), better: term(better, 'text') },
    );
}
