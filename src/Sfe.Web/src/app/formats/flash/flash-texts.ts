/**
 * The Flash viewer's texts that depend on its state: each button's name (short: menus, screen readers) and its hint
 * (a sentence), what the movie is, why it didn't play. Pure functions.
 */
export const flashTexts = {
    play: (playing: boolean) => (playing ? $localize`Pause` : $localize`Play`),
    playHint: (playing: boolean) => (playing ? $localize`Pause the movie.` : $localize`Play the movie.`),
    restart: () => $localize`Play again`,
    restartHint: () => $localize`Play the movie again from the start.`,
    mute: (muted: boolean) => (muted ? $localize`Unmute` : $localize`Mute`),
    muteHint: (muted: boolean) => (muted ? $localize`Turn the sound back on.` : $localize`Turn the sound off.`),
    /** The line at the bottom: whether the movie plays. */
    state: (playing: boolean) => (playing ? $localize`Playing` : $localize`Paused`),
    frameRate: (fps: number) => $localize`:frames per second:${Math.round(fps * 10) / 10}:fps: fps`,
    /** The reason as the frame or Ruffle gives it, ended by a stop when it has none. */
    couldNotPlay: (reason: string) =>
        $localize`Could not play the movie: ${/[.!?…]$/.test(reason) ? reason : `${reason}.`}:reason:`,
};
