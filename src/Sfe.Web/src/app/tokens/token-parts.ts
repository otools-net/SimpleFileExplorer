import { SessionTenant } from '../core/access.models';
import { AccessTokenInfo, AccessTokensConfig } from '../core/access-token.models';
import { ClientConfig } from '../core/config.models';
import { formatDate, joinParts } from '../core/format';
import { icu } from '../core/i18n/icu';
import { RightDefinition } from '../access/access.models';

/** Where a token stands: usable, past its expiry, or revoked. */
export type TokenState = 'active' | 'expired' | 'revoked';

/** A life offered for a new token: `days` null is the longest allowed (never expiring when there is no limit). */
export interface ExpiryChoice {
    days: number | null;
    label: string;
}

/** The rights of one area, offered for a new token. */
export interface RightArea {
    title: string;
    rights: { id: string; title: string; description: string }[];
}

const USUAL_LIVES: ExpiryChoice[] = [
    { days: 7, label: $localize`7 days` },
    { days: 30, label: $localize`30 days` },
    { days: 90, label: $localize`90 days` },
    { days: 365, label: $localize`1 year` },
];

/** Whether `token` still works at `now` (as far as the token itself tells: its account may have lost rights). */
export function tokenState(token: AccessTokenInfo, now = Date.now()): TokenState {
    if (token.revoked) return 'revoked';
    if (token.expires && Date.parse(token.expires) <= now) return 'expired';
    return 'active';
}

/** The space a token works in, by name; every space of its account when it names none. */
export function tokenLibrary(token: AccessTokenInfo, tenants: readonly SessionTenant[]): string {
    if (token.tenantId === null) return $localize`Every space`;
    return tenants.find((tenant) => tenant.id === token.tenantId)?.name ?? $localize`Space ${token.tenantId}:id:`;
}

/** When a token was made, when it ends (or ended) and when it was last used, as one line. */
export function tokenDates(token: AccessTokenInfo, now = Date.now()): string {
    const state = tokenState(token, now);
    const expires = token.expires ? formatDate(token.expires) : null;
    const end =
        state === 'revoked'
            ? $localize`Revoked ${formatDate(token.revoked)}:date:`
            : expires === null
              ? $localize`Never expires`
              : state === 'expired'
                ? $localize`Expired ${expires}:date:`
                : $localize`Expires ${expires}:date:`;
    return joinParts([
        $localize`Created ${formatDate(token.created)}:date:`,
        end,
        token.lastUsed ? $localize`Last used ${formatDate(token.lastUsed)}:date:` : $localize`Never used`,
    ]);
}

/** What a token may do, in a few words: all its account's rights, or how many of them. */
export function rightsSummary(token: AccessTokenInfo): string {
    const count = token.rights.length;
    return count
        ? icu($localize`{count, plural, one {# right} other {# rights}}`, { count })
        : $localize`All of the account's rights`;
}

/** What `api/config` tells of tokens (`true`, or an object with their longest life); null when they are off. */
export function tokensConfig(config: ClientConfig | null): AccessTokensConfig | null {
    const tokens = config?.accessTokens;
    if (typeof tokens === 'boolean') return tokens ? { enabled: true } : null;
    return tokens?.enabled ? tokens : null;
}

/**
 * The lives a new token may have under `maxLifetimeDays` (0: no limit; unknown: the server's limit), the longest
 * last.
 */
export function expiryChoices(maxLifetimeDays: number | undefined): ExpiryChoice[] {
    if (maxLifetimeDays === undefined) return [...USUAL_LIVES, { days: null, label: $localize`The longest allowed` }];
    if (maxLifetimeDays <= 0) return [...USUAL_LIVES, { days: null, label: $localize`Never expires` }];
    const shorter = USUAL_LIVES.filter((choice) => choice.days! < maxLifetimeDays);
    const label = icu($localize`{days, plural, one {# day} other {# days}} (the longest allowed)`, {
        days: maxLifetimeDays,
    });
    return [...shorter, { days: null, label }];
}

/** The expiry sent for a life of `days` from `now`; null lets the server give the longest. */
export function expiryOf(days: number | null, now = new Date()): string | null {
    return days === null ? null : new Date(now.getTime() + days * 86_400_000).toISOString();
}

/**
 * The rights a token may be limited to: the session's own (`own`), by area of the catalog when it could be read (it
 * needs a right to manage accounts), else by id.
 */
export function rightAreas(own: readonly string[], catalog: readonly RightDefinition[] | null): RightArea[] {
    if (!catalog) return [{ title: $localize`Rights`, rights: own.map((id) => ({ id, title: id, description: '' })) }];
    const held = new Set(own);
    const areas = new Map<string, RightArea>();
    for (const right of catalog.filter((definition) => held.has(definition.id))) {
        const area = areas.get(right.area) ?? { title: right.area, rights: [] };
        area.rights.push({ id: right.id, title: right.title, description: right.description });
        areas.set(right.area, area);
    }
    return [...areas.values()];
}
