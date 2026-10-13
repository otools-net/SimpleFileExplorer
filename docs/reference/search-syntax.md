# Search syntax

The terms of the [search box](../user-guide/search.md). Search is case-insensitive and covers the current folder and its
sub-folders; it only finds indexed files.

| Query | Meaning |
|---|---|
| `cat dog` | name contains both |
| `cat, dog` | either one |
| `cat -kitten` | exclude a word (per alternative) |
| `s:>1gb`, `s:<=10mb`, `s:=0b`, `s:700mb` | size larger / smaller / exact / about (units b, kb, mb, gb, tb; mb by default) |
| `e:mp4` | extension |
| `type:image`, `type:video`, `type:audio`, `type:other` | photos / videos / audio files / every other file, by extension ([Types of file](#types-of-file)) |
| `w:>=1920`, `h:<720`, `r:>720x1024`, `r:1920x1080` | width / height / both sides of a photo or video, in pixels (`<`, `<=`, `>`, `>=`, `=`; none = exactly) |
| `r:hr`, `r:lr` | photos and videos of high / low resolution, as the HR/LR marks say |
| `d:>10m`, `d:<=30s`, `d:1h`, `d:>1:30:00` | videos and audio files longer / shorter / about that long (units s, m, h; m by default; or a clock `m:ss`, `h:mm:ss`) |
| `*holiday 2020` | match the whole path instead of the file name |
| `t:beach`, `tag:"science fiction"`, `t:science_fiction` | tagged with it, or with a tag inheriting from it (the search box suggests tags) |
| `special:untagged` | files without tags |
| `-t:work`, `-e:jpg`, `-s:>1gb`, `-r:1920x1080`, `-d:>1h`, `-type:video`, `-special:untagged` | a `-` before any term finds the files that term doesn't: not tagged with it, any other extension, not larger than 1 GB, not exactly 1920×1080, not longer than an hour, not a video, with at least one tag |
| `"a, b"` | double quotes keep spaces and commas in a term |

## Names and paths

- A file's name is searched without its extension: use `e:` for the extension (`clip.mp4` as a name finds nothing; the
  [explanation](../user-guide/search.md#explanations) points that out).
- `*` only counts at the start of the query, and then applies to every alternative.

## Types of file

`type:` finds files by kind: `image` (photos), `video`, `audio`, or `other` (everything that is none of the three). The
kind comes from the extension, by the same lists as the listing's icons and previews
([Scanning › File types](../administration/scanning.md#file-types)): change a list and searches follow at once, without a scan.
Several `type:` terms in one alternative are either-or, like `e:`: `type:image type:video` finds photos and videos.
`-type:video` finds everything but videos; `-type:image -type:video` neither photos nor videos. An unknown kind
(`type:folder`) is ignored, as the [explanation](../user-guide/search.md#explanations) points out.

## Resolutions

`w:` compares the width, `h:` the height, `r:` both at once, so `r:>720x1024` finds pictures wider than 720 **and**
taller than 1024 pixels. They are the sides of the picture as shown (a portrait phone photo or video counts as
portrait). Only photos and videos whose resolution a scan has read are found.

An excluded resolution finds everything else, files whose resolution is not known included: `-w:1920` lists documents
too; `-w:1920 w:>0` keeps only the photos and videos of another width. `-r:1920x1080` leaves out the files that are 1920
wide **and** 1080 high, not every file 1920 wide.

`r:hr` finds the photos and videos whose shorter side reaches the high-resolution threshold of its kind (1500 pixels for
photos, 720 for videos by default; [Interface › Browsing](../administration/interface.md#browsing)), `r:lr` the ones
whose resolution is known and below it. They are not opposites: files of unknown resolution are neither.

## Durations

`d:` compares how long a video or audio file lasts. A number is in minutes unless it ends with `s` (seconds) or `h`
(hours): `d:>90` and `d:>1.5h` are the same. A clock reads like a player's: `d:1:30` is a minute and a half,
`d:<1:30:00` under an hour and a half.

Without an operator, the duration is "about": `d:10` finds 9½ to 10½ minutes, `d:1h` half an hour to an hour and a
half, `d:45s` 44½ to 45½ seconds; `d:=` is the exact second.

Only videos and audio files whose duration a scan has read are found; `-d:>1h` also lists everything else (photos,
documents), `-d:>1h d:>0` only the shorter videos and songs; add `e:mp3` (or another extension) to keep one kind.

As for `e:`, `s:` and the others, a full-path search for a drive (`*D:\Movies`) takes `D:` as a duration: search
`*\Movies` instead.

## Tags

`t:` matches a tag's name, shorthand or aliases, and includes the tags inheriting from it. Double quotes in a tag name
are ignored: `t:"12 Vinyl"` finds the tag `12" Vinyl`. Files with a hidden tag (like **Archived**) only show up when the
search names that tag or a parent of it.

## Languages

On SQLite, case-insensitive matching covers ASCII letters only: `русский` or `ESPAÑOL` don't find `Русский` or
`Español`. SQL Server's case-insensitive collation does.
