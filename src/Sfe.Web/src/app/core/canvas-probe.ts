import { inject } from '@angular/core';
import { ToastService } from './toast.service';

/*
 * Whether the page may read back what it draws on a canvas, asked before work that needs it (the picture editor's
 * Save, "Save this frame"). Firefox with `privacy.resistFingerprinting` gives a blank or random picture instead, and
 * asks the user for permission only when the read is done inside a click or key press: so the probe runs there, before
 * the work, rather than when saving, after the edits are made.
 */

/** The colour drawn and read back: none of what a blocked read gives (white, black, transparent, most noise). */
const PROBE_COLOUR = [12, 140, 210];
/** How far a channel may drift: Firefox's milder protection nudges a few values, which pictures survive. */
const TOLERANCE = 16;
/**
 * The probe's size: large enough not to be taken for a tiny canvas (favicons) that a browser might let through
 * while blocking real pictures.
 */
const PROBE_SIZE = 32;
/** How long the explanation stays: it is long. */
const REFUSED_TIMEOUT_MS = 15000;

export const CANVAS_REFUSED =
    'This browser does not let the page read the pictures it draws, which editing a picture and saving a video ' +
    'frame need. Firefox does this when privacy.resistFingerprinting is on: allow the permission it asks for ' +
    '(the picture icon in the address bar), then try again.';

/** Draws a known colour and reads it back; false when the browser refuses, changes it or has no 2D canvas. */
export function canvasReadable(): boolean {
    try {
        const canvas = Object.assign(document.createElement('canvas'), { width: PROBE_SIZE, height: PROBE_SIZE });
        const context = canvas.getContext('2d', { willReadFrequently: true });
        if (!context) return false;
        context.fillStyle = `rgb(${PROBE_COLOUR.join(', ')})`;
        context.fillRect(0, 0, PROBE_SIZE, PROBE_SIZE);
        const data = context.getImageData(0, 0, PROBE_SIZE, PROBE_SIZE).data;
        const centre = ((PROBE_SIZE / 2) * PROBE_SIZE + PROBE_SIZE / 2) * 4;
        return (
            data[centre + 3] === 255 &&
            PROBE_COLOUR.every((value, channel) => Math.abs(data[centre + channel] - value) <= TOLERANCE)
        );
    } catch {
        return false; // a SecurityError, or no canvas at all
    }
}

/**
 * The probe for a component (call it in a field initializer): true when canvases can be read back, else false after
 * showing why in a toast. Call it at the start of the user's click or key press, before any `await`.
 */
export function canvasCheck(): () => boolean {
    const toasts = inject(ToastService);
    return () => {
        if (canvasReadable()) return true;
        toasts.error(CANVAS_REFUSED, REFUSED_TIMEOUT_MS);
        return false;
    };
}
