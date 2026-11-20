/* The few Node.js functions specs use to read the client's sources (the tests run in Node; @types/node isn't installed). */
declare module 'node:fs' {
    export function readFileSync(path: string, encoding: 'utf8'): string;
    export function writeFileSync(path: string, data: string): void;
    export function readdirSync(path: string, options: { recursive: true }): string[];
    export function readdirSync(path: string): string[];
}
declare const process: { cwd(): string; env: Record<string, string | undefined> };
