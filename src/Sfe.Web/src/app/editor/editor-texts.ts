import { Size } from './edits';
import { formatSize } from '../core/format';

/** A picture's size in pixels, as the editor shows it ("800 × 600"). */
export function dimensions({ width, height }: Size): string {
    return $localize`:a picture's width and height:${width}:width: × ${height}:height:`;
}

/** The size of the picture opened, and the size it will have once edited ("4000 × 3000 → 1600 × 1200"). */
export function sizeLine(original: Size, edited: Size | null): string {
    if (!edited) return dimensions(original);
    const before = dimensions(original);
    const after = dimensions(edited);
    return $localize`:sizes before and after the edits:${before}:original: → ${after}:edited:`;
}

/** The hint of Save, when it writes over the original. `permanent`: this space keeps no previous version. */
export function overwriteHint(permanent: boolean): string {
    return permanent
        ? $localize`Save the edits over the original picture. This space keeps no previous version (Ctrl+S).`
        : $localize`Save the edits over the original picture. The version before goes to the trash (Ctrl+S).`;
}

/**
 * Where the editor saves, under its buttons: a copy goes to `folder` (Administration › Media › Picture editor) or
 * next to the original; with `overwrite`, Save writes over the original too.
 */
export function destinationLine(folder: string | null, overwrite: boolean, permanent: boolean): string {
    if (!overwrite)
        return folder
            ? $localize`Saved as a new file in “${folder}:folder:”, in the same folders as the original.`
            : $localize`Saved as a new file next to the original, with “(edited)” in its name.`;
    const over = permanent
        ? $localize`Save writes over the original.`
        : $localize`Save writes over the original, and the version before goes to the trash.`;
    const copy = folder
        ? $localize`Save a copy makes a new file in “${folder}:folder:”, in the same folders as the original.`
        : $localize`Save a copy makes a new file next to the original, with “(edited)” in its name.`;
    return `${over} ${copy}`;
}

/** The hint of an aspect ratio's button; `size` is the picture's (0 × 0 while it loads). */
export function aspectHint(id: string, label: string, size: Size): string {
    if (id === 'free') return $localize`Free: the frame takes any width and height.`;
    if (id === 'original')
        return size.width
            ? $localize`Original: the frame keeps the picture's own shape, ${dimensions(size)}:size:.`
            : $localize`Original: the frame keeps the picture's own shape.`;
    if (id === '1:1') return $localize`Square (1:1): the frame keeps this shape.`;
    return $localize`${label}:ratio:: the frame keeps this shape.`;
}

/**
 * The hint of the button turning the ratio between landscape and portrait: `flipped` is the ratio it would use
 * (null without a ratio), `portrait` whether the current one is.
 */
export function flipHint(flipped: string | null, portrait: boolean): string {
    if (!flipped) return $localize`Pick a ratio first, then this turns it between landscape and portrait (X).`;
    return portrait
        ? $localize`Use ${flipped}:ratio: instead: the same ratio, in landscape (X).`
        : $localize`Use ${flipped}:ratio: instead: the same ratio, in portrait (X).`;
}

/** The toast of Save a copy: where the new file is, its size in pixels and bytes. */
export function savedCopyToast(path: string, size: Size, bytes: number): string {
    return $localize`Saved as “${path}:path:” (${dimensions(size)}:size:, ${formatSize(bytes)}:bytes:).`;
}

/** The toast of Save over the original; `permanent`: this space keeps no previous version. */
export function savedOverToast(name: string, size: Size, bytes: number, permanent: boolean): string {
    const pixels = dimensions(size);
    const weight = formatSize(bytes);
    return permanent
        ? $localize`“${name}:name:” saved (${pixels}:size:, ${weight}:bytes:).`
        : $localize`“${name}:name:” saved (${pixels}:size:, ${weight}:bytes:). The version before is in the trash.`;
}

/** The error toast of a save, with the reason when there is one. */
export function notSavedToast(reason?: string | null): string {
    return reason ? $localize`Could not save the picture: ${reason}:reason:` : $localize`Could not save the picture.`;
}

/** The question when the picture changed since the editor opened it. */
export function changedElsewhereQuestion(): string {
    return $localize`The picture was changed elsewhere since the editor opened it. Save over it anyway?`;
}

/** The question before closing the editor with edits not saved. */
export function discardQuestion(): string {
    return $localize`Close the editor and lose these edits? This can't be undone.`;

}
