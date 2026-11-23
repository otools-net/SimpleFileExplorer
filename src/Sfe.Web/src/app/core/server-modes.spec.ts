import { describe, expect, it } from 'vitest';
import { modeName, modeSummary, onlyInModeNote } from './server-modes';

describe('server modes', () => {
    it('names each mode as the Installation page does', () => {
        expect(modeName('local')).toBe('Local');
        expect(modeName('external')).toBe('External');
    });

    it('says what each mode means in one sentence', () => {
        expect(modeSummary('local')).toBe(
            'Local mode: only this computer opens SFE, only administrators sign in, and there are no visitors.',
        );
        expect(modeSummary('external')).toContain('the library folder and ffmpeg come from the server');
    });

    it('says why something only the other mode uses is greyed out', () => {
        expect(onlyInModeNote('local')).toBe('Only in Local mode: this server is in External mode.');
        expect(onlyInModeNote('external')).toBe('Only in External mode: this server is in Local mode.');
    });
});
