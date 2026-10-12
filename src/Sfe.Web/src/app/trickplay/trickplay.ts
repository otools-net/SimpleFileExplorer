import { Api } from '../core/api.service';
import { TrickplayManifest } from '../core/models';

const PLAYER_RETRY_MS = 4000;       // the player waiting for the trickplay of its video

/**
 * Trickplay of the video at `path` once it is there: while the job has it waiting or running, it is asked for again
 * until `isCurrent()` turns false (`onWaiting()` is called each time). Null when there is none to come (outside the
 * folders, failed, off).
 */
export async function waitForTrickplay(api: Api, path: string, isCurrent: () => boolean, onWaiting = () => { }): Promise<TrickplayManifest | null> {
    while (true) {
        const trickplay = await api.trickplay(path).catch(() => null);
        if (trickplay || !isCurrent())
            return trickplay;
        // The request queued it (videos of the configured folders).
        const result = await api.trickplayStates([path]).catch(() => null);
        if (!result?.files[path])
            return api.trickplay(path).catch(() => null);  // nothing coming, unless it just got done
        onWaiting();
        await new Promise(resolve => setTimeout(resolve, PLAYER_RETRY_MS));
        if (!isCurrent())
            return null;
    }
}
