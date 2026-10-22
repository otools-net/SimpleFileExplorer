# Keep alive

**Administration › Keep alive**: for a space whose library folder is on a network share. A file server drops the
connection to a share nobody used for a while (Windows file servers after 15 minutes, many NAS sooner), and the drive
or mount may be lost with it. Keep alive makes SFE look at the library folder every few minutes, a single read of the
folder's first entry however large it is, so the connection never goes idle. Right: `settings.keepAlive`
(Administrators).

Keep alive keeps a connection from being dropped; it can't reconnect a drive that is already lost: reconnect it as
usual (a mapped drive, as the account SFE runs as).

## Status

![Administration › Keep alive › Status](../images/admin-keepAlive-status.png)

- The line at the top says whether keep alive is on, how often it pings, or how many folders don't answer.
- Each folder pinged since the server started: whether it is **on a network share**, when it was last pinged and how
  long the ping took, or why it failed and **since when**. **Waiting for an answer**: the share doesn't answer at all;
  it is not pinged again until it does (or the ping's time limit is over, then it counts as failed).
- **Ping now** pings the library folder at once, also while keep alive is off: to try a share before turning it on,
  or to check it answers again.
- **Settings** opens the settings below.

The [log](logs.md) also says when keep alive starts on a folder, when a folder stops answering (once, not at every
ping) and when it answers again.

## Settings

![Administration › Keep alive › Settings](../images/admin-keepAlive-settings.png)

Settings of each space (and their default for every space at the **Whole server** level).

| Setting | Default | |
|---|---|---|
| **Keep the library folder alive** (`Sfe:KeepAlive:Mode`) | Off | **When it is on a network share** (`Auto`): a `\\server\share` folder, a mapped network drive, or a CIFS, NFS or SSHFS mount, also through a link. **Always**: also a folder SFE can't tell is on the network |
| **Seconds between pings** (`Sfe:KeepAlive:IntervalSeconds`) | 240 | 10–3600; shorter than the time the file server waits before dropping an idle connection |
| **Time limit (seconds)** (`Sfe:KeepAlive:TimeoutSeconds`) | 30 | 1–300; a ping taking longer counts as failed |
