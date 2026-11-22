import { icu } from '../core/i18n/icu';

/**
 * The player's texts: each control's name (short: its menu entry, screen readers) and its hint (a sentence, with the
 * key that does it). Pure functions of the player's state, shared by its buttons, its "⋯" entries and its menu.
 */
export const playerTexts = {
    play: (paused: boolean) => (paused ? $localize`Play` : $localize`Pause`),
    playHint: (paused: boolean) => (paused ? $localize`Play the video (K).` : $localize`Pause the video (K).`),
    mute: (muted: boolean) => (muted ? $localize`Unmute` : $localize`Mute`),
    muteHint: (muted: boolean) => (muted ? $localize`Turn the sound back on (M).` : $localize`Turn the sound off (M).`),
    loop: (looping: boolean) => (looping ? $localize`Stop looping` : $localize`Loop`),
    loopHint: (looping: boolean) =>
        looping
            ? $localize`Stop playing the video again when it ends (R).`
            : $localize`Play the video again from the start each time it ends (R).`,
    cinema: (on: boolean) => (on ? $localize`Default view` : $localize`Cinema mode`),
    cinemaHint: (on: boolean) =>
        on
            ? $localize`Bring the video back to its usual size (C).`
            : $localize`Spread the video over the whole width of the window (C).`,
    fullScreen: (on: boolean) => (on ? $localize`Exit full screen` : $localize`Full screen`),
    fullScreenHint: (on: boolean) =>
        on ? $localize`Leave full screen (F).` : $localize`Show the video on the whole screen (F).`,
    back: (seconds: number) =>
        icu($localize`{seconds, plural, one {Back # second} other {Back # seconds}}`, { seconds }),
    backHint: (seconds: number) =>
        icu($localize`{seconds, plural, one {Go back # second (J).} other {Go back # seconds (J).}}`, { seconds }),
    forward: (seconds: number) =>
        icu($localize`{seconds, plural, one {Forward # second} other {Forward # seconds}}`, { seconds }),
    forwardHint: (seconds: number) =>
        icu($localize`{seconds, plural, one {Go forward # second (L).} other {Go forward # seconds (L).}}`, {
            seconds,
        }),
    /** The time jumped by taps or keys in a row, shown on the video for a moment ("+30 s"). */
    jumped: (sign: string, seconds: number) =>
        $localize`:seconds jumped shown on the video:${sign}:sign:${seconds}:seconds: s`,
    speed: (rate: number) => $localize`Speed: ${rate}:rate:×`,
    speedHint: () => $localize`Choose how fast the video plays.`,
    rate: (rate: number) => $localize`:a playback speed:${rate}:rate:×`,
    rateHint: (rate: number) => $localize`Play the video at ${rate}:rate:× its normal speed.`,
    convert: () => $localize`Play converted`,
    convertHint: () =>
        $localize`Play the video as the server converts it, for a video that shows no picture or plays no \
sound here.`,
    saveFrame: () => $localize`Save this frame`,
    saveFrameHint: () => $localize`Save the frame shown as a picture (S).`,
    pictureInPicture: () => $localize`Picture in picture`,
    pictureInPictureHint: () => $localize`Keep the video playing in a small window, above the other windows.`,
};

/** The links to a video that the viewer's and the player page's menus give. */
export const videoLinkTexts = {
    copyTimeLink: () => $localize`Copy link at this time`,
    copyTimeLinkHint: () => $localize`Copy a link that plays the video from where it is now.`,
    external: (player: string | null | undefined) => $localize`Open in ${player}:player:`,
    externalHint: (player: string | null | undefined) =>
        $localize`Play it in ${player}:player: through a link that works without signing in, for a while.`,
};
