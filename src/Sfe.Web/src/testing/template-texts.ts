import { readdirSync, readFileSync } from 'node:fs';
import {
    parseTemplate,
    TmplAstBoundText,
    TmplAstElement,
    TmplAstRecursiveVisitor,
    TmplAstTemplate,
    TmplAstText,
    TmplAstTextAttribute,
    tmplAstVisitAll,
} from '@angular/compiler';

/**
 * Finds the texts of the client's templates that aren't marked for translation (`i18n`, `i18n-title`…), for
 * `hard-coded-texts.spec.ts`. See agents/docs/development/translations.md.
 */

/** A text people read, not marked for translation. */
export interface HardCodedText {
    line: number;
    text: string;
}

/** Attributes people read (shown, or said by screen readers). */
const READ_ATTRIBUTES = new Set(['title', 'placeholder', 'alt', 'aria-label', 'aria-description', 'label']);

/** Elements whose content isn't text: skipped with their content, as are those marked `translate="no"`. */
const NOT_TEXT = new Set(['style', 'script', 'svg']);

/** Two letters in a row: "x", "×", "·", "1:05" or "{{ size }}" alone aren't words to translate. */
const WORD = /\p{L}{2}/u;

/** A template and the file line it starts on (0 for an `.html` file). */
export interface TemplateSource {
    template: string;
    firstLine: number;
}

/** The templates of `src/app`, by path from `src/` with `/`: the `.html` files and the inline `template:`s. */
export function appTemplates(root = 'src'): Map<string, TemplateSource> {
    const templates = new Map<string, TemplateSource>();
    for (const entry of readdirSync(`${root}/app`, { recursive: true })) {
        const path = `app/${entry.replaceAll('\\', '/')}`;
        if (path.endsWith('.html')) {
            templates.set(path, { template: readFileSync(`${root}/${path}`, 'utf8'), firstLine: 0 });
        } else if (path.endsWith('.ts') && !path.endsWith('.spec.ts')) {
            const code = readFileSync(`${root}/${path}`, 'utf8');
            const inline = /\btemplate:\s*`([\s\S]*?)`/.exec(code);
            if (inline) {
                const firstLine = code.slice(0, inline.index).split('\n').length - 1;
                templates.set(path, { template: inline[1], firstLine });
            }
        }
    }
    return templates;
}

/** The texts of one template people would read that aren't marked for translation. */
export function hardCodedTexts({ template, firstLine }: TemplateSource, url: string): HardCodedText[] {
    const parsed = parseTemplate(template, url, { preserveWhitespaces: false });
    if (parsed.errors?.length) throw new Error(`${url}: ${parsed.errors[0].msg}`);
    const finder = new HardCodedFinder(firstLine);
    tmplAstVisitAll(finder, parsed.nodes);
    return finder.found;
}

class HardCodedFinder extends TmplAstRecursiveVisitor {
    readonly found: HardCodedText[] = [];
    /** Elements marked `i18n` around the node visited: their texts are in the element's message. */
    private marked = 0;

    constructor(private readonly firstLine: number) {
        super();
    }

    override visitElement(element: TmplAstElement): void {
        if (NOT_TEXT.has(element.name) || skipped(element.attributes)) return;
        this.attributes(element.attributes);
        this.inside(element.i18n, () => super.visitElement(element));
    }

    override visitTemplate(template: TmplAstTemplate): void {
        if (skipped(template.attributes)) return;
        this.attributes(template.attributes);
        this.inside(template.i18n, () => super.visitTemplate(template));
    }

    override visitText(text: TmplAstText): void {
        if (!this.marked) this.add(text.value, text.sourceSpan.start.line);
    }

    override visitBoundText(text: TmplAstBoundText): void {
        const strings = (text.value as { ast?: { strings?: string[] } }).ast?.strings ?? [];
        if (!this.marked && !text.i18n) this.add(strings.join(' '), text.sourceSpan.start.line);
    }

    /**
     * Visits the content of an element, counting it as marked when the element is: only texts with an interpolation
     * carry `i18n` themselves, plain ones don't.
     */
    private inside(i18n: unknown, visit: () => void): void {
        if (i18n) this.marked++;
        visit();
        if (i18n) this.marked--;
    }

    private attributes(attributes: TmplAstTextAttribute[]): void {
        for (const attribute of attributes) {
            if (READ_ATTRIBUTES.has(attribute.name) && !attribute.i18n) {
                this.add(attribute.value, attribute.sourceSpan.start.line);
            }
        }
    }

    private add(text: string, line: number): void {
        const trimmed = text.replace(/\s+/g, ' ').trim();
        if (WORD.test(trimmed)) this.found.push({ line: this.firstLine + line + 1, text: trimmed });
    }
}

function skipped(attributes: TmplAstTextAttribute[]): boolean {
    return attributes.some((attribute) => attribute.name === 'translate' && attribute.value === 'no');
}
