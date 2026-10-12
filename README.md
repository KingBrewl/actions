# Streamer.bot actions

The stream's chat commands and rewards, version controlled. Each file is a self-contained
Streamer.bot C# action.

Some of them can also put a card on the old alerts overlay (the `overlays` repo) with
`CPH.WebsocketBroadcastJson`. With no overlay listening that broadcast just goes nowhere, so
the chat side works the same either way.

## Importing an action

1. In Streamer.bot: **Actions → right-click → Add Action**, name it after the file.
2. Add a **C# Method** sub-action and paste the file's full contents.
3. Attach the trigger from the table below (right-click the action → Test Trigger to fire it).

## Actions

| File | Trigger | Notes |
|---|---|---|
| `rewards/checkin.cs` | Channel Point Reward → *Check-in*, **and** Twitch → Stream Online | Chat only — arrival number and visit count; the on-screen clip is Tangia's — see Check-in below |
| `rewards/first.cs` | Channel Point Reward → *First* | Chat only — an in-character greeting for the first redeemer; the reward must be created in Streamer.bot — see First below |
| `commands/shoutout.cs` | Command → `!so` / `!shoutout` (mods, VIPs), or Twitch → Raid | Streamer card: avatar, last category, bio — see Shoutouts below |
| `commands/quote.cs` | Command → `!quote` | Chat only; quotes persist in SB globals |
| `commands/clip.cs` | Command → `!clip` | Cuts a Twitch clip, posts the link, cards it on screen — see Clips below |
| `commands/death.cs` | Command → `!death` / `!deaths` | The challenge death counter: chat reply plus an OBS text source — see Death counter below |
| `commands/bad.cs` | Command → `!bad` | Chat calls the play: one random roast, aware of the current category and the death count — see Roasts below |
| `commands/hug.cs` | Command → `!hug` | Chat only; the target must be a real Twitch user — see Hugs below |
| `commands/lurk.cs` | Command → `!lurk` / `!unlurk` | Chat only; `!unlurk` says how long they were gone — see Lurking below |
| `commands/game.cs` | Command → `!game` | Chat only; reads the current category live from Twitch |
| `commands/commands.cs` | Command → `!commands` / `!cmds` / `!cmd` | Chat only; lists the commands the asker can use (VIPs and mods see theirs too). The list is written by hand — **add new commands to it** |
| `commands/links.cs` | Command → `!discord`, `!social` / `!socials`, `!youtube` / `!yt` — all in one trigger | Chat only; every link is written once at the top of the file and each message is built from it, so a changed invite or handle is a one-line edit |
| `commands/letsgo.cs` | Command → `!letsgo` / `!lg` | Chat counts every "let's go" — this stream and all time — see Let's go below |
| `commands/raid.cs` | Command → `!raid` | Chat only; posts the raid call twice for chat to copy — once with channel emotes for subs (they only work for subs in another channel's chat), once with emoji for everyone. Set a cooldown on the trigger if it gets spammed |

## Shoutouts

`commands/shoutout.cs` is the one action that reads *back* from Twitch rather than just
reporting what happened. It looks the channel up through Streamer.bot's own credentials — no
API key to configure — and broadcasts a `"variant": "shoutout"` alert, which the overlay
renders as a person card instead of an event strip: round avatar, display name, the category
they last streamed with its box art, their bio (three lines), and `twitch.tv/handle`.

The card holds for `DEFAULT_SHOUTOUT_MS` in the `overlays` repo's `src/alerts/commands.ts` (18s). The action sends
no `ms` of its own, so that constant is the only place to change it.

### Box art

Twitch serves category art from a predictable path, so no second API call and no key is
needed — the game's **id** off the channel record is the whole job. The action prefers it,
and falls back to the older name-keyed URL only when the record carries no id.

The id is what keeps that honest. A name Twitch doesn't recognise (compound names, anything
with a slash) does **not** 404 — it redirects to a grey `404_boxart` placeholder that loads
as a perfectly valid image, so nothing downstream can tell it from real art: `onError` never
fires, and the CDN sends no CORS header, so the pixels can't be read either. An id from the
channel record can't land there. If you are on the name fallback and see a grey box, that is
what it is.

Streamer.bot's inline compiler references only mscorlib, so `System.Net` is out of reach for
verifying the url here without asking you to add an assembly reference — and `System.Uri` with
it, which is why the percent-encoding in `Escape` is done by hand.

**Writing new actions:** add the command to the list in `commands/commands.cs` so `!commands`
shows it. mscorlib only. `Convert`, `Random`, `StringBuilder`, `Encoding` and
`System.Reflection` are all fine; anything from `System.dll` — `System.Net`, `System.Uri`,
`System.Linq` — will not compile.

Attach it twice if you want both halves:

- **Command → `!so`** with *Mods*, *VIPs* and *Broadcaster* ticked. It also checks the mod
  and VIP flags itself, so an accidentally-public command still won't let chat spam it.
- **Twitch → Raid**. The action notices the raid's `fromUser`
  argument, shouts the raider out without anyone typing, and skips the mod check.

Three toggles sit at the top of the file: `BioLimit` (200 characters over the wire),
`SendNativeShoutout` (also fires Twitch's own `/shoutout`, which puts the channel in
viewers' sidebars — Twitch rate limits it to one per 2 minutes and the action ignores a
rejection), and `PostChatMessage` (the "go show them some love" line in chat).

Twitch's SB wrappers have been renamed across versions, and Streamer.bot hands `CPH` out as
an interface — a method declared only there is not always reachable by exact name on the
concrete type, which is why the category row came back empty at first. So nothing is spelled
out: the action takes any single-string method whose name looks like a channel-info getter
(preferring the ByLogin overload over ById, since the wrong one silently returns nothing),
then any property on the result that looks like a category, then the user record.

The card degrades a field at a time — a channel with no bio, no category, or a default avatar
still gets a shoutout, just a shorter one. If the category row is missing, the action writes
what it *did* find to the Streamer.bot log (**Logs** tab, or `%APPDATA%\Streamer.bot\logs\`):
either the single-string methods matching "channel" that your build exposes, or every
property on the record it got back. One test run names what to reach for.

## Clips

`commands/clip.cs` cuts a Twitch clip of what just happened and posts the link. Twitch clips
what already happened, so `!clip` *after* the moment is the right order — nobody has to
predict anything.

| Typed | Does |
|---|---|
| `!clip` | The last 30 seconds |
| `!clip 15` / `!clip 60` (or `15s`) | Any length from 5 to 60 seconds — Twitch's limits |
| `!clip that jump was clean` | Titles the clip; with no title it takes the stream title |
| `!clip 15 that jump was clean` | Both |
| `!clip last` | The latest clip's link again, cooldown or not |
| `!clip help` | All of the above, in chat |

A number out of range on its own (`!clip 90`) gets a "5 to 60 seconds" reply; with words after
it (`!clip 3 kills in one shot`) it was never a length, so the whole thing is the title.

**Length and title need an up-to-date Streamer.bot** — Twitch added both to its clip API in
December 2025, and Streamer.bot's `CreateClip(title, duration)` passes them on. On an older
build `!clip` still works, at 30 seconds and untitled, and says so in chat when someone asked
for more.

Two things have to be true, and neither is something the action can fix:

- **The stream must be live.** There is no buffer to cut from otherwise, and Twitch rejects
  the call. Chat is told exactly that rather than left wondering.
- **Streamer.bot's Twitch account needs the `clips:edit` scope.** If `!clip` has never worked
  and the stream is definitely live, that is the thing to check: **Platforms → Twitch →**
  re-authorise the broadcaster account. A scope granted before you ever wanted clips is not
  retroactively added.

The toggles sit at the top of the file: `ModsOnly` (off — anyone can clip), `CooldownSeconds`
(30), `DefaultSeconds` (30), `AllowTitles` (on), `PostChatMessage` and `ShowOverlayCard` (an 8s `🎬 <user> clipped it!` card, off is a
perfectly reasonable setting since the chat link is the actual payload).

### The cooldown re-serves, it doesn't reject

Five people typing `!clip` at the same funny thing is five people agreeing, not five moments.
Inside `CooldownSeconds` the action hands back the link it just made instead of cutting a
near-identical clip and instead of telling anyone to wait — so the second person through still
gets a url they can open. The last clip lives in persisted globals (`clip.lastUrl`,
`clip.lastAt`, `clip.lastBy`), so it survives a Streamer.bot restart mid-stream.

An unreadable clock fails *open* — a duplicate clip costs nothing next to a missed moment.

### Reading the url back

Twitch's create-clip response carries only an **id** and an **edit url**; the plain view url is
derived, not sent. So how much of it a Streamer.bot build fills in varies, and the action takes
`Url`, then `EditUrl` with the `/edit` suffix trimmed (that url works for you and for nobody
else, so it is never posted as-is), then builds `clips.twitch.tv/<id>` from the id.

A build whose clip method returns a bare `true` has made the clip without saying where it went.
That is not a failure and is not reported as one — chat gets "clipped it!" and a pointer to the
clips page, and the log gets the method signatures it saw. Saying "no clip" about a clip that is
sitting in the dashboard is the more expensive lie.

The method itself is found by shape, the way `commands/shoutout.cs` finds its getters. This one
is deliberately the exception to that file's no-mutators rule — cutting a clip *is* a mutation —
so the name has to contain `clip` as well as `create`, which is what stops the pattern wandering
onto some other create-shaped method. Shapes are tried richest first — `(title, duration)`, then
a title alone, then the older no-argument and post-to-chat-flag forms — and the **first method
that makes a clip is the last one called**, so a build with several can't cut the moment twice.

**A clip is public the moment it exists**, and anyone in chat can make one. The title is the one
piece of viewer text the action passes on, and it goes to Twitch — shown on the clips page —
never to chat or the overlay card. Twitch runs it through AutoMod and refuses a clip whose title
fails; the action then cuts it again untitled and says so, so a bad title never costs the
moment. That is why there is still no blocked-terms list to keep in sync here. If chat abuses titles
anyway, `AllowTitles` off goes back to ignoring everything after the length.

## Death counter

`commands/death.cs` counts deaths in a challenge run. Attach it to **one** Command trigger with
both `!death` and `!deaths` in the command box — the plural is the read-only one, so a viewer
asking for the score can never add to it.

| Typed | Does |
|---|---|
| `!death` | +1 — *"💀 Death #12 (hc-minecraft-all-bosses)"* |
| `!death undo` (or `-1`, `oops`) | Takes one back, for a misfire |
| `!death 3` | +3, for a spiral nobody was counting through |
| `!death set 30` | Absolute, after counting off-stream |
| `!death switch <name>` | Changes to another counter, or starts a new one at 0 — the one left behind keeps its count |
| `!death reset [name]` | Back to zero; with a name, zeroes (or starts) that counter and switches to it |
| `!death rename <name>` | Renames the current counter, keeping its count |
| `!death forget <name>` | Deletes a counter — never the current one |
| `!death hide` | Blanks the text source; every count is kept, and the next `!death` brings it back |
| `!deaths` | Anyone: *"💀 12 deaths so far (hc-minecraft-all-bosses)"* |
| `!deaths <name>` | Anyone: another counter's count |
| `!deaths list` | Anyone: every counter and its count |
| `!death help [command]` | Anyone: the commands they can use (the full set for mods and VIPs, the `!deaths` side for everyone else), or what one does — `!death help switch` |

**A bare argument is always a delta** — `!death 3` adds three. `set` is the only absolute, which
is what stops `!death 12` silently wiping eleven deaths when a mod meant to add some. `!deaths`
only ever reads, whatever follows it.

`ModsOnly` and `VipsCanEdit` sit at the top of the file — both on, so anyone can read the count
and mods, VIPs and the broadcaster can change it. If a VIP still gets nothing at all, check the
Command trigger's own permissions in Streamer.bot aren't set to mods only.

### One counter per run

Every counter has a name, and switching between them never loses a count:

```
!death rename hc-minecraft-all-bosses   <- name the run in progress (count kept)
!death switch hc-wow                    <- new counter at 0; Minecraft saved where it was
!death switch hc-minecraft-all-bosses   <- back to Minecraft, right where it left off
```

Names are lowercased and spaces become dashes, so `!death switch HC WoW` is `hc-wow`. Each
counter's count is the persisted global `death.saved.<name>`, the list of them is `death.runs`
and the current one is `death.run`. `death.count` and `death.label` still always hold the
*current* counter, which is what `!bad` and the overlay read — neither needs to know there is
more than one.

A count from before counters had names carries over on the first `!death` or `!deaths` after
updating: it becomes a counter named after its old run name, or the unnamed one if it had none.

### Showing it in OBS

No browser source and no overlay build: the action writes an **OBS Text source** through
Streamer.bot's own OBS connection.

1. In OBS, add a **Text (GDI+)** source named `Deaths` — font, colour, outline and position are
   all yours, and it can sit in as many scenes as you like.
2. In Streamer.bot: **Platforms → OBS Studio**, connect if you haven't. That is the only
   dependency; the action needs no scene name and no source id.
3. Type `!death`. The source fills in.

Four toggles configure it: `ObsTextSource` (the source's **name**, `"Deaths"` — it must match
exactly), `ObsTextFormat` (`"Deaths: {count}"`; `{label}` is the run name, and wrapping it as
`"Deaths: {count} ({label})"` is tidied back to `"Deaths: 12"` on an unnamed run rather than
leaving empty brackets), `ObsConnection` for a rig with more than one OBS, and `UpdateObsText`
to switch the whole thing off.

Addressed by source **name**, using `SetInputSettings` rather than SB's `ObsSetGdiText` wrapper:
a counter shared across scenes then needs no scene named anywhere, so it cannot quietly stop
updating when you build a new scene around it — and it works for a FreeType2 source too, where
the GDI+ wrapper does not.

The nice property of doing it this way is that **OBS saves the text with the scene collection**.
Close OBS at 43 deaths, open it tomorrow, and it still says 43 before Streamer.bot has sent
anything. Nothing needs replaying at startup.

#### If Streamer.bot isn't connected to OBS

Set `TextFilePath` to a path instead, and tick **Read from file** on the text source. A file
needs no connection at all. It is the alternative to the OBS route rather than a companion to
it — running both just writes the same number twice.

### Where the count lives

Each counter's `death.saved.<name>` is a **persisted global**, and it is the record: the text on
screen is a copy. That is what makes a Streamer.bot restart mid-run harmless, and it is why
`!deaths` can answer with no OBS involved at all.

`ShowOnOverlay` (off) also sends the count to the old alerts overlay; it needs that overlay and
its `counters-refresh.cs` action, both in the `overlays` repo, and can stay off.

### Zero shows, nothing hides

`!death reset` leaves **Deaths: 0** on screen — that is the point of resetting at the start of a
run. The counter goes away only on `!death hide`, which writes empty text and unsets
`death.count` / `death.label` (so `!bad`'s death lines sit out too) — but keeps every saved
count, so the next `!death` carries on from where it was. Empty text is the whole trick: no scene item to hide, no visibility to remember to put
back, and the source can stay in every scene it already sits in. Between challenges there is
nothing to count, and a stale zero in the corner of a just-chatting stream is worse than no
counter at all.

### Adding a second counter

Nothing here is death-specific — bosses beaten, cheese consumed, a counter is a name and a
number. A second one is a copy of `commands/death.cs` with its own command, globals and
`ObsTextSource`, pointed at a second text source in OBS.

## Roasts

`commands/bad.cs` is the one viewers reach for when you are playing badly. Attach it to a
**Command** trigger with `!bad` in the box — no mods tick, since a roast nobody in chat can
call is just you talking to yourself.

```
viewer:  !bad
bot:     Elden Ring shipped with a tutorial, KingBrewl. It's right there in the menu.
```

The lines live in the `Roasts` array at the top of the file and that array is the whole
configurable surface — edit it, paste the action back in, done. Four placeholders are filled
in: `{streamer}`, `{user}` (whoever typed it), `{game}` (the category the channel is set to
right now) and `{deaths}` (the counter from `commands/death.cs`).

**A line is only picked when every placeholder it uses has a value.** That is what lets the
array carry lines that lean on the game and lines that lean on the death count without either
turning into a sentence with a hole in it: `{game}` lines sit out when nothing knows what the
category is, `{deaths}` lines sit out until a run is actually in progress, and the general
lines always play. So one array covers a no-category just-chatting stream and a forty-death
boss fight without any setup in between.

The category comes off Streamer.bot's own Twitch credentials — no API key, found by shape the
way `commands/shoutout.cs` finds it, and mutators are excluded, because a single-string method
with `game` in the name could just as easily be the one that *changes* your category live on
stream. The death count is just the `death.count` global read back, so `commands/death.cs` does
not need to be imported for `!bad` to work — its lines simply never come up.

### Only one target

**Anything typed after `!bad` is ignored outright.** Not parsed, not echoed, not treated as a
name — the command roasts the broadcaster and there is no spelling of it that roasts anyone
else. That is deliberate and it is the reason this action needs no blocked-terms list to keep
in sync with `alert-cheer.cs`: the only viewer-supplied text that ever reaches chat is the
username of the person who typed it, exactly as with `!clip`.

The flip side is that these lines go out under **your** channel's name. Keep them affectionate
and aimed at the play rather than the player; a line Twitch would object to is your problem,
not chat's.

### The cooldown goes quiet

`CooldownSeconds` (45) works like `!clip`'s in spirit and differently in practice. Five people
typing `!bad` at the same bad play are five people agreeing, not five bad plays — but unlike a
clip there is nothing to re-serve them, so the calls after the first do **nothing at all**: no
second roast, and no "wait 30 seconds" line either. A bot scolding four people for agreeing is
more chat noise than the roast it is protecting.

An unreadable clock fails **open**, for the same reason the clip cooldown does: one extra roast
costs nothing, and a stuck cooldown kills the command silently for the rest of the stream.

### Not the same one twice

The last `NoRepeatMemory` (20) roasts are off the table when picking the next, which is what
keeps a finite list from feeling finite. They are remembered in the `bad.recent` persisted
global as hashes of the line rather than indexes into the array — so editing `Roasts` can't
leave a stale number pointing at some innocent bystander of a roast — and when everything
currently eligible has been said lately the memory is simply forgotten and the whole set
comes back.

If you grow the memory, the number to stay under is **the count of lines with no placeholder
in them** — currently 64 of the 97 — and not the length of the array. A just-chatting stream
with no death counter running sees only those, and a memory as long as that pool would spend
every pick forgetting and starting over.

`ShowOverlayCard` puts the line on screen as well, off by default: a roast belongs in chat,
where the person being roasted is already looking.

## Hugs

`commands/hug.cs` is the one command that *does* put a viewer's text in chat — the name being
hugged — so that name is held to what a Twitch login can look like and then looked up. Only a
user Twitch knows is ever posted, spelled as Twitch spells it; anything else gets "couldn't find
that adventurer" without repeating what was typed. `!hug` with no name hugs the whole tavern,
and hugging yourself gets its own line rather than "X pulls X close".

## Lurking

`commands/lurk.cs` serves both `!lurk` and `!unlurk` — put both in one Command trigger's box.
`!lurk` notes the time in a persisted Twitch user variable (`lurk.since`), and `!unlurk` turns
it into *"…emerges from the tavern shadows after 47 minutes…"*. A second `!lurk` keeps the
first time, and a lurk older than `StaleHours` (12) is treated as left over from an earlier
stream, so nobody is welcomed back "after 26 hours". `!unlurk` without a `!lurk` is just the
welcome-back line.

## Let's go

`commands/letsgo.cs` counts how often the streamer says "let's go". Chat is the one listening,
so **anyone** can count one with `!letsgo` — *"LET'S GOOO! #14 this stream, 1,203 all time"*.

| Typed | Does |
|---|---|
| `!letsgo` | +1 (anyone) — anything typed after it is just cheering and is ignored |
| `!letsgo count` | Anyone: both numbers, without adding one |
| `!letsgo undo` | Mods, VIPs: takes one back |
| `!letsgo set <n>` / `setall <n>` | Mods, VIPs: sets this stream's / the all-time count exactly — each moves one number only |
| `!letsgo help [command]` | Anyone: the commands they can use (everything for mods and VIPs, counting and the score for everyone else), or what one does — `!letsgo help setall` |

**A burst counts once.** Five people typing `!letsgo` at the same "let's go" are five people
agreeing, so anything inside `BurstSeconds` (10) of the last count is silently ignored — the
same idea as `!clip` and `!bad`. Mods and VIPs aren't exempt; they heard the same one.

**"This stream" resets itself**: a count after more than `NewStreamAfterHours` (6) of quiet
starts the stream number over, so there's no Stream Online trigger to set up. The all-time count
never resets on its own. All-time milestones (100, 250, 500, 1,000, 5,000) get a line of their
own, in the `Milestones` table at the top of the file.

The numbers live in persisted globals (`letsgo.stream`, `letsgo.total`, `letsgo.lastAt`). An
OBS Text source works the way the death counter's does — off by default; add a Text source
named `LetsGo`, then switch `UpdateObsText` on. A **Hot Key** trigger on the same action counts
one too, and skips the burst check, since that's you at your own keyboard.

## Gotchas

- **A fresh clip link works before the clip is watchable.** Twitch returns the url immediately
  and finishes processing a few seconds later, so the first person to click may land on a
  still-rendering clip. Nothing is wrong; a reload fixes it.
- **A shoutout costs a Twitch API round trip** before anything is posted — usually well under a
  second, but not instant.
- **A hotkey is the natural way to add a death while you are playing.** Add a **Hot Key**
  trigger to `commands/death.cs` itself and it steps by one, exactly as `!death` does —
  persisted global, chat line and OBS text all included. A hotkey carries no user and no mod
  flag, so `ModsOnly` deliberately does not apply to it: a trigger with no chat user behind it
  is you, at your own keyboard. A channel point redeem does carry a user, and is still screened.

## First

`rewards/first.cs` greets whoever claims the *First* reward in chat, in the tavern voice of
`!lurk`. That is all it does — the "only the first person" part is the reward's own setting,
which Twitch enforces server side, so two people redeeming in the same second can't both win.

Create the reward **in Streamer.bot** (**Platforms → Twitch → Channel Point Rewards →**
right-click **→ Create Reward**), not the Twitch dashboard — Twitch only lets the app that made
a reward refund, rename or pause it, so this keeps those doors open for later.

| Setting | Value |
|---|---|
| Cost | 1 |
| Max Per Stream | 1 |
| Skip Reward Requests Queue | on |

The per-stream limit resets when a stream starts. Twitch still accepts redemptions while you're
offline, so someone can claim it before you go live; if that turns out to happen, pausing the
reward on Stream Offline and unpausing it on Stream Online is the fix.

## Check-in

`rewards/checkin.cs` is the reward for everyone who isn't First. Each viewer can check in once a
stream, and chat gets their arrival number and how many times they've checked in:

> Ponytail is the 4th to check in tonight! Welcome back to the hearth - that's 12 visits all told.

A first-ever check-in gets a welcome instead, and visits 10, 25, 50, 100 and 250 get a line of
their own (the `VisitMilestones` table at the top of the file).

**No streaks.** Twitch already shows watch streaks, and a streak that resets because someone
forgot to click is a penalty rather than a reward. The visit count only ever goes up.

| Setting | Value |
|---|---|
| Cost | up to you — cheap enough that nobody thinks twice |
| Max Per User Per Stream | 1 |
| Skip Reward Requests Queue | on |

Attach **two** triggers to the action: the reward, and **Twitch → Stream Online**. Stream
Online starts the arrival count over and opens a new night of check-ins. It isn't fragile
about it: if Streamer.bot wasn't running when you went live, the first check-in after
`NewStreamAfterHours` (6) of quiet starts the new stream instead. And a second Stream Online
within those 6 hours — going live again after a crash — is treated as the same stream, so the
arrival count carries on and nobody is counted twice.

Twitch resets the per-stream limit on any go-live, crash restarts included, so after one a
viewer who already checked in *can* redeem again. They're told they're already in and nothing
is counted, but the points are spent; refund them from the reward queue if it comes up.

The channel-wide numbers are persisted globals (`checkin.stream`, `checkin.arrivals`,
`checkin.lastAt`, `checkin.startedAt`); each viewer's last stream and total are Twitch user
variables (`checkin.lastStream`, `checkin.total`).

### The clip is Tangia's

Nothing in the action talks to Tangia, and nothing needs to. A redemption is a Twitch event that
every connected app sees on its own: Streamer.bot posts the chat line, and Tangia plays the
clip off the same redemption, side by side. Point the Tangia alert at the *Check-in* reward and
that's the whole integration.

A check-in rush at the start of a stream queues one clip per redemption, so keep the clip
short.
