/** A heading of a help page (levels 2 and 3), as plain text. */
export interface HelpHeading {
    level: number;
    text: string;
}

/** A page of the help: its file under the help's root (`user-guide/browsing.md`), its title and headings. */
export interface HelpPage {
    path: string;
    title: string;
    headings: HelpHeading[];
}

/** `GET api/help`: every page of the help, `README.md` first. */
export interface HelpIndex {
    pages: HelpPage[];
}
