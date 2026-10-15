# Task schedules

Code: `src/Sfe.Core/Tasks/` (`TaskScheduleService`, `ScheduleCalculator`), `src/Sfe.Api/TaskScheduleWorker.cs`;
client `src/app/tasks/` (`ScheduleForm`).

- `TaskSchedules` (UTC `Start`, `Kind` Once/Interval/Weekly, `Days` bit 0 = Sunday; "every day" = weekly with all
  days).
- `ScheduleCalculator.Next` is strictly after a moment; weekly uses the server's time zone (skipped DST times run
  later, repeated ones the first time).
- The loop (`TaskScheduleService.RunAsync`, hosted by `TaskScheduleWorker`) starts what came due since its last check,
  never before `Updated` (no runs for the past), sleeps until the next occurrence (≤ 30 min) or a schedule change;
  missed runs aren't caught up; due while running → a `Skipped` run (a trickplay pass is queued instead).
- Trickplay's own `RecheckHours` pass stays; its time is `TrickplayStatus.NextPass` (`automaticNextRun`).
- The client's schedule form previews with `POST schedules/preview` (same calculation) rather than computing times
  itself.
