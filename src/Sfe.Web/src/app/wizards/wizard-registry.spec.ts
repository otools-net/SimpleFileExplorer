import { readFileSync } from 'node:fs';
import { ADMIN_SECTIONS } from '../admin/admin-sections';
import { searchAdmin } from '../admin/admin-search';
import { Rights } from '../core/rights';
import { helpAnchors } from '../../testing/help-anchors';
import { canOpenWizard, findWizard, WIZARDS } from './wizard-registry';

/** Every registered wizard, checked once here: a new wizard needs no test of these. */
describe('WIZARDS', () => {
    it('have distinct ids, and each loads the definition of its own id and title', async () => {
        expect(new Set(WIZARDS.map((wizard) => wizard.id)).size).toBe(WIZARDS.length);
        for (const wizard of WIZARDS) {
            const definition = await wizard.load();
            expect(definition.id).toBe(wizard.id);
            expect(definition.title).toBe(wizard.title);
            expect(definition.steps.length, wizard.id).toBeGreaterThan(0);
        }
    });

    it('link to a page of the help with a heading for every step', async () => {
        for (const wizard of WIZARDS) {
            const definition = await wizard.load();
            const anchors = helpAnchors(definition.helpPage);
            for (const step of definition.steps)
                expect(anchors.has(step.helpAnchor), `${definition.helpPage}#${step.helpAnchor}`).toBe(true);
        }
    });

    it('name a right that exists and an administration page that exists', () => {
        const rights = new Set<string>(Object.values(Rights));
        for (const wizard of WIZARDS) {
            expect(rights.has(wizard.right), wizard.id).toBe(true);
            const section = ADMIN_SECTIONS.find((candidate) => candidate.id === wizard.home.section);
            expect(section, wizard.id).toBeDefined();
            if (wizard.home.page)
                expect(
                    section!.subpages.some((page) => page.id === wizard.home.page),
                    wizard.id,
                ).toBe(true);
        }
    });

    it('are found by the administration search, by their title', () => {
        for (const wizard of WIZARDS) {
            const found = searchAdmin(wizard.title, ADMIN_SECTIONS, WIZARDS);
            expect(
                found.some((result) => result.wizard === wizard.id),
                wizard.id,
            ).toBe(true);
        }
    });

    it('load their code only when opened: nothing eager imports a wizard', () => {
        const eager = ['app/wizards/wizard-registry.ts', 'app/app.routes.ts', 'app/admin/admin-search.ts'];
        for (const file of eager) {
            const source = readFileSync(`src/${file}`, 'utf8');
            for (const wizard of WIZARDS)
                expect(source, file).not.toMatch(new RegExp(`^import [^;]*'\\./(wizards/)?${wizard.id}/`, 'm'));
        }
    });

    it('open for the sessions with their right, in their server mode', () => {
        const wizard = { ...findWizard('new-space')!, onlyIn: 'external' as const };
        const all = () => true;

        expect(canOpenWizard(wizard, all, 'external')).toBe(true);
        expect(canOpenWizard(wizard, all, 'local')).toBe(false);
        expect(canOpenWizard(wizard, () => false, 'external')).toBe(false);
        expect(findWizard('nothing')).toBeUndefined();
    });
});
