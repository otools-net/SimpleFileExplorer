/* Reading form controls from their events in templates: `(input)="name.set(valueOf($event))"`. */

export function valueOf(event: Event): string {
    return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value;
}

export function checkedOf(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
}
