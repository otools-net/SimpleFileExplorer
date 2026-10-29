// Where a run keeps its things, shared by the config, the server script and the setup: nothing goes into the repo.
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const here = path.dirname(fileURLToPath(import.meta.url));
export const repo = path.resolve(here, '../..');

/** Build, library copy, database, caches, sign-in states, reports: `SFE_E2E_WORK`, else `<tmp>/sfe-e2e`. */
export const work = path.resolve(process.env.SFE_E2E_WORK ?? path.join(os.tmpdir(), 'sfe-e2e'));
export const port = Number(process.env.SFE_E2E_PORT ?? 5181);
export const baseURL = `http://localhost:${port}/`;
export const setupCode = 'e2e-setup-code-1';

export const auth = (account: string) => path.join(work, 'auth', `${account}.json`);
/** The copy of tests/samples/library the app serves (scripts/server.mjs). */
export const library = path.join(work, 'library');

/** The accounts the setup makes (`setup/global-setup.ts`); specs pick one with `test.use({ account })`. */
export const accounts = {
    admin: { userName: 'admin', password: 'admin-password-1' },
    sam: { userName: 'sam', password: 'sam-password-1' },
} as const;
export type Account = keyof typeof accounts;
