import { Location } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfigService } from '../core/config.service';
import { HelpComponent } from '../shared/help.component';
import { IconComponent } from '../shared/icon.component';
import { SpacePart } from './space-part.component';
import { SpaceView } from './space-view';

/** A link of a space's page to one of its parts, a sub-page of Spaces & accounts. */
interface PartLink {
    page: SpacePart;
    icon: string;
    name: string;
    /** What the sub-page holds; Visitors says whether they are let in instead. */
    line: string;
    title: string;
}

const LINKS: readonly PartLink[] = [
    {
        page: 'groups',
        icon: 'group',
        name: $localize`Groups`,
        line: $localize`Sets of rights: what their members may do in the space.`,
        title: $localize`Open the groups of this space.`,
    },
    {
        page: 'accounts',
        icon: 'user',
        name: $localize`Accounts`,
        line: $localize`People who sign in, each in one or more of the space's groups.`,
        title: $localize`Open the accounts of this space.`,
    },
    {
        page: 'folders',
        icon: 'lock',
        name: $localize`Folder access`,
        line: $localize`Folders hidden from a group or an account, or only read there.`,
        title: $localize`Open the folder rules of this space.`,
    },
    {
        page: 'visitors',
        icon: 'globe',
        name: $localize`Visitors`,
        line: '',
        title: $localize`Choose whether people without an account may browse this space.`,
    },
];

/**
 * One space, laid out as it works: its library (a folder), and the way to its groups (each a set of rights), its
 * accounts (each in some of its groups), its folder rules and the visitors without an account it lets in, each a
 * sub-page of Spaces & accounts about this space. Server administrators open any space here; a space's administrator,
 * their own.
 */
@Component({
    selector: 'sfe-space-detail',
    imports: [IconComponent, HelpComponent, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    host: { class: 'space-detail' },
    templateUrl: './space-detail.component.html',
})
export class SpaceDetailComponent {
    protected readonly config = inject(ConfigService);
    private readonly location = inject(Location);

    readonly space = input.required<SpaceView>();

    protected readonly links = LINKS;
    /** Kept by the entries the links replace, so that "back" still leads where it did. */
    protected readonly state = (this.location.getState() as Record<string, unknown> | null) ?? {};

    /** The address of the sub-page `page` about this space. */
    protected paramsOf(page: SpacePart) {
        return { section: 'access', page, space: this.space().id };
    }
}
