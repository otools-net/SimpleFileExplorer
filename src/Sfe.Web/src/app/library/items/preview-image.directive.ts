import { computed, Directive, input, linkedSignal, output, signal } from '@angular/core';

/**
 * A lazy preview, `<img [sfePreview]="sources">`, trying each source in turn: `failed` is emitted when none could
 * be shown, `shape` (width / height) once one has loaded. Thumbnails keep the shape of the original, so either
 * source gives the tile its size.
 */
@Directive({
    selector: 'img[sfePreview]',
    host: {
        alt: '',
        loading: 'lazy',
        decoding: 'async',
        '[src]': 'src()',
        '[class.loaded]': 'loaded()',
        '(load)': 'onLoad($event)',
        '(error)': 'onError()',
    },
})
export class PreviewImageDirective {
    readonly sfePreview = input.required<string[]>();
    readonly shape = output<number>();
    readonly failed = output<void>();

    /** Starts over when the sources change (e.g. the original instead of the thumbnail after a change of view). */
    private readonly index = linkedSignal({ source: this.sfePreview, computation: () => 0 });
    protected readonly loaded = signal(false);
    protected readonly src = computed(() => this.sfePreview()[this.index()] ?? '');

    protected onLoad(event: Event): void {
        const image = event.target as HTMLImageElement;
        if (image.naturalWidth && image.naturalHeight)
            this.shape.emit(image.naturalWidth / image.naturalHeight);
        this.loaded.set(true);
    }

    protected onError(): void {
        if (this.index() + 1 < this.sfePreview().length)
            this.index.update(index => index + 1);
        else
            this.failed.emit();
    }
}
