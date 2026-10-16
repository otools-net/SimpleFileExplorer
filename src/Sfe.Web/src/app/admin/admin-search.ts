/*
 * The search box of the administration page: finds the sections, sub-pages and settings (settings-schema.ts) of the
 * sections a session sees, by every word typed (any case, accents ignored) in their titles, descriptions, choices and
 * configuration keys. Those whose title holds every word come first.
 */

import { AdminSection, AdminSubpage } from './admin-sections';
import { SettingField, SettingGroup, TABS } from '../settings/settings-schema';

export interface AdminSearchResult {
    section: AdminSection;
    subpage: AdminSubpage;
    /** The setting found (its key in settings-schema.ts); none for a section or a sub-page. */
    setting?: string;
    title: string;
    /** Where it is: "Section › Sub-page › Group". */
    where: string;
    text?: string;
}

/** The most results listed. */
export const ADMIN_SEARCH_LIMIT = 40;

/** Lower case without accents, so that "theme" finds "Thème" and the other way round. */
export const normalize = (text: string) => text.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();

/** The words of a query, normalized. */
export const searchWords = (query: string) => normalize(query).split(/\s+/).filter(Boolean);

/** Everything of a setting a search looks in, besides its title. */
function fieldText(field: SettingField, group: SettingGroup): string[] {
    const choices = field.type === 'select' ? field.options.map(option => option[1])
        : field.type === 'preset' ? field.presets.map(preset => preset[0]) : [];
    return [field.hint, field.key, group.title, group.hint ?? '', ...choices];
}

/** Matches of `query` among `sections` (those the session sees), best first; the page lists `ADMIN_SEARCH_LIMIT` of them. */
export function searchAdmin(query: string, sections: AdminSection[]): AdminSearchResult[] {
    const words = searchWords(query);
    if (!words.length)
        return [];
    const titled: AdminSearchResult[] = [];
    const others: AdminSearchResult[] = [];
    const add = (result: AdminSearchResult, ...more: string[]) => {
        const title = normalize(result.title);
        const all = normalize([result.title, result.where, ...more].join('\n'));
        if (words.every(word => title.includes(word)))
            titled.push(result);
        else if (words.every(word => all.includes(word)))
            others.push(result);
    };
    for (const section of sections) {
        const single = section.subpages.length === 1;
        add({ section, subpage: section.subpages[0], title: section.title, where: section.group, text: section.description }, section.description);
        for (const subpage of section.subpages) {
            const where = single ? section.title : `${section.title} › ${subpage.title}`;
            if (!single)
                add({ section, subpage, title: subpage.title, where: section.title, text: subpage.description ?? section.description });
            const tab = subpage.settings ? TABS.find(t => t.id === subpage.settings) : undefined;
            for (const group of tab?.groups ?? [])
                for (const field of group.fields)
                    add({ section, subpage, setting: field.key, title: field.label, where: `${where} › ${group.title}`, text: field.hint },
                        ...fieldText(field, group));
        }
    }
    return [...titled, ...others];
}

/** `text` cut into the parts matching one of `words` and the others, to mark the first ones. */
export function markWords(text: string, words: string[]): { text: string; match: boolean }[] {
    // Normalizing keeps the length of the usual letters (é becomes e plus a mark, removed): compare letter by letter.
    const plain = [...text].map(letter => normalize(letter));
    const matched = new Array<boolean>(plain.length).fill(false);
    const joined = plain.join('');
    // Positions in `joined` back to letters of `text`.
    const letterAt: number[] = [];
    plain.forEach((part, index) => { for (let i = 0; i < part.length; i++) letterAt.push(index); });
    for (const word of words)
        for (let at = joined.indexOf(word); at >= 0; at = joined.indexOf(word, at + word.length))
            for (let i = at; i < at + word.length; i++)
                matched[letterAt[i]] = true;
    const parts: { text: string; match: boolean }[] = [];
    [...text].forEach((letter, index) => {
        const last = parts[parts.length - 1];
        if (last && last.match === matched[index])
            last.text += letter;
        else
            parts.push({ text: letter, match: matched[index] });
    });
    return parts;
}
