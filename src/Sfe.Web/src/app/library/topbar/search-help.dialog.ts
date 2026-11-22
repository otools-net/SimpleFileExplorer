import { ChangeDetectionStrategy, Component, computed, inject, output, viewChild } from '@angular/core';
import { ConfigService } from '../../core/config.service';
import { Rights } from '../../core/rights';
import { SheetComponent } from '../../shared/sheet.component';
import { registerDialog } from '../dialogs.service';

interface SyntaxRow {
    examples: string[];
    meaning: string;
}

interface SyntaxSection {
    title: string;
    rows: SyntaxRow[];
}

/** The query language in short, by theme; docs/reference/search-syntax.md has the fine print. */
export const SEARCH_SYNTAX: SyntaxSection[] = [
    {
        title: $localize`Words`,
        rows: [
            { examples: ['cat dog'], meaning: $localize`Finds names containing both words.` },
            { examples: ['cat, dog'], meaning: $localize`Finds names containing either word.` },
            { examples: ['cat -kitten'], meaning: $localize`Leaves out names containing a word.` },
            {
                examples: ['*holiday'],
                meaning: $localize`Matches the whole path, not just the name; a * at the start counts for the \
whole search.`,
            },
        ],
    },
    {
        title: $localize`File properties`,
        rows: [
            {
                examples: ['s:>1gb', 's:<=10mb', 's:700mb'],
                meaning: $localize`Size: larger than, smaller than or about.`,
            },
            { examples: ['e:mp4'], meaning: $localize`Extension; several find either one.` },
            {
                examples: ['type:image', 'type:video', 'type:audio', 'type:other'],
                meaning: $localize`Kind of file, known by its extension.`,
            },
        ],
    },
    {
        title: $localize`Photos, videos and audio`,
        rows: [
            {
                examples: ['w:>=1920', 'h:<720', 'r:1920x1080'],
                meaning: $localize`Resolution in pixels: the width, the height, or both sides.`,
            },
            { examples: ['r:hr', 'r:lr'], meaning: $localize`High or low resolution, as the HR and LR marks say.` },
            {
                examples: ['d:>10m', 'd:<30s', 'd:1:30'],
                meaning: $localize`Duration of videos and audio: longer than, shorter than or about.`,
            },
        ],
    },
    {
        title: $localize`Tags`,
        rows: [
            {
                examples: ['t:beach', 't:"two words"'],
                meaning: $localize`Files with this tag, or with a tag inheriting from it; typing t: suggests tags.`,
            },
            { examples: ['special:untagged'], meaning: $localize`Files without tags.` },
        ],
    },
    {
        title: $localize`Leaving out`,
        rows: [
            {
                examples: ['-t:work', '-e:jpg', '-s:>1gb', '-w:1920'],
                meaning: $localize`A - before any term finds the files it doesn’t.`,
            },
        ],
    },
];

/**
 * "Search syntax": the query language by theme. A dialog rather than a popover, since it outgrew one; each example is
 * a button adding it to the search box (`use`).
 */
@Component({
    selector: 'sfe-search-help',
    imports: [SheetComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './search-help.dialog.html',
})
export class SearchHelpDialog {
    /** An example picked: the term to add to the search. */
    readonly use = output<string>();

    private readonly sheet = viewChild.required<SheetComponent>('sheet');
    protected readonly sections = SEARCH_SYNTAX;
    private readonly config = inject(ConfigService);
    protected readonly managesFiles = computed(
        () =>
            this.config.can(Rights.createFolders) ||
            this.config.can(Rights.renameFiles) ||
            this.config.can(Rights.deleteFiles),
    );

    constructor() {
        registerDialog((dialogs) => dialogs.searchHelp, this);
    }

    open(): void {
        this.sheet().open();
        this.sheet().scrollToTop();
    }

    protected pick(example: string): void {
        this.sheet().close();
        this.use.emit(example);
    }
}
