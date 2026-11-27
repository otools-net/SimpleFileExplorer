import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import type { Mock } from 'vitest';
import { TestBed } from '@angular/core/testing';
import { ApiError } from '../core/api-error';
import { Api } from '../core/api.service';
import { mockApi, settle, testProviders, type } from '../../testing/fakes';
import { WizardHarness } from '../../testing/wizard-harness';
import { injectWizard } from './wizard-context';
import { defineWizard, WizardEnd } from './wizard-definition';

interface TestDraft {
    alpha: string;
    skipBeta: boolean;
    gamma: string;
}

/** A step's field: the draft's `alpha`, and its refusal. */
@Component({
    selector: 'sfe-test-alpha',
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <input id="alpha" [value]="wizard.draft().alpha" (input)="wizard.change({ alpha: $any($event.target).value })">
        @if (wizard.errors()['alpha']; as error) {
            <span class="field-error">{{ error }}</span>
        }
    `,
})
class AlphaStep {
    protected readonly wizard = injectWizard<TestDraft>();
}

@Component({ selector: 'sfe-test-empty', changeDetection: ChangeDetectionStrategy.OnPush, template: `<p>empty</p>` })
class EmptyStep {}

let check: Mock<(draft: TestDraft, api: Api) => Promise<void>>;
let finish: Mock<(draft: TestDraft) => Promise<WizardEnd>>;

function testWizard() {
    return defineWizard<TestDraft>({
        id: 'test',
        title: 'Test wizard',
        helpPage: 'administration/wizards.md',
        initialDraft: () => ({ alpha: '', skipBeta: false, gamma: '' }),
        steps: [
            {
                id: 'alpha',
                title: 'Alpha',
                component: AlphaStep,
                explanation: 'The first step.',
                helpAnchor: 'alpha',
                fields: ['alpha'],
                valid: (draft) => draft.alpha !== '',
                check: (draft) => check(draft, inject(Api)),
                review: (draft) => [{ label: 'Alpha', value: draft.alpha }],
            },
            {
                id: 'beta',
                title: 'Beta',
                component: EmptyStep,
                explanation: 'Left out when asked.',
                helpAnchor: 'beta',
                fields: ['beta'],
                valid: () => true,
                skip: (draft) => draft.skipBeta,
                review: () => [{ label: 'Beta', value: 'b' }],
            },
            {
                id: 'gamma',
                title: 'Gamma',
                component: EmptyStep,
                explanation: 'The last step.',
                helpAnchor: 'gamma',
                fields: ['gamma'],
                valid: () => true,
                review: (draft) => [{ label: 'Gamma', value: draft.gamma || '—' }],
            },
        ],
        finish: (draft) => finish(draft),
    });
}

const refusal = (errors: Record<string, string[]>) => new ApiError(400, 'Refused', errors);
const end: WizardEnd = {
    message: 'Made it.',
    links: [{ label: 'See it', title: 'Open what was made', route: 'admin', query: { section: 'access' } }],
};

async function open(): Promise<WizardHarness> {
    TestBed.configureTestingModule({ providers: testProviders(mockApi()) });
    return WizardHarness.open(testWizard());
}

beforeEach(() => {
    check = vi.fn(() => Promise.resolve());
    finish = vi.fn(() => Promise.resolve(end));
});

describe('WizardComponent', () => {
    it('opens on its first step, Next greyed out until the step is filled in', async () => {
        const wizard = await open();

        expect(wizard.title()).toBe('Alpha');
        expect(wizard.progress()).toBe('Step 1 of 4');
        expect(wizard.steps()).toEqual(['Alpha', 'Beta', 'Gamma', 'Review']);
        expect(wizard.button('Next')!.disabled).toBe(true);
        expect(wizard.button('Back')).toBeUndefined();
        type(wizard.element.querySelector<HTMLInputElement>('#alpha')!, 'a');
        await settle();
        expect(wizard.button('Next')!.disabled).toBe(false);
    });

    it('keeps what was typed through Next and Back, and lists every step in the review', async () => {
        const wizard = await open();
        await wizard.fill({ alpha: 'first', gamma: 'third' });

        await wizard.next();
        await wizard.next();
        expect(wizard.title()).toBe('Gamma');
        await wizard.press('Back');
        await wizard.press('Back');
        expect(wizard.element.querySelector<HTMLInputElement>('#alpha')!.value).toBe('first');
        await wizard.next();
        await wizard.next();
        await wizard.next();

        expect(wizard.title()).toBe('Review');
        expect(wizard.progress()).toBe('Step 4 of 4');
        const lines = [...wizard.element.querySelectorAll('.wizard-review-part dd')].map((dd) => dd.textContent);
        expect(lines).toEqual(['first', 'b', 'third']);
        buttonIn(wizard, '.wizard-review-part', 'Change').click();
        await settle();
        expect(wizard.title()).toBe('Alpha');
    });

    it('leaves out a step skipped by the answers, in the count and the list', async () => {
        const wizard = await open();
        await wizard.fill({ alpha: 'a', skipBeta: true });

        await wizard.next();

        expect(wizard.title()).toBe('Gamma');
        expect(wizard.progress()).toBe('Step 2 of 3');
        expect(wizard.steps()).toEqual(['Alpha', 'Gamma', 'Review']);
    });

    it("runs the step's check on Next: its fields' refusals keep it, a later step's wait", async () => {
        const wizard = await open();
        await wizard.fill({ alpha: 'taken' });
        check.mockRejectedValueOnce(refusal({ alpha: ['Already taken.'] }));

        await wizard.next();
        expect(wizard.title()).toBe('Alpha');
        expect(wizard.errors()).toEqual(['Already taken.']);
        expect(check).toHaveBeenCalledWith({ alpha: 'taken', skipBeta: false, gamma: '' }, expect.anything());

        check.mockRejectedValueOnce(refusal({ gamma: ['Not yet.'] }));
        await wizard.next();
        expect(wizard.title()).toBe('Beta');
        expect(wizard.errors()).toEqual([]);
    });

    it('shows an error naming no field above the buttons', async () => {
        const wizard = await open();
        await wizard.fill({ alpha: 'a' });
        check.mockRejectedValueOnce(new Error('The server is away.'));

        await wizard.next();

        expect(wizard.title()).toBe('Alpha');
        expect(wizard.errors()).toEqual(['The server is away.']);
    });

    it('sends nothing before Finish, then shows the end', async () => {
        const wizard = await open();
        await wizard.fill({ alpha: 'a' });
        for (let i = 0; i < 3; i++) await wizard.next();
        expect(finish).not.toHaveBeenCalled();

        await wizard.next();

        expect(finish).toHaveBeenCalledOnce();
        expect(wizard.title()).toBe('Done');
        expect(wizard.progress()).toBe('');
        expect(wizard.element.querySelector('[role="status"]')!.textContent).toBe('Made it.');
        const link = wizard.element.querySelector<HTMLAnchorElement>('.wizard-links a')!;
        expect(link.getAttribute('href')).toBe('/admin?section=access');
        expect(wizard.wizard.canLeave()).toBe(true);
    });

    it('comes back to the step whose field a refused Finish names, with its error', async () => {
        const wizard = await open();
        await wizard.fill({ alpha: 'a' });
        for (let i = 0; i < 3; i++) await wizard.next();
        finish.mockRejectedValueOnce(refusal({ alpha: ['Taken meanwhile.'] }));

        await wizard.next();

        expect(wizard.title()).toBe('Alpha');
        expect(wizard.errors()).toEqual(['Taken meanwhile.']);
        expect(wizard.draft<TestDraft>().alpha).toBe('a');
    });

    it('starts again with nothing chosen (Run again), and closes on Done and Cancel', async () => {
        const wizard = await open();
        await wizard.fill({ alpha: 'a' });
        for (let i = 0; i < 4; i++) await wizard.next();

        await wizard.press('Run again');
        expect(wizard.title()).toBe('Alpha');
        expect(wizard.draft<TestDraft>().alpha).toBe('');
        await wizard.press('Cancel');
        expect(wizard.closed.count).toBe(1);
    });

    it('asks before leaving with something chosen, and before a reload', async () => {
        const wizard = await open();
        const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
        expect(wizard.wizard.canLeave()).toBe(true);
        expect(confirm).not.toHaveBeenCalled();

        await wizard.fill({ alpha: 'a' });
        const unload = new Event('beforeunload', { cancelable: true });
        window.dispatchEvent(unload);

        expect(wizard.wizard.canLeave()).toBe(false);
        expect(confirm).toHaveBeenCalledOnce();
        expect(unload.defaultPrevented).toBe(true);
        confirm.mockRestore();
    });

    it("goes on with Enter in a field, and puts the focus on the new step's title", async () => {
        const wizard = await open();
        await wizard.fill({ alpha: 'a' });

        wizard.element.querySelector('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
        await settle();

        expect(wizard.title()).toBe('Beta');
        expect(document.activeElement).toBe(wizard.element.querySelector('.wizard-stage h2'));
    });
});

function buttonIn(wizard: WizardHarness, selector: string, label: string): HTMLButtonElement {
    return [...wizard.element.querySelectorAll<HTMLButtonElement>(`${selector} button`)].find(
        (button) => button.textContent!.trim() === label,
    )!;
}
