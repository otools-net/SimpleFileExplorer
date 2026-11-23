/**
 * An editing session id, as the server wants it (letters, digits): the previous version of a file saved over goes to
 * the trash at the first save of a session. crypto.randomUUID needs a secure context.
 */
export function editingSession(): string {
    const bytes = crypto.getRandomValues(new Uint8Array(16));
    return Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
}
