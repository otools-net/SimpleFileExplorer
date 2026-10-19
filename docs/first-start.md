# First start

SFE has accounts, and nothing works until its first administrator exists: until then every page shows the setup
page and the API answers nothing else. The setup is done in the browser, with no file to edit.

## The setup code

So that only whoever runs the server can claim it, the setup page asks for a **setup code**, except on the server's
own computer (below). Without one in its configuration, SFE makes one itself at its first start (16 letters and
digits, like `K7QF-9WZM-2HXA-PL4D`):

- it is shown in the window where SFE runs (for a service: its console output, see below), with the address to open;
  it never goes to the server's [log](administration/logs.md);
- it is kept in `data/setup-code.txt`, in SFE's folder (`~/.config/sfe/data/` for the AppImage).

Type it in any case, with or without the dashes. Keep the file: the [update page](upgrading.md#the-update-page-137-and-later)
asks for the same code after each upgrade.

| How SFE runs | Where the code is shown |
|---|---|
| Portable package / AppImage / from source | The console window of SFE |
| Windows service | The file (a service has no window; the event log says where the file is) |
| systemd | `journalctl -u sfe` |
| IIS | The site's stdout log, or the file in the site's folder |
| Container | `docker logs <container>` (keep `data/` on a volume, or set the code yourself) |

### On the server's own computer: no code

A new install (no account yet) opened in a browser **on the computer SFE runs on** (`http://localhost:5080`) skips the
code: whoever sits there runs the server anyway. That is the case of the portable package on Windows, which opens the
setup page in your browser by itself. From a phone or another computer, through a reverse proxy, or once any account
exists, the code is asked as above. To ask for it on the server's computer too, set `Sfe:Setup:LocalWithoutCode` to
`false`.

### Setting the code yourself

A code set in the configuration (at least 12 characters) replaces the generated one, e.g. for a container whose data
folder isn't kept, or to use a code you choose:

| How SFE runs | Where the code goes |
|---|---|
| Windows service | `"Setup": { "Code": "…" }` in the `Sfe` section of `appsettings.json` next to the program, or the system environment variable `Sfe__Setup__Code`; restart the service |
| systemd | `Environment=Sfe__Setup__Code=…` in the `[Service]` section (`systemctl edit sfe`), then `systemctl restart sfe` |
| IIS | `<environmentVariable name="Sfe__Setup__Code" value="…" />` in the site's `web.config`, then recycle the app pool |
| Container | the environment variable `Sfe__Setup__Code` (`docker run -e`, a compose file, your NAS's container settings) |
| Portable package / AppImage | `"Setup": { "Code": "…" }` in the `Sfe` section of its `appsettings.json` (`~/.config/sfe` for the AppImage) |
| From source | `Sfe__Setup__Code=…` in the environment, or `appsettings.Development.json` |

When SFE can't write its data folder and no code is set, the page shows these instructions for the way your server
runs.

## The setup page

![The setup page](images/setup-page.png)

Open the app (`http://localhost:5080`; the portable package on Windows opens it in your browser by itself). The page
has three steps (two on the server's own computer, which skips the code):

1. **Setup code**: the code above.
2. **Your library** (the [installation settings](administration/system.md#installation)):
   - **How SFE is used**: **Local** (SFE on your own computer, only for its administrators) or **External** (other
     people open it from their devices). Local is preselected on the server's own computer, and can only be chosen
     there; External elsewhere. Later, a Local server can still become External, but not the other way round.
   - **Library folder**: click through the server's drives and folders to the folder of your photos, videos and music
     (**Home**, **Pictures**, **Videos** and **Music** are listed first). It becomes the first space's library.
   - **Who can open SFE** (External only): **Only this computer**, or **Also the other devices of my network** (phones,
     TVs, other computers; everyone still signs in), and the port. This one applies once SFE is started again.
   - **ffmpeg** (optional): whether it was found; to add it, download it from [ffmpeg.org](https://ffmpeg.org/), unzip
     it and pick its folder. Without it there are no video thumbnails, trickplay, video durations or converted videos.
3. **Administrator**: name, display name, password, and the name of the first **space** (your library, its index, tags
   and settings; more spaces come later in [Spaces & accounts](administration/spaces-and-accounts.md)). For the
   portable package and the AppImage, **Sign me in when SFE starts on this computer** turns on
   [fast sign-in](#fast-sign-in).

The administrator is signed in at once and is a **server administrator**: every right in every space. If the network
setting changed, the page asks you to start SFE again.

A value set by the server's environment (an environment variable or the command line, e.g. `Sfe__Root` in a systemd
unit) is shown but can't be changed on the page.

## Fast sign-in

For SFE used on one computer only, as an app: each time you start it, the browser it opens signs you in without your
password. Turn it on at the setup page's last step, or later in
[Administration › System › Start-up](administration/system.md#start-up).

- Only for the portable package and the AppImage started by hand (a service opens no browser), with the browser opening
  at start (not with `--no-browser`).
- The link the browser opens works once, for a minute, and only from that computer. Other browsers and devices still
  sign in with the password.
- Only while the server has a **single** server administrator: with a second one, nobody is signed in that way.
- Anyone who can start SFE on that computer gets in as the administrator: leave it off on a shared computer, or when
  other devices of your network use SFE.

## Next steps

1. **Scan the library**: ⋮ menu › **Scan this folder** at the top of the library, so that search, tags and duplicates
   know its files ([Scanning](user-guide/scanning.md)). Scans can also run on a
   [schedule](administration/background-tasks.md#schedules).
2. **Create accounts** for the others, or let visitors in without one:
   [Spaces & accounts](administration/spaces-and-accounts.md).
3. Look through the [administration page](administration/README.md) for the settings of your library.
4. To start SFE with the computer on Windows, run `install-service.cmd` in its folder
   ([Windows service](installation.md#windows-service)).

## Locked out, or lost administrator password

Wrong passwords block only the address they come from, so someone guessing yours doesn't keep you out. If the account
itself got locked (guesses from many addresses, 50 by default) or you forgot the password, recover it from the sign-in
page, without restarting anything:

1. **Recover the account** under the sign-in form; enter the administrator's name and **Show a recovery code on the
   server**.
2. The code appears where SFE runs, as the [setup code](#the-setup-code) does: its console window or the service's
   output, and the file `recovery-code.txt` beside `setup-code.txt` in SFE's data folder (for a Windows service).
   Only whoever runs the server can read it, which is what it proves.
3. Type the code and a new password: the account is unlocked, signed in, and its other sessions end (someone who knew
   the old password is out).

A code works once, for 15 minutes; asking again shows the same one. Only enabled local server administrators can be
recovered this way: other accounts are unlocked by an administrator (Administration › Accounts). The server's log
records each request with the address it came from, never the code.

For a disabled administrator, or a server whose last administrator is gone, set `Sfe__Setup__Recover=true` and restart: the sign-in page links to the setup page, which asks for the setup code
(the configured one, or the one in `data/setup-code.txt`), then gives a local administrator (by name) a new password,
unlocked and enabled, or creates one. The app keeps working meanwhile. Turn it off afterwards.
