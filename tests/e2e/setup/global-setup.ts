// Once per run, on the fresh app started by scripts/server.mjs: the administrator (setup), a limited account, a
// scan of the library, then each account's signed-in browser state (cookies) for the specs (`storageState`).
import { request, type APIRequestContext } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { accounts, auth, baseURL, setupCode } from '../paths';

const headers = { 'X-SFE-Request': '1' };

async function call<T = unknown>(api: APIRequestContext, method: string, url: string, data?: unknown): Promise<T> {
    const response = await api.fetch(url, { method, data });
    const text = await response.text();
    if (!response.ok())
        throw new Error(`${method} ${url}: ${response.status()} ${text}`);
    return (text ? JSON.parse(text) : null) as T;
}

export default async function globalSetup(): Promise<void> {
    fs.mkdirSync(path.dirname(auth('admin')), { recursive: true });

    const admin = await request.newContext({ baseURL, extraHTTPHeaders: headers });
    await call(admin, 'POST', 'api/setup', {
        code: setupCode, userName: accounts.admin.userName, password: accounts.admin.password,
        displayName: 'Alex (admin)', libraryName: 'Home media',
    });
    await call(admin, 'POST', 'api/scan', { path: '' });
    for (let i = 0; ; i++) {
        const scan = await call<{ isRunning?: boolean; running?: boolean }>(admin, 'GET', 'api/scan');
        if (!scan.isRunning && !scan.running)
            break;
        if (i > 200)
            throw new Error('the scan of the sample library did not end in a minute');
        await new Promise(resolve => setTimeout(resolve, 300));
    }

    const groups = await call<{ groups?: { id: number; name: string }[] } | { id: number; name: string }[]>(admin, 'GET', 'api/access/groups');
    const users = (Array.isArray(groups) ? groups : groups.groups ?? []).find(group => /users/i.test(group.name));
    if (!users)
        throw new Error('no Users group');
    await call(admin, 'POST', 'api/access/users', {
        userName: accounts.sam.userName, displayName: 'Sam', password: accounts.sam.password,
        kind: 'user', isServerAdmin: false, disabled: false, groupIds: [users.id],
    });
    await admin.storageState({ path: auth('admin') });
    await admin.dispose();

    const sam = await request.newContext({ baseURL, extraHTTPHeaders: headers });
    await call(sam, 'POST', 'api/session/login', { userName: accounts.sam.userName, password: accounts.sam.password, remember: true });
    await sam.storageState({ path: auth('sam') });
    await sam.dispose();
}
