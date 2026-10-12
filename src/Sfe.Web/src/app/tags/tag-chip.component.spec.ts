import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TagSummary } from '../core/models';
import { ApiMock, loadConfig, mockApi, summary, tag, testProviders } from '../../testing/fakes';
import { TagChipComponent } from './tag-chip.component';

describe('TagChipComponent', () => {
    let api: ApiMock;
    let fixture: ComponentFixture<TagChipComponent>;
    let element: HTMLElement;

    beforeEach(async () => {
        api = mockApi();
        TestBed.configureTestingModule({ providers: testProviders(api) });
        await loadConfig(api);
        fixture = TestBed.createComponent(TagChipComponent);
        element = fixture.nativeElement;
    });

    async function show(chip: TagSummary, inputs: Record<string, unknown> = {}): Promise<void> {
        fixture.componentRef.setInput('tag', chip);
        for (const [name, value] of Object.entries(inputs))
            fixture.componentRef.setInput(name, value);
        await fixture.whenStable();
    }

    it('shows the display name as plain text', async () => {
        await show(summary(tag('Freddy', { displayName: 'Freddy (FNAF)' })));

        expect(element.querySelector('span.tag-name')!.textContent).toBe('Freddy (FNAF)');
        expect(element.querySelector('span.tag-name')!.getAttribute('title')).toBe('Freddy (FNAF)');
        expect(element.querySelector('button')).toBeNull();
        expect(element.classList).toContain('tag-chip');
    });

    it('falls back to the name when there is no display name', async () => {
        await show({ ...summary(tag('Cat')), displayName: '' });

        expect(element.querySelector('.tag-name')!.textContent).toBe('Cat');
    });

    it('shows a label in place of the name, and a title in place of the label', async () => {
        await show(summary(tag('Cat')), { label: 'Cat · 3', title: 'On 3 of 5 files' });

        expect(element.querySelector('.tag-name')!.textContent).toBe('Cat · 3');
        expect(element.querySelector('.tag-name')!.getAttribute('title')).toBe('On 3 of 5 files');
    });

    it('takes the color of its slug, with readable text', async () => {
        await show(summary(tag('Gold', { color: 'yellow' })));

        expect(element.classList).toContain('colored');
        expect(element.style.getPropertyValue('--chip-bg')).toBe('#ffd63d');
        expect(element.style.getPropertyValue('--chip-text')).toBe('#16181d');
    });

    it('stays uncolored for an unknown color, and without one', async () => {
        await show(summary(tag('Odd', { color: 'plaid' })));
        // The class follows the slug, the colors only known ones.
        expect(element.style.getPropertyValue('--chip-bg')).toBe('');

        await show(summary(tag('Plain')));
        expect(element.classList).not.toContain('colored');
        expect(element.style.getPropertyValue('--chip-bg')).toBe('');
    });

    it('marks hidden tags and carries the tag id', async () => {
        const hidden = summary(tag('Archived', { isHidden: true }));
        await show(hidden);

        expect(element.classList).toContain('hidden-tag');
        expect(element.dataset['tagId']).toBe(String(hidden.id));
    });

    it('emits pick when clickable, and remove from its × button', async () => {
        const chip = summary(tag('Cat'));
        await show(chip, { clickable: true, removable: true });
        const picked: TagSummary[] = [];
        const removed: TagSummary[] = [];
        fixture.componentInstance.pick.subscribe(t => picked.push(t));
        fixture.componentInstance.remove.subscribe(t => removed.push(t));

        element.querySelector<HTMLButtonElement>('button.tag-name')!.click();
        const remove = element.querySelector<HTMLButtonElement>('button.tag-remove')!;
        remove.click();

        expect(picked).toEqual([chip]);
        expect(removed).toEqual([chip]);
        expect(remove.getAttribute('aria-label')).toBe('Remove Cat');
        expect(remove.title).toBe('Remove Cat');
    });
});
