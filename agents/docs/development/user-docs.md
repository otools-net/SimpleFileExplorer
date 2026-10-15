# End-user documentation (docs/)

- User-visible changes (behaviour, settings, UI) go in `docs/`, kept user-oriented: the page of that screen
  (`docs/user-guide/` for the listing page and its dialogs, `docs/administration/` one page per admin section,
  `docs/reference/` for rights, settings levels, search syntax), plus `docs/configuration.md` for a configuration key
  and `docs/upgrading.md` for what an upgrade changes.
- Every web-UI function is documented, with a screenshot in `docs/images/` where it shows something (regenerate:
  [docs-screenshots.md](../environment/docs-screenshots.md)).
- `README.md` stays short: overview, quick start, links into `docs/`; add to it only what every user needs.
- Implementation facts go in `agents/docs/`, not in `docs/`.
- Routes are documented by OpenAPI, not in the docs: give every route `.WithName`, `.WithSummary`, `.WithTags` (and
  `.WithDescription` for details).
