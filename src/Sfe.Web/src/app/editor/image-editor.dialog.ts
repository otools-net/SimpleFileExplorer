import { ChangeDetectionStrategy, Component, computed, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Api } from '../core/api.service';
import { ConfigService } from '../core/config.service';
import { formatSize } from '../core/format';
import { LibraryItem } from '../core/models';
import { ToastService } from '../core/toast.service';
import { BusyDirective, whileBusy } from '../shared/busy.directive';
import { IconComponent } from '../shared/icon.component';
import { registerDialog } from '../library/dialogs.service';
import { ASPECTS, aspectRatio, clampRect, flipAspect, largestAround, dragRect, fitAspect, fullRect, Handle, HANDLES, rectFromPoints, sameRect } from './crop';
import {
    describeEdit, EDITOR_TOOLS, EditKindName, editedSize, EditOperation, encode, outputType, PICTURE_LOADER, Rect, renderEdits, Size,
} from './edits';

/** A drag of the crop frame: what moves, from where, and the frame then. */
interface CropDrag {
    pointerId: number;
    handle: Handle | null;
    startX: number;
    startY: number;
    start: Rect;
    /** Picture pixels per screen pixel. */
    scale: number;
    /** A new frame, drawn from this point of the picture. */
    origin: { x: number; y: number } | null;
}

type CropField = keyof Rect;

/**
 * The picture editor, over the viewer: the edits (EDIT_KINDS, one tool each) apply in turn to the original, each one
 * on the result of the ones before; Undo takes the last one back. "Save a copy" encodes the result in the browser and
 * sends it to the server, which saves it as a new file (Administration › Media › Picture editor): the original is
 * never changed. The crop tool shows the picture with a frame to drag (sides, corners, inside to move it, outside to
 * draw a new one), its size and position as numbers, and aspect ratios.
 */
@Component({
    selector: 'sfe-image-editor',
    imports: [BusyDirective, IconComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <dialog #dialog class="image-editor" aria-label="Edit picture" (cancel)="onCancel($event)" (close)="onClose()" (keydown)="onKeydown($event)">
            <header class="viewer-bar editor-bar">
                <div class="viewer-title">
                    <strong>{{ item()?.name }}</strong>
                    <span>{{ subtitle() }}</span>
                </div>
                <div class="editor-tools" role="toolbar" aria-label="Edits">
                    @for (tool of tools; track tool.id) {
                        <button type="button" class="icon-button" [class.active]="tool.id === activeTool()" [attr.aria-pressed]="tool.id === activeTool()"
                                [title]="tool.label + ' (' + tool.key + ')'" [attr.aria-label]="tool.label" [disabled]="!picture()" (click)="useTool(tool.id)">
                            <svg [sfeIcon]="tool.icon"></svg>
                        </button>
                    }
                    <button type="button" class="icon-button" title="Undo the last edit (Ctrl+Z)" aria-label="Undo" [disabled]="!operations().length || saving()" (click)="undo()">
                        <svg sfeIcon="undo"></svg>
                    </button>
                </div>
                <div class="viewer-actions">
                    <button type="button" class="button primary" title="Save the edited picture as a new file; the original stays as it is (Ctrl+S)"
                            [sfeBusy]="saving()" [disabled]="!dirty() || saving()" (click)="save()">Save a copy</button>
                    <button type="button" class="icon-button" title="Close without saving (Esc)" aria-label="Close" (click)="requestClose()"><svg sfeIcon="close"></svg></button>
                </div>
            </header>
            <div class="editor-body">
                <div class="editor-stage">
                    @if (error(); as error) {
                        <div class="viewer-message"><p>{{ error }}</p></div>
                    } @else if (!picture()) {
                        <div class="viewer-message"><p>Loading the picture…</p></div>
                    }
                    <div class="editor-picture" [hidden]="!picture() || error()">
                        <div #canvasHost class="editor-canvas"></div>
                        @if (activeTool() === 'crop' && picture()) {
                            <div class="crop-clip">
                                <div #cropLayer class="crop-layer" (pointerdown)="startDrag($event, null)" (pointermove)="moveDrag($event)"
                                     (pointerup)="endDrag($event)" (pointercancel)="endDrag($event)">
                                    <div class="crop-frame" [style.left.%]="percent(crop().x, size().width)" [style.top.%]="percent(crop().y, size().height)"
                                         [style.width.%]="percent(crop().width, size().width)" [style.height.%]="percent(crop().height, size().height)"
                                         (pointerdown)="startDrag($event, 'move')">
                                        @for (handle of handles; track handle) {
                                            <span class="crop-handle" [attr.data-handle]="handle" (pointerdown)="startDrag($event, handle)"></span>
                                        }
                                    </div>
                                </div>
                            </div>
                        }
                    </div>
                </div>
                <aside class="editor-panel">
                    @if (activeTool() === 'crop') {
                        <h3>Crop</h3>
                        <div class="aspect-head">
                            <span id="aspect-label">Aspect ratio</span>
                            <button type="button" class="button small" [disabled]="!picture() || !flipped()" (click)="flipAspect()"
                                    [title]="flipped() ? 'Use ' + flipped() + ' instead: the same ratio, ' + (isPortrait() ? 'landscape' : 'portrait') + ' (X)' : 'Pick a ratio to turn it between landscape and portrait (X)'">
                                {{ isPortrait() ? 'Landscape' : 'Portrait' }}
                            </button>
                        </div>
                        <div class="aspect-choices" role="radiogroup" aria-labelledby="aspect-label">
                            @for (choice of aspects; track choice[0]) {
                                <button type="button" role="radio" class="aspect-choice" [attr.aria-checked]="choice[0] === aspect()" [attr.data-aspect]="choice[0]"
                                        [disabled]="!picture()" [title]="aspectHint(choice)" (click)="setAspect(choice[0])">
                                    <span class="aspect-shape" [class.free]="choice[0] === 'free'" [class.portrait]="shapeOf(choice[0]) < 1" [style.aspect-ratio]="shapeOf(choice[0])"></span>
                                    <span>{{ choice[1] }}</span>
                                </button>
                            }
                        </div>
                        <div class="crop-fields">
                            @for (field of cropFields; track field[0]) {
                                <label class="field">
                                    <span>{{ field[1] }}</span>
                                    <input type="number" min="0" step="1" [value]="crop()[field[0]]" [disabled]="!picture()" [attr.data-field]="field[0]"
                                           (change)="setCropField(field[0], valueOf($event))">
                                </label>
                            }
                        </div>
                        <p class="field-hint">Drag the frame's sides and corners, drag inside it to move it, or drag outside it to draw a new one. Sizes are in pixels of the picture.</p>
                        <div class="editor-panel-actions">
                            <button type="button" class="button" title="Select the whole picture" [disabled]="!pendingCrop()" (click)="resetCrop()">Whole picture</button>
                            <button type="button" class="button primary" title="Crop to the frame, then go on editing (Enter)" [disabled]="!pendingCrop()" (click)="applyCrop()">Apply</button>
                        </div>
                    }
                    @if (operations().length) {
                        <h3>Edits</h3>
                        <ol class="editor-history">
                            @for (operation of operations(); track $index) {
                                <li>{{ describe(operation) }}</li>
                            }
                        </ol>
                    }
                    <p class="field-hint">{{ destination() }}</p>
                </aside>
            </div>
        </dialog>
    `,
})
export class ImageEditorDialog {
    private readonly api = inject(Api);
    private readonly config = inject(ConfigService);
    private readonly toasts = inject(ToastService);
    private readonly loadPicture = inject(PICTURE_LOADER);
    private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
    private readonly canvasHost = viewChild.required<ElementRef<HTMLElement>>('canvasHost');
    private readonly cropLayer = viewChild<ElementRef<HTMLElement>>('cropLayer');

    protected readonly tools = EDITOR_TOOLS;
    protected readonly handles = HANDLES;
    protected readonly aspects = ASPECTS;
    protected readonly cropFields: [CropField, string][] = [['x', 'Left'], ['y', 'Top'], ['width', 'Width'], ['height', 'Height']];

    protected readonly item = signal<LibraryItem | null>(null);
    /** The original, decoded; null while loading. */
    protected readonly picture = signal<HTMLImageElement | null>(null);
    protected readonly error = signal<string | null>(null);
    /** The edits applied, in order. */
    protected readonly operations = signal<EditOperation[]>([]);
    /** The size of the picture with the edits applied (what the tools work on). */
    protected readonly size = signal<Size>({ width: 0, height: 0 });
    protected readonly activeTool = signal<EditKindName | null>('crop');
    protected readonly crop = signal<Rect>({ x: 0, y: 0, width: 0, height: 0 });
    protected readonly aspect = signal('free');
    protected readonly saving = signal(false);

    /** The current ratio the other way round (16:9 → 9:16); null without one. */
    protected readonly flipped = computed(() => flipAspect(this.aspect()));
    protected readonly isPortrait = computed(() => {
        const ratio = aspectRatio(this.aspect(), this.size());
        return ratio !== null && ratio < 1;
    });

    /** The crop frame leaves part of the picture out: Save applies it first. */
    protected readonly pendingCrop = computed(() => this.activeTool() === 'crop' && this.size().width > 0 && !sameRect(this.crop(), fullRect(this.size())));
    protected readonly dirty = computed(() => this.operations().length > 0 || this.pendingCrop());
    protected readonly subtitle = computed(() => {
        const picture = this.picture();
        if (!picture)
            return '';
        const original = `${picture.naturalWidth} × ${picture.naturalHeight}`;
        const edited = this.pendingCrop() ? this.crop() : this.size();
        return this.dirty() ? `${original} → ${edited.width} × ${edited.height}` : original;
    });
    protected readonly destination = computed(() => {
        const folder = this.config.config()?.editor?.outputFolder;
        return folder
            ? `Saved as a new file in “${folder}”, in the same folders as the original.`
            : 'Saved as a new file next to the original, with “(edited)” in its name.';
    });

    /** The picture with the edits applied, shown on the stage. */
    private result: HTMLCanvasElement | null = null;
    private drag: CropDrag | null = null;
    /** Loads of pictures opened before the current one are dropped. */
    private generation = 0;

    constructor() {
        registerDialog(dialogs => dialogs.imageEditor, this);
    }

    /** Opens the editor on a picture of the listing (its original, from this server). */
    async open(item: LibraryItem): Promise<void> {
        const generation = ++this.generation;
        this.item.set(item);
        this.picture.set(null);
        this.error.set(null);
        this.operations.set([]);
        this.size.set({ width: 0, height: 0 });
        this.activeTool.set('crop');
        this.aspect.set('free');
        this.result = null;
        this.canvasHost().nativeElement.replaceChildren();
        const dialog = this.dialog().nativeElement;
        if (!dialog.open)
            dialog.showModal();
        try {
            const picture = await this.loadPicture(this.config.urls()!.original(item.path));
            if (generation !== this.generation)
                return;
            this.picture.set(picture);
            this.render();
        } catch (error) {
            if (generation === this.generation)
                this.error.set(error instanceof Error && error.name !== 'EncodingError' ? error.message : 'This browser cannot open this picture for editing.');
        }
    }

    close(): void {
        this.dialog().nativeElement.close();
    }

    /** Closes, after asking when there are unsaved edits. */
    protected requestClose(): void {
        if (this.confirmDiscard())
            this.close();
    }

    protected useTool(tool: EditKindName): void {
        this.activeTool.set(tool);
        this.crop.set(fitAspect(fullRect(this.size()), this.ratio(), this.size()));
    }

    /** Back to the picture before the last edit. */
    protected undo(): void {
        if (!this.operations().length)
            return;
        this.operations.update(list => list.slice(0, -1));
        this.render();
    }

    protected applyCrop(): void {
        if (!this.pendingCrop())
            return;
        this.operations.update(list => [...list, { kind: 'crop', rect: this.crop() }]);
        this.render();
    }

    protected resetCrop(): void {
        this.crop.set(fullRect(this.size()));
    }

    /** A ratio picked: the largest frame of that shape, around the centre of the current one (Free keeps the frame). */
    protected setAspect(id: string): void {
        this.aspect.set(id);
        this.crop.set(largestAround(this.crop(), aspectRatio(id, this.size()), this.size()));
    }

    /** The same ratio, between landscape and portrait (16:9 ↔ 9:16), the frame keeping its centre. */
    protected flipAspect(): void {
        const flipped = this.flipped();
        if (flipped)
            this.setAspect(flipped);
    }

    /** The shape drawn on a ratio's button (CSS aspect-ratio); the free one is square, dashed. */
    protected shapeOf(id: string): number {
        return aspectRatio(id, this.size().width ? this.size() : { width: 1, height: 1 }) ?? 1;
    }

    protected aspectHint([id, label]: [string, string]): string {
        return id === 'free' ? 'Free: any width and height'
            : id === 'original' ? `Original: the picture's own shape${this.size().width ? ` (${this.size().width} × ${this.size().height})` : ''}`
                : `${label === 'Square' ? 'Square (1:1)' : label}: the frame keeps this shape`;
    }

    /** A number typed for the frame: the others follow the aspect ratio and the picture's edges. */
    protected setCropField(field: CropField, text: string): void {
        const value = Number(text);
        const current = this.crop();
        if (text.trim() === '' || !Number.isFinite(value)) {
            this.crop.set({ ...current });
            return;
        }
        const ratio = this.ratio();
        const next = { ...current, [field]: value };
        if (ratio && field === 'width')
            next.height = value / ratio;
        else if (ratio && field === 'height')
            next.width = value * ratio;
        this.crop.set(ratio ? fitAspect(clampRect(next, this.size()), ratio, this.size()) : clampRect(next, this.size()));
    }

    protected async save(): Promise<void> {
        const item = this.item();
        if (!item || !this.result || this.saving())
            return;
        this.applyCrop();
        if (!this.operations().length)
            return;
        try {
            const result = this.result;
            const saved = await whileBusy(this.saving, async () => {
                const image = await encode(result, outputType(item.name), this.config.config()?.editor?.quality ?? 92);
                return this.api.saveEditedImage(item.path, image);
            });
            this.operations.set([]);
            this.close();
            this.toasts.show(`Saved as “${saved.path}” (${saved.width} × ${saved.height}, ${formatSize(saved.size)}).`, { timeout: 8000 });
        } catch (error) {
            this.toasts.error(`Could not save the picture: ${(error as Error).message}`);
        }
    }

    /* ---------- Crop frame drags (in picture pixels: the stage shows the picture smaller or larger) ---------- */

    protected startDrag(event: PointerEvent, handle: Handle | null): void {
        if (event.button !== 0 || this.drag)
            return;
        event.stopPropagation();
        event.preventDefault();
        const layer = this.cropLayer()?.nativeElement;
        const box = layer?.getBoundingClientRect();
        if (!layer || !box?.width)
            return;
        const scale = this.size().width / box.width;
        this.drag = {
            pointerId: event.pointerId,
            handle,
            startX: event.clientX,
            startY: event.clientY,
            start: this.crop(),
            scale,
            origin: handle ? null : { x: (event.clientX - box.left) * scale, y: (event.clientY - box.top) * scale },
        };
        layer.setPointerCapture?.(event.pointerId);
    }

    protected moveDrag(event: PointerEvent): void {
        const drag = this.drag;
        if (!drag || event.pointerId !== drag.pointerId)
            return;
        const dx = (event.clientX - drag.startX) * drag.scale;
        const dy = (event.clientY - drag.startY) * drag.scale;
        const ratio = this.ratio();
        this.crop.set(drag.origin
            ? rectFromPoints(drag.origin, { x: drag.origin.x + dx, y: drag.origin.y + dy }, this.size(), ratio)
            : dragRect(drag.start, drag.handle!, dx, dy, this.size(), ratio));
    }

    protected endDrag(event: PointerEvent): void {
        if (this.drag?.pointerId === event.pointerId)
            this.drag = null;
    }

    /* ---------- Keys, closing ---------- */

    protected onKeydown(event: KeyboardEvent): void {
        const target = event.target as Element;
        const typing = target.matches?.('input, select, textarea');
        const command = event.ctrlKey || event.metaKey;
        const key = event.key.toLowerCase();
        if (command && key === 's') {
            event.preventDefault();
            void this.save();
        } else if (command && key === 'z' && !typing) {
            event.preventDefault();
            this.undo();
        } else if (event.key === 'Enter' && !target.closest?.('button') && this.pendingCrop()) {
            // In a number field, Enter commits the number first (its change event), then applies.
            (target as HTMLElement).blur?.();
            event.preventDefault();
            this.applyCrop();
        } else if (key === 'x' && !command && !event.altKey && !typing && this.activeTool() === 'crop' && this.flipped()) {
            event.preventDefault();
            this.flipAspect();
        } else if (!command && !event.altKey && !typing) {
            const tool = this.tools.find(t => t.key.toLowerCase() === key);
            if (tool && this.picture()) {
                event.preventDefault();
                this.useTool(tool.id);
            }
        }
        // The listing page's own shortcuts are for the page under the editor.
        event.stopPropagation();
    }

    /** Esc with unsaved edits asks first. */
    protected onCancel(event: Event): void {
        if (!this.confirmDiscard())
            event.preventDefault();
    }

    private confirmDiscard(): boolean {
        return !this.dirty() || this.saving() || confirm('Close the editor and lose these edits?');
    }

    protected onClose(): void {
        ++this.generation;
        this.drag = null;
        this.picture.set(null);
        this.operations.set([]);
        this.result = null;
        this.canvasHost().nativeElement.replaceChildren();
    }

    protected percent(value: number, total: number): number {
        return total ? value / total * 100 : 0;
    }

    protected describe(operation: EditOperation): string {
        return describeEdit(operation);
    }

    protected valueOf(event: Event): string {
        return (event.target as HTMLInputElement | HTMLSelectElement).value;
    }

    private ratio(): number | null {
        return aspectRatio(this.aspect(), this.size());
    }

    /** Draws the original with the edits, puts it on the stage, and frames the whole result. */
    private render(): void {
        const picture = this.picture();
        if (!picture)
            return;
        const original = { width: picture.naturalWidth, height: picture.naturalHeight };
        try {
            this.result = renderEdits(picture, original, this.operations());
        } catch (error) {
            this.error.set((error as Error).message);
            return;
        }
        this.canvasHost().nativeElement.replaceChildren(this.result);
        this.size.set(editedSize(original, this.operations()));
        this.crop.set(fitAspect(fullRect(this.size()), this.ratio(), this.size()));
    }
}
