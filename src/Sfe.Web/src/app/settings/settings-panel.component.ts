import { NgTemplateOutlet } from '@angular/common';
import { afterNextRender, ChangeDetectionStrategy, Component, computed, effect, inject, Injector, input, output, untracked, viewChild } from '@angular/core';
import { ThemeChoice, ThemeService } from '../core/theme.service';
import { checkedOf, valueOf } from '../shared/events';
import { IconComponent } from '../shared/icon.component';
import { TrickplayStatusComponent } from '../trickplay/trickplay-status.component';
import { isSimpleGroup, settingFieldId, SettingsForm } from './settings-form.service';
import { SettingField, TABS } from './settings-schema';

/**
 * One tab of settings-schema.ts, as a sub-page of its section of the administration page: its grouped fields,
 * bound to the page's SettingsForm. Submitting (Enter in a field) asks the page to save (`submitted`).
 */
@Component({
    selector: 'sfe-settings-panel',
    imports: [NgTemplateOutlet, IconComponent, TrickplayStatusComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        @if (form.loadError(); as error) {
            <div class="settings-panel">
                <p class="form-error">Could not load the settings: {{ error }}</p>
                <button type="button" class="button small" title="Ask the server for the settings again" (click)="form.load()">Try again</button>
            </div>
        } @else if (!form.loaded()) {
            <p class="muted">Loading the settings…</p>
        } @else {
            <form class="settings-form" novalidate (submit)="submit($event)">
                <div class="settings-panel" [id]="'settings-panel-' + tab()">
                    @if (schema().intro && form.level() !== 'user') {
                        <p class="settings-intro">{{ schema().intro }}</p>
                    }
                    @if (onlyAdvanced()) {
                        <p class="settings-intro settings-advanced-note">
                            The settings here are only shown in the full view.
                            <button type="button" class="button small" title="Show every setting instead of only the most used ones" (click)="form.setShowAll(true)">Show all settings</button>
                        </p>
                    }
                    @for (group of schema().groups; track group.title) {
                        <section class="settings-group" [hidden]="form.groupHidden(group)">
                            <header class="settings-group-head">
                                <h3>{{ group.title }}</h3>
                                @if (group.hint) {
                                    <p class="field-hint">{{ group.hint }}</p>
                                }
                            </header>
                            @if (group.key && form.errors()[group.key]) {
                                <p class="form-error">{{ form.errors()[group.key] }}</p>
                            }
                            <div class="settings-grid">
                                @for (field of group.fields; track field.key) {
                                    <ng-container [ngTemplateOutlet]="fieldTemplate" [ngTemplateOutletContext]="{ $implicit: field }" />
                                }
                            </div>
                            <!-- A user's preferences only offer what concerns this browser. -->
                            @switch (form.level() === 'user' && group.action !== 'browserTheme' && group.action !== 'clearSearchHistory' ? null : group.action) {
                                @case ('clearHistory') {
                                    <div class="settings-action">
                                        <button type="button" class="button small" title="Empty the sidebar's “Last searched” and “Last viewed” lists (asks first)" (click)="form.clearHistory()">Clear tag history</button>
                                        <span class="field-hint">Forgets the searched and viewed tags right away. Tags and files are not changed.</span>
                                    </div>
                                }
                                @case ('clearSearchHistory') {
                                    <div class="settings-action">
                                        <button type="button" class="button small" title="Forget the searches this browser suggests again (asks first)" (click)="form.clearSearchHistory()">Clear search history</button>
                                        <span class="field-hint">Forgets the searches kept by this browser. Other browsers keep theirs.</span>
                                    </div>
                                }
                                @case ('trickplayStatus') {
                                    <sfe-trickplay-status (activity)="activity.emit()" />
                                }
                                @case ('browserTheme') {
                                    <!-- Not part of the form: kept by this browser and applied right away. -->
                                    <div class="settings-grid">
                                        <div class="field narrow">
                                            <label class="field-label" for="browser-theme">This browser's theme</label>
                                            <select id="browser-theme" aria-describedby="browser-theme-hint" (change)="chooseTheme(valueOf($event))">
                                                <option value="" [selected]="theme.current() === ''">Server default ({{ theme.defaultName() }})</option>
                                                <option value="light" [selected]="theme.current() === 'light'">Light</option>
                                                <option value="dark" [selected]="theme.current() === 'dark'">Dark</option>
                                            </select>
                                            <span class="field-hint" id="browser-theme-hint">Colors of the pages in this browser, applied right away without saving. Kept in this browser only; the other browsers and devices are not changed.</span>
                                        </div>
                                    </div>
                                }
                            }
                        </section>
                    }
                </div>
                <!-- Enter in a field submits the form: a hidden default button, the page's bar has the visible one. -->
                <button type="submit" hidden aria-hidden="true" tabindex="-1"></button>
            </form>
        }

        <ng-template #fieldTemplate let-field>
            @let id = fieldIdOf(field.key);
            @let error = form.errors()[field.key];
            @if (field.type === 'checkbox') {
                <div class="span setting-check" [hidden]="form.fieldHidden(field)">
                    <label class="check">
                        <input type="checkbox" [id]="id" [attr.aria-describedby]="id + '-hint'" [disabled]="form.fieldLocked(field)" [checked]="form.control(field.key)"
                               (change)="form.setControl(field.key, checkedOf($event))">
                        <span>
                            <strong>{{ field.label }}</strong>
                            <span class="field-hint" [id]="id + '-hint'">{{ field.hint }}</span>
                            @if (error) {
                                <span class="field-error">{{ error }}</span>
                            }
                        </span>
                    </label>
                    <ng-container [ngTemplateOutlet]="lockTemplate" [ngTemplateOutletContext]="{ $implicit: field }" />
                </div>
            } @else {
                <!-- Numbers and lists of choices share a row two by two; the other fields take the whole width. -->
                <div class="field" [class.narrow]="field.type === 'number' || field.type === 'select'" [class.span]="field.type !== 'number' && field.type !== 'select'" [hidden]="form.fieldHidden(field)">
                    <label class="field-label" [for]="id">{{ field.label }}</label>
                    @switch (field.type) {
                        @case ('preset') {
                            <select [id]="id" [attr.aria-describedby]="id + '-hint'" [disabled]="form.fieldLocked(field)" (change)="applyPreset(field, valueOf($event))">
                                <option value="" [selected]="form.presetValue(field) === ''">None (no button)</option>
                                @for (preset of field.presets; track $index) {
                                    <option [value]="'' + $index" [selected]="form.presetValue(field) === '' + $index">{{ preset[0] }}</option>
                                }
                                <option value="custom" [selected]="form.presetValue(field) === 'custom'">Custom…</option>
                            </select>
                        }
                        @case ('select') {
                            <select [id]="id" [attr.aria-describedby]="id + '-hint'" [disabled]="form.fieldLocked(field)" (change)="form.setControl(field.key, valueOf($event))">
                                @for (option of field.options; track option[0]) {
                                    <option [value]="option[0]" [selected]="form.control(field.key) === option[0]">{{ option[1] }}</option>
                                }
                            </select>
                        }
                        @case ('list') {
                            <textarea [id]="id" rows="3" spellcheck="false" [placeholder]="field.placeholder ?? ''" [attr.aria-describedby]="id + '-hint'"
                                      [disabled]="form.fieldLocked(field)" [value]="form.control(field.key)" (input)="form.setControl(field.key, valueOf($event))"></textarea>
                        }
                        @default {
                            <input [id]="id" [type]="field.type === 'number' ? 'number' : 'text'" [attr.min]="field.min ?? null" [attr.max]="field.max ?? null"
                                   [placeholder]="field.placeholder ?? ''" spellcheck="false" autocomplete="off" [attr.aria-describedby]="id + '-hint'"
                                   [disabled]="form.fieldDisabled(field) || form.fieldLocked(field)" [value]="form.control(field.key)" (input)="form.setControl(field.key, valueOf($event))">
                        }
                    }
                    <span class="field-hint" [id]="id + '-hint'">{{ field.hint }}</span>
                    @if (error) {
                        <span class="field-error">{{ error }}</span>
                    }
                    <ng-container [ngTemplateOutlet]="lockTemplate" [ngTemplateOutletContext]="{ $implicit: field }" />
                </div>
            }
        </ng-template>

        <!-- Why a setting cannot change at this level, or the box locking it for the levels below. -->
        <ng-template #lockTemplate let-field>
            @if (form.lockNote(field); as note) {
                <span class="setting-lock-note"><svg sfeIcon="lock"></svg>{{ note }}</span>
            } @else if (form.lockable(field)) {
                <label class="setting-lock" [title]="lockHint()">
                    <input type="checkbox" [checked]="form.locked(field.key)" (change)="form.setLocked(field.key, checkedOf($event))">
                    <svg sfeIcon="lock"></svg>{{ lockLabel() }}
                </label>
            }
        </ng-template>
    `,
})
export class SettingsPanelComponent {
    /** The id of a tab of settings-schema.ts. */
    readonly tab = input.required<string>();
    /** Trickplay's "Show activity". */
    readonly activity = output<void>();
    readonly submitted = output<void>();

    protected readonly form = inject(SettingsForm);
    protected readonly theme = inject(ThemeService);
    private readonly injector = inject(Injector);
    private readonly trickplayStatus = viewChild(TrickplayStatusComponent);

    protected readonly schema = computed(() => TABS.find(tab => tab.id === this.tab())!);
    /** In the simple view, a tab without any of its settings shows a way to the full view instead. */
    protected readonly onlyAdvanced = computed(() => this.form.level() !== 'user' && !this.form.showAll() && !this.schema().groups.some(isSimpleGroup));
    protected readonly lockLabel = computed(() => this.form.level() === 'server' ? 'Lock for spaces and users' : 'Lock for users');
    protected readonly lockHint = computed(() => this.form.level() === 'server'
        ? 'Spaces and users cannot change it: the value above applies everywhere'
        : 'Users of the space cannot change it for themselves');

    protected readonly valueOf = valueOf;
    protected readonly checkedOf = checkedOf;
    protected readonly fieldIdOf = settingFieldId;

    constructor() {
        // The trickplay job's state is refreshed while it is shown (it stops itself when destroyed).
        effect(() => {
            const status = this.trickplayStatus();
            untracked(() => status?.start());
        });
    }

    protected submit(event: Event): void {
        event.preventDefault();
        this.submitted.emit();
    }

    protected chooseTheme(choice: string): void {
        this.theme.choose(choice as ThemeChoice);
    }

    protected applyPreset(field: SettingField, value: string): void {
        const focus = this.form.applyPreset(field, value);
        if (focus)
            afterNextRender(() => document.getElementById(settingFieldId(focus))?.focus(), { injector: this.injector });
    }
}
