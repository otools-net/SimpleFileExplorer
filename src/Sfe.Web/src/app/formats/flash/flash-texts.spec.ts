import { flashTexts } from './flash-texts';

describe('flashTexts', () => {
    it('names each button by what a click does, with a hint as a whole sentence', () => {
        expect(flashTexts.play(false)).toBe('Play');
        expect(flashTexts.play(true)).toBe('Pause');
        expect(flashTexts.playHint(true)).toBe('Pause the movie.');
        expect(flashTexts.mute(false)).toBe('Mute');
        expect(flashTexts.mute(true)).toBe('Unmute');
        expect(flashTexts.muteHint(true)).toBe('Turn the sound back on.');
        expect(flashTexts.restartHint()).toBe('Play the movie again from the start.');
    });

    it('tells whether the movie plays, and its frame rate', () => {
        expect(flashTexts.state(true)).toBe('Playing');
        expect(flashTexts.state(false)).toBe('Paused');
        expect(flashTexts.frameRate(29.97)).toBe('30 fps');
        expect(flashTexts.frameRate(12.5)).toBe('12.5 fps');
    });

    it('says why a movie did not play, as a sentence', () => {
        expect(flashTexts.couldNotPlay('Invalid SWF')).toBe('Could not play the movie: Invalid SWF.');
        expect(flashTexts.couldNotPlay("Ruffle didn't start in time.")).toBe(
            "Could not play the movie: Ruffle didn't start in time.",
        );
    });
});
