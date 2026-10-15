# Background tasks, history and run logs

Code: `src/Sfe.Core/Tasks/` (`BackgroundTasks`, `TaskHistory`, `TaskRunLog`, `TaskLogProvider`), entities in
`Data/Entities/Tasks/`; client `src/app/tasks/` (`TasksSection`: Tasks/Schedules/History, `TaskHistory`, `tasks.ts`
wording). Schedules: [schedules.md](schedules.md).

- `BackgroundTasks` describes/starts/cancels scan, trickplay pass, similar videos and thumbnails (thumbnails: cancel
  only, `ThumbnailService.CancelAll`).
- Each run of the first three is a `TaskRuns` row written at start (`TaskHistory.Begin`) and completed at the end with
  outcome, summary and log JSON; rows still `Running` at start-up become `Interrupted` (`RecoverAsync`); purge after
  `Tasks:LogRetentionDays`.
- Logs: `TaskLogProvider` (an `ILoggerProvider`, filter `Sfe` ≥ Debug) copies entries of the async flow whose
  `AsyncLocal` `Begin` set — call `Begin` from the run's own method (an awaited async method restores the caller's
  value on return); tasks/threads it starts inherit it. Long logs keep the first and last 1000 lines.
- A new job: give it a trigger + `Begin`/`CompleteAsync` and a case in `BackgroundTasks`.
- Bodyless start routes take inputs from the query (`POST api/tasks/{task}/start?path=`,
  [api-routes.md](../development/api-routes.md)).
