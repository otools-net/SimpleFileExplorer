import { describe, expect, it } from 'vitest';
import { readingTexts } from './reading-texts';

describe('reading texts', () => {
    it('names the choices of width, margins and size', () => {
        expect(readingTexts.width(0)).toBe('Whole width');
        expect(readingTexts.width(72)).toBe('72 characters');
        expect(readingTexts.margin(0)).toBe('None');
        expect(readingTexts.margin(16)).toBe('16 px');
        expect(readingTexts.size(17)).toBe('17 px');
    });
});
