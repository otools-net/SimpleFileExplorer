import { tag } from '../../testing/fakes';
import { describeTag, TAG_TERM, tagQuery, termAt, termValue, textOn } from './tags';

describe('tags', () => {
    describe('tagQuery', () => {
        it('quotes the name, so spaces and commas stay in the term', () => {
            expect(tagQuery({ name: 'Science Fiction' })).toBe('t:"Science Fiction"');
            expect(tagQuery({ name: 'Rock, Paper' })).toBe('t:"Rock, Paper"');
            expect(tagQuery({ name: 'Ünïcødé 日本' })).toBe('t:"Ünïcødé 日本"');
        });

        it('drops double quotes, which the query language cannot hold (the server ignores them in names too)', () => {
            expect(tagQuery({ name: '12" Vinyl' })).toBe('t:"12 Vinyl"');
            expect(tagQuery({ name: '"""' })).toBe('t:""');
        });
    });

    describe('textOn', () => {
        it('picks dark text on light colors and white text on dark ones', () => {
            expect(textOn('#ffffff')).toBe('#16181d');
            expect(textOn('#ffd63d')).toBe('#16181d');
            expect(textOn('#000000')).toBe('#ffffff');
            expect(textOn('#3b87f0')).toBe('#ffffff');
            expect(textOn('#e22c3c')).toBe('#ffffff');
        });

        it('reads upper and lower case hex alike', () => {
            expect(textOn('#FFD63D')).toBe(textOn('#ffd63d'));
        });

        it('weighs green most, as the eye does', () => {
            expect(textOn('#00ff00')).toBe('#16181d');
            expect(textOn('#0000ff')).toBe('#ffffff');
        });
    });

    describe('describeTag', () => {
        it('is empty for a plain tag', () => {
            expect(describeTag(tag('Plain'))).toBe('');
        });

        it('lists the shorthand, the aliases and the parents in that order', () => {
            const parent = tag('Arcane');
            const details = tag('Jinx Piltover', {
                shorthand: 'Jinx',
                aliases: ['Jinxy', 'Powder'],
                parents: [parent, tag('League of Legends', { displayName: 'League of Legends (Game)' })],
            });

            expect(describeTag(details)).toBe('Jinx · Jinxy, Powder · in Arcane, League of Legends (Game)');
        });

        it('leaves out the parts a tag does not have', () => {
            expect(describeTag(tag('A', { aliases: ['B'] }))).toBe('B');
            expect(describeTag(tag('A', { parents: [tag('P')] }))).toBe('in P');
        });
    });

    describe('TAG_TERM', () => {
        it('recognises t: and tag:, negated or not, in any case', () => {
            expect('t:cat'.match(TAG_TERM)?.slice(1)).toEqual(['', 't:', 'cat']);
            expect('-TAG:"big cat"'.match(TAG_TERM)?.slice(1)).toEqual(['-', 'TAG:', '"big cat"']);
            expect('t:'.match(TAG_TERM)?.[3]).toBe('');
        });

        it('leaves other terms alone', () => {
            expect(TAG_TERM.test('tags:cat')).toBe(false);
            expect(TAG_TERM.test('cat')).toBe(false);
            expect(TAG_TERM.test('e:mp4')).toBe(false);
            expect(TAG_TERM.test('--t:cat')).toBe(false);
        });
    });

    describe('termAt', () => {
        const at = (text: string) => {
            const caret = text.indexOf('|');
            const plain = text.replace('|', '');
            const { start, end } = termAt(plain, caret);
            return plain.slice(start, end);
        };

        it('finds the term around the caret, split by spaces and commas', () => {
            expect(at('cat t:sh|rek dog')).toBe('t:shrek');
            expect(at('cat,t:sh|rek,dog')).toBe('t:shrek');
            expect(at('|cat dog')).toBe('cat');
            expect(at('cat dog|')).toBe('dog');
            expect(at('cat |')).toBe('');
            expect(at('|')).toBe('');
        });

        it('keeps separators inside double quotes', () => {
            expect(at('x t:"Science Fi|ction" y')).toBe('t:"Science Fiction"');
            expect(at('x t:"Rock, Pa|per" y')).toBe('t:"Rock, Paper"');
            expect(at('t:"a b" t:"c |d"')).toBe('t:"c d"');
        });

        it('runs to the end of the text when a quote is left open', () => {
            expect(at('x t:"Science Fi|ction y, z')).toBe('t:"Science Fiction y, z');
        });
    });

    describe('termValue', () => {
        it('leaves a plain name as it is', () => {
            expect(termValue('Shrek')).toBe('Shrek');
            expect(termValue('snake_case')).toBe('snake_case');
        });

        it('quotes names holding a separator, without their own quotes', () => {
            expect(termValue('Science Fiction')).toBe('"Science Fiction"');
            expect(termValue('Rock,Paper')).toBe('"Rock,Paper"');
            expect(termValue('tab\there')).toBe('"tab\there"');
            expect(termValue('12" Vinyl')).toBe('"12 Vinyl"');
            expect(termValue('a"b')).toBe('"ab"');
        });
    });
});
