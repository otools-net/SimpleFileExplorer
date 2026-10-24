# Video player (viewer and player page)

Code: `src/app/player/` (`VideoPlayerComponent`, `PlayerPageComponent` `player?path=&t=`), `src/app/library/viewer/`.
Streams: [video-streams.md](video-streams.md).

- `VideoPlayerComponent` is shared by the viewer and the player page; the seek icons get the step
  (`config.seekSeconds`) overlaid; the viewer gives each item its own elements (`@for` over a one-item list tracked by
  path).
- Player size: `VideoPlayerComponent.minWidth` (the controls that stay: play, mute, fullscreen, the "⋯", the
  position) → host `--player-min-width`; the video's `min-width: min(var(--player-min-width), 100cqw)` against
  `.viewer-stage` (`container-type: size`): a `100%` there resolved against the player, which the video itself sizes,
  and capped it at ~320 px.
- `.player-row` is an overflow row (`sfeOverflowRow`, [overflow-menus](overflow-menus.md)): one
  line; the time is two items, the position (stays) and `.player-time-length` (priority -1, no menu entry: leaves
  first, alone, without showing the "⋯"); then Cinema, Save frame, Loop, Convert, Speed, Forward, Back leave from the
  right into the "⋯" (`more` entries: Loop and Cinema `checked`, Speed a submenu). Play, Mute, Fullscreen stay. The
  buttons after the time are pushed right by `margin-right: auto` on the last part of the time shown (margins aren't
  measured; a `flex: 1` spacer would be). The right-click menu (`menuEntries()`) is unchanged.
- Heights in the viewer use `100cqh` (the stage), not `100dvh - 90px`: the title bar grows with tags, and a full-height
  video (cinema mode) then overflowed the dialog, which showed a scrollbar.
- Cinema mode (`cinemaButton`, viewer only, `sfe.player.cinema`) = host class `cinema`,
  `.viewer-stage:has(> .player.cinema)`.
- The player page stays dark ([theme.md](theme.md)) and opts out of the scrollbar gutter.
- Tests stub `HTMLMediaElement` ([testing-client.md](../development/testing-client.md)).
