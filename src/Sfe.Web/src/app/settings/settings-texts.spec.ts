import { partlySaved, resetTexts } from './settings-texts';

describe('settings texts', () => {
    it('says which level was saved and which needs fixing', () => {
        expect(partlySaved('tenant')).toBe('Settings of the whole server saved. Those of this space need fixing.');
        expect(partlySaved('server')).toBe('Settings of this space saved. Those of the whole server need fixing.');
    });

    it('asks before resetting a level, and says what it follows then', () => {
        expect(resetTexts('server')?.question).toContain("configuration file? This cannot be undone.");
        expect(resetTexts('tenant')?.done).toBe("This space follows the server's settings again.");
        expect(resetTexts('user')?.done).toBe("Your settings follow the space's again.");
        expect(resetTexts('environment')).toBeNull();
    });
});
