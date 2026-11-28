# Wizards

Code: `src/Sfe.Web/src/app/wizards/` (`wizard-definition.ts`, `wizard-registry.ts`, `wizard.component.*`,
`wizard-context.ts`, `wizard-run.ts`, `wizard-kept.ts`, `wizard-page.component.ts`, `leave-wizard.ts`, one folder per wizard:
`new-space/`, `share-folder/`), `styles/wizards.css`, `testing/wizard-harness.ts`; server: `validateOnly` on the route a wizard saves
with (`TenantEndpoints.Create`, `FolderRuleEndpoints.Set`). User docs: `docs/administration/wizards.md`.

## How it is put together

- **Definition** (`WizardDefinition<D>`, made with `defineWizard`): id, title, help page, `initialDraft()`, steps,
  `finish(draft)`. A step: id, title, component, explanation, `helpAnchor`, `fields` (the request's field names it
  fills), `valid`, optional `skip` and `check`, `review` lines. Who may open it is **not** in the definition: it is in
  the registry line only (right, `onlyIn`, `home`), so it is written once.
- **Registry** (`WIZARDS`): one line per wizard, read by the page, the administration search and the
  [getting-started checklist](getting-started.md) (an item names its wizard by id).
  `load: () => import('./<id>/<id>.wizard')`: a wizard's code is a lazy chunk; nothing eager imports a wizard folder
  (`wizard-registry.spec.ts` checks the registry, the routes and the search).
- **Page**: one route `wizard?id=<id>` (one level deep like every route: [client-routes](../architecture/client-routes.md)),
  guard `leaveWizard`. It refuses an unknown id or a session without the right / in the other mode, sets the title,
  loads the definition. Cancel, Done and the arrow go to `admin?section=&page=` of the line's `home`.
- **Shell** (`sfe-wizard`): Step n of m (skipped steps left out; the review counts), the steps' list (hidden on phones),
  Next (submit: Enter in a field), Back, Cancel, the review with Change per step, Finish, the end (message, optional
  `details`: lines under a heading, links, Run again, Done), focus on the new title, `beforeunload` and `canLeave()` while the draft differs from the initial one.
- **Step context** (`WIZARD_CONTEXT`, `injectWizard<D>()`): the draft signal, `change(partial)`, `errors`, `busy`.
- **Checks and refusals**: a step's `check` and the definition's `finish` run in an injection context
  (`runInInjectionContext`): `inject()` before the first `await`. A check's refusal (`ApiError.errors`) keeps the
  step when it names a field of this step or an earlier one; a refusal of a later step's field lets it go on (the
  server checks everything at once and stops at the first error, in field order). A refused Finish goes to the first
  step whose `fields` holds a refused field; a refusal of no step's field shows above the buttons.
- **Drafts are plain JSON values** (no `Set`, no `Date`): `dirty` compares `JSON.stringify`, and a redirect keeps one
  in `sessionStorage`.
- **Leaving for another site** (a provider's sign-in): `finish` answers `{ redirect: address }` (only with `resume`).
  The shell keeps `resume.keep(draft)` (secrets left out) in `sessionStorage` (`sfe.wizard.<id>`, wizard-kept.ts),
  stops the leave guard and calls `leaveFor(address)` (`location.assign`; specs spy on it). The other site sends the
  browser back to `wizard?id=<id>&…`: the page hands the query besides `id` to the shell (`returned`), which takes
  the kept draft (once; left when older than 15 minutes), shows the review and runs `resume.run(draft, query)`: the
  end, or its refusal above Finish (Finish starts again). A kept draft without a query, or a query without one,
  starts the wizard afresh.
- `wizard-run.ts`: `runRequest` (busy, field errors, the problem above the buttons, 429 in words) is the setup page's
  former `run`; the setup page and the shell share it and `.wizard-progress` / `.wizard-actions` (`wizards.css`).
  The setup page itself stays outside the framework (pre-sign-in, saves at step 2, restart step).
- Help: `helpLink` comes from `help/help-files.ts` (never `help-links.ts`, which brings markdown-it).
- Server side, a step's `check` is the save route run with `validateOnly=true`: same checks, 204, nothing made.
  `POST tenants?validateOnly=true` runs the folder's checks (`RootError`) then `AccessService.CheckNewTenantAsync`
  (`NewTenants.CheckAsync`: name, short name, rights switched off).
  `PUT access/folder-rules?validateOnly=true` runs `FolderRuleService.CheckAsync` (the checks of `SetAsync`, in its
  order: path, level, principal (`userId` when none is named), the 250-rule cap).

## The wizards

- `new-space` (5.3a): folder, name, visitors; `POST tenants`. Entry: **Step by step** on the Spaces page.
- `share-folder` (5.3b): folder (`access/folders`, every folder; the root can't be picked: the picker emits no empty
  path, so a rule on the root stays on the page), who (groups then accounts, radio buttons, `g12` / `u5` as in the
  card), level (`FOLDER_LEVELS` from `access/folder-levels.ts`; "Today …" from `access/folder-access`); one
  `PUT access/folder-rules`, then the end's `details` from `access/folder-access` (accounts and visitors; left out if
  that read fails, the rule being set). Session's space only: **Step by step** shows on the Folder access card only
  without `source` (connections) and `space` (another space).

## Adding a wizard

1. A folder `wizards/<id>/`: the draft type (plain values) and its request mapping, one component per step
   (`injectWizard<Draft>()`), `<id>.wizard.ts` exporting `defineWizard<Draft>({...})`.
2. One line in `WIZARDS` (title, description, search words, right, `onlyIn` if any, `home`, `load`).
3. Its section in `docs/administration/wizards.md`, with a heading per step (the `helpAnchor`s) and a screenshot.
4. Its save: one route; when it writes several things, one route doing them in one transaction (both DB providers),
   with its right, `validateOnly` for the steps' checks, the OpenAPI baseline and Sfe.Client regenerated.
5. Specs of its own steps and `finish` (with `WizardHarness`), the route's tests. Nothing else: the shell, the
   registry spec, the search and the e2e spec cover every wizard already.

A need two wizards share goes into the framework (or `wizards/steps/`), in a commit of its own.

## Gotchas

- `WizardHarness.next()` settles twice: a check's promise, then the new step.
- A step component is created again on each visit (`NgComponentOutlet`): its own state (the folder picker's listing)
  starts over; what must stay goes in the draft.
- The page spec needs several `settle()` + `detectChanges()` turns: the wizard's chunk loads after the page.
