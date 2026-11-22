import { playerTexts as texts, videoLinkTexts as links } from './player-texts';

describe('player texts', () => {
    it('names each control shortly and hints at it with a sentence and its key', () => {
        expect([texts.play(true), texts.playHint(true)]).toEqual(['Play', 'Play the video (K).']);
        expect([texts.play(false), texts.playHint(false)]).toEqual(['Pause', 'Pause the video (K).']);
        expect([texts.mute(true), texts.muteHint(true)]).toEqual(['Unmute', 'Turn the sound back on (M).']);
        expect([texts.fullScreen(true), texts.fullScreenHint(true)]).toEqual([
            'Exit full screen',
            'Leave full screen (F).',
        ]);
        expect(texts.loop(true)).toBe('Stop looping');
        expect(texts.cinema(false)).toBe('Cinema mode');
    });

    it('counts the seconds of a jump', () => {
        expect(texts.back(1)).toBe('Back 1 second');
        expect(texts.back(10)).toBe('Back 10 seconds');
        expect(texts.forwardHint(30)).toBe('Go forward 30 seconds (L).');
        expect(texts.backHint(1)).toBe('Go back 1 second (J).');
        expect(texts.jumped('−', 20)).toBe('−20 s');
    });

    it('writes the speeds with ×', () => {
        expect(texts.speed(1.5)).toBe('Speed: 1.5×');
        expect(texts.rate(0.75)).toBe('0.75×');
        expect(texts.rateHint(2)).toBe('Play the video at 2× its normal speed.');
    });

    it('names the external player in its link', () => {
        expect(links.external('VLC')).toBe('Open in VLC');
        expect(links.externalHint('VLC')).toBe(
            'Play it in VLC through a link that works without signing in, for a while.',
        );
    });
});
