import type { InstallationView, ServerMode } from '../core/access.models';

export type HostingKind = 'windowsService' | 'systemd' | 'iis' | 'docker' | 'appImage' | 'portable' | 'other';

/** /api/setup: whether the server still needs its first administrator, and whether it has a setup code. */
export interface SetupInfo {
    required: boolean;
    available: boolean;
    codeConfigured: boolean;
    codeTooShort: boolean;
    recover: boolean;
    minimumCodeLength: number;
    minimumPasswordLength: number;
    libraryName: string;
    hosting: HostingKind;
    /** The code is the one the server made itself: shown in its console and kept in `codeFile`. */
    codeGenerated?: boolean;
    /** Where that code is kept, as configured (usually relative to SFE's folder). */
    codeFile?: string | null;
    /**
     * The setup asks this visitor for the code; false for a new install set up from the server's own computer (missing
     * from older servers: true).
     */
    codeRequired?: boolean;
}

/** What the installation page changes; a field left out stays. `ffmpegPath` empty = look for it on PATH. */
export interface InstallationInput {
    root?: string | null;
    network?: boolean | null;
    port?: number | null;
    ffmpegPath?: string | null;
    mode?: ServerMode | null;
}

export interface InstallationSaved {
    settings: InstallationView;
    restartRequired: boolean;
}
