# Wizards

Code: `src/Sfe.Web/src/app/wizards/` (`wizard-definition.ts`, `wizard-registry.ts`, `wizard.component.*`,
`wizard-context.ts`, `wizard-run.ts`, `wizard-page.component.ts`, `leave-wizard.ts`, one folder per wizard:
`new-space/`), `styles/wizards.css`, `testing/wizard-harness.ts`; server: `validateOnly` on the route a wizard saves
with (`TenantEndpoints.Create`). User docs: `docs/administration/wizards.md`.

## How it is put together

- **Definition** (`WizardDefinition<D>`, made with `defineWizard`): id, title, help page, `initialDraft()`, steps,
  `finish(draft)`. A step: id, title, component, explanation, `helpAnchor`, `fields` (the request's field names it
  fills), `valid`, optional `skip` and `check`, `review` lines. Who may open it is **not** in the definition: it is in
  the registry line only (right, `onlyIn`, `home`), so it is written once.
- **Registry** (`WIZARDS`): one line per wizard, read by the page, the administration search and (5.2) the
  checklist. `load: () => import('./<id>/<id>.wizard')`: a wizard's code is a lazy chunk; nothing eager imports a
  wizard folder (`wizard-registry.spec.ts` checks the registry, the routes and the search).
- **Page**: one route `wizard?id=<id>` (one level deep like every route: [client-routes](../architecture/client-routes.md)),
  guard `leaveWizard`. It refuses an unknown id or a session without the right / in the other mode, sets the title,
  loads the definition. Cancel, Done and the arrow go to `admin?section=&page=` of the line's `home`.
- **Shell** (`sfe-wizard`): Step n of m (skipped steps left out; the review counts), the steps' list (hidden on phones),
  Next (submit: Enter in a field), Back, Cancel, the review with Change per step, Finish, the end (message, links, Run
  again, Done), focus on the new title, `beforeunload` and `canLeave()` while the draft differs from the initial one.
- **Step context** (`WIZARD_CONTEXT`, `injectWizard<D>()`): the draft signal, `change(partial)`, `errors`, `busy`.
- **Checks and refusals**: a step's `check` and the definition's `finish` run in an injection context
  (`runInInjectionContext`): `inject()` before the first `await`. A check's refusal (`ApiError.errors`) keeps the
  step when it names a field of this step or an earlier one; a refusal of a later step's field lets it go on (the
  server checks everything at once and stops at the first error, in field order). A refused Finish goes to the first
  step whose `fields` holds a refused field; a refusal of no step's field shows above the buttons.
- **Drafts are plain JSON values** (no `Set`, no `Date`): `dirty` compares `JSON.stringify`, and 5.3c keeps one in
  `sessionStorage` across a sign-in's redirect.
- `wizard-run.ts`: `runRequest` (busy, field errors, the problem above the buttons, 429 in words) is the setup page's
  former `run`; the setup page and the shell share it and `.wizard-progress` / `.wizard-actions` (`wizards.css`).
  The setup page itself stays outside the framework (pre-sign-in, saves at step 2, restart step).
- Help: `helpLink` comes from `help/help-files.ts` (never `help-links.ts`, which brings markdown-it).
- Server side, a step's `check` is the save route run with `validateOnly=true`: same checks, 204, nothing made.
  `POST tenants?validateOnly=true` runs the folder's checks (`RootError`) then `AccessService.CheckNewTenantAsync`
  (`NewTenants.CheckAsync`: name, short name, rights switched off).

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
