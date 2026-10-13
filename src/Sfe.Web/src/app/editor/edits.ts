import { InjectionToken } from '@angular/core';

/*
 * The picture editor's edits. Each edit is an operation (a kind and its parameters) that the browser applies in turn
 * on a canvas (renderEdits); the server only stores the encoded result, as a new file. A new kind of edit (rotate,
 * flip, resize...) adds its operation to `EditOperation` and its handler to EDIT_KINDS, and a tool to EDITOR_TOOLS for
 * the editor's bar: the history, the undo, the preview and the save follow.
 */

export interface Size {
    width: number;
    height: number;
}

/** A part of a picture, in its pixels. */
export interface Rect extends Size {
    x: number;
    y: number;
}

/** Keeps `rect` of the picture. */
export interface CropOperation {
    kind: 'crop';
    rect: Rect;
}

export type EditOperation = CropOperation;

export type EditKindName = EditOperation['kind'];

/** How a kind of edit changes a picture. */
export interface EditKind<T extends EditOperation> {
    /** The size of the picture after the edit. */
    size(operation: T, input: Size): Size;
    /** Draws `input`, edited, on `context`, whose canvas already has the size `size` gives. */
    draw(context: CanvasRenderingContext2D, input: CanvasImageSource, operation: T, inputSize: Size): void;
    /** The edit in a few words, for the editor's history ("Crop to 800 × 600"). */
    describe(operation: T): string;
}

export const EDIT_KINDS: { [K in EditKindName]: EditKind<Extract<EditOperation, { kind: K }>> } = {
    crop: {
        size: operation => ({ width: operation.rect.width, height: operation.rect.height }),
        draw: (context, input, { rect }) => context.drawImage(input, rect.x, rect.y, rect.width, rect.height, 0, 0, rect.width, rect.height),
        describe: ({ rect }) => `Crop to ${rect.width} × ${rect.height}`,
    },
};

/** A tool of the editor's bar: the edit it makes, its button and its shortcut. */
export interface EditorTool {
    id: EditKindName;
    label: string;
    icon: string;
    key: string;
}

export const EDITOR_TOOLS: readonly EditorTool[] = [
    { id: 'crop', label: 'Crop', icon: 'crop', key: 'C' },
];

function kindOf<T extends EditOperation>(operation: T): EditKind<T> {
    return EDIT_KINDS[operation.kind] as EditKind<T>;
}

export function describeEdit(operation: EditOperation): string {
    return kindOf(operation).describe(operation);
}

/** The size of a picture of `input` size once `operations` are applied. */
export function editedSize(input: Size, operations: readonly EditOperation[]): Size {
    return operations.reduce((size, operation) => kindOf(operation).size(operation, size), { width: input.width, height: input.height });
}

/** The browser cannot draw a canvas that large (each has its own limit), or has no memory left for it. */
export class CanvasTooLargeError extends Error {
    constructor(size: Size) {
        super(`This browser cannot edit a picture of ${size.width} × ${size.height} pixels.`);
    }
}

/**
 * `source` (of `size`) with `operations` applied in turn, on a canvas of its own: a copy of `source` without
 * operations. Each step draws on a new canvas, from the original's pixels for the first one.
 */
export function renderEdits(source: CanvasImageSource, size: Size, operations: readonly EditOperation[],
    createCanvas: () => HTMLCanvasElement = () => document.createElement('canvas')): HTMLCanvasElement {
    let current: CanvasImageSource = source;
    let currentSize: Size = size;
    let canvas: HTMLCanvasElement | null = null;
    for (const operation of operations.length ? operations : [null]) {
        const next = operation ? kindOf(operation).size(operation, currentSize) : currentSize;
        canvas = createCanvas();
        canvas.width = next.width;
        canvas.height = next.height;
        const context = canvas.getContext('2d');
        if (!context)
            throw new CanvasTooLargeError(next);
        if (operation)
            kindOf(operation).draw(context, current, operation, currentSize);
        else
            context.drawImage(current, 0, 0);
        current = canvas;
        currentSize = next;
    }
    return canvas!;
}

/** What a canvas can encode, and so what the editor saves. */
export type EditedType = 'image/jpeg' | 'image/png' | 'image/webp';

/** The format of the original when a canvas can write it; PNG (lossless) for the others (GIF, BMP, TIFF, AVIF...). */
export function outputType(name: string): EditedType {
    const extension = name.slice(name.lastIndexOf('.') + 1).toLowerCase();
    return extension === 'jpg' || extension === 'jpeg' || extension === 'jpe' ? 'image/jpeg'
        : extension === 'webp' ? 'image/webp'
            : 'image/png';
}

/**
 * The canvas encoded as `type`, `quality` (1-100) applying to JPEG and WebP. A browser that can't write the type
 * writes PNG instead (Safari and WebP): the server names the file after what it receives.
 */
export function encode(canvas: HTMLCanvasElement, type: EditedType, quality: number): Promise<Blob> {
    return new Promise((resolve, reject) => canvas.toBlob(
        blob => blob ? resolve(blob) : reject(new CanvasTooLargeError(canvas)),
        type,
        Math.min(1, Math.max(0.01, quality / 100))));
}

/** Loads and decodes a picture (EXIF orientation applied, as the viewer shows it). */
export function loadPicture(url: string): Promise<HTMLImageElement> {
    const image = new Image();
    image.decoding = 'async';
    image.src = url;
    return image.decode().then(() => image);
}

/** How the editor loads pictures (tests give it pictures of their own: jsdom loads none). */
export const PICTURE_LOADER = new InjectionToken<(url: string) => Promise<HTMLImageElement>>('PICTURE_LOADER', {
    providedIn: 'root',
    factory: () => loadPicture,
});
