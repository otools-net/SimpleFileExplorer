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
        title: 'Words',
        rows: [
            { examples: ['cat dog'], meaning: 'Names containing both words' },
            { examples: ['cat, dog'], meaning: 'Either one' },
            { examples: ['cat -kitten'], meaning: 'Leave out names with a word' },
            { examples: ['*holiday'], meaning: 'Match the whole path, not just the name (a * at the start applies to the whole search)' },
        ],
    },
    {
        title: 'File properties',
        rows: [
            { examples: ['s:>1gb', 's:<=10mb', 's:700mb'], meaning: 'Size: larger, smaller or about' },
            { examples: ['e:mp4'], meaning: 'Extension; several are either one' },
            { examples: ['type:image', 'type:video', 'type:audio', 'type:other'], meaning: 'Kind of file, by its extension' },
        ],
    },
    {
        title: 'Photos, videos and audio',
        rows: [
            { examples: ['w:>=1920', 'h:<720', 'r:1920x1080'], meaning: 'Resolution in pixels: width, height, both sides' },
            { examples: ['r:hr', 'r:lr'], meaning: 'High or low resolution, as the HR/LR marks say' },
            { examples: ['d:>10m', 'd:<30s', 'd:1:30'], meaning: 'Duration of videos and audio: longer, shorter or about' },
        ],
    },
    {
        title: 'Tags',
        rows: [
            { examples: ['t:beach', 't:"two words"'], meaning: 'Tagged with it, or with a tag inheriting from it; typing t: suggests tags' },
            { examples: ['special:untagged'], meaning: 'Files without tags' },
        ],
    },
    {
        title: 'Leaving out',
        rows: [
            { examples: ['-t:work', '-e:jpg', '-s:>1gb', '-w:1920'], meaning: 'A - before any term finds the files it doesn’t' },
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
    template: `
        <sfe-sheet #sheet heading="Search syntax" subtitle="Click an example to add it to the search" variant="search-help" [wide]="true">
            @for (section of sections; track section.title) {
                <section class="search-help-section">
                    <h3>{{ section.title }}</h3>
                    <dl>
                        @for (row of section.rows; track row.meaning) {
                            <div class="search-help-row">
                                <dt>
                                    @for (example of row.examples; track example) {
                                        <button type="button" class="search-help-example" [title]="'Add ' + example + ' to the search'"
                                                (click)="pick(example)"><code>{{ example }}</code></button>
                                    }
                                </dt>
                                <dd>{{ row.meaning }}</dd>
                            </div>
                        }
                    </dl>
                </section>
            }
            <section class="search-help-section search-help-tips">
                <h3>Tips</h3>
                <ul>
                    <li>Results update as you type once the search has more than 3 characters; press Enter to search shorter ones.</li>
                    <li>Searches the current folder and below (the chip at the end of the box); click the chip to search the whole library.</li>
                    <li>Only indexed files are found: <strong>Scan this folder</strong> in the ⋮ menu refreshes the index.</li>
                    <li>Files with a hidden tag, like <strong>Archived</strong>, only show up when the search names that tag.</li>
                    <li>The speech-bubble button in the box explains a search in plain words.</li>
                    <li>Press <kbd>/</kbd> to go to the search box, <kbd>?</kbd> to open this help.</li>
                    <li>Once an item has the focus (<kbd>Tab</kbd>), the arrows move between items, <kbd>Space</kbd> selects, <kbd>Shift</kbd>+arrows select as they go.</li>
                    @if (managesFiles()) {
                        <li>In a folder, <kbd>Shift</kbd>+<kbd>N</kbd> makes a new folder and <kbd>F2</kbd> renames the item selected; right-click an item (Shift for the browser's menu) or use its ⋮ button for more.</li>
                    }
                </ul>
            </section>
            <span sheetFooter class="spacer"></span>
            <button sheetFooter type="button" class="button" title="Close (Esc)" (click)="sheet.close()">Close</button>
        </sfe-sheet>
    `,
})
export class SearchHelpDialog {
    /** An example picked: the term to add to the search. */
    readonly use = output<string>();

    private readonly sheet = viewChild.required<SheetComponent>('sheet');
    protected readonly sections = SEARCH_SYNTAX;
    private readonly config = inject(ConfigService);
    protected readonly managesFiles = computed(() => this.config.can(Rights.createFolders) || this.config.can(Rights.renameFiles));

    constructor() {
        registerDialog(dialogs => dialogs.searchHelp, this);
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
