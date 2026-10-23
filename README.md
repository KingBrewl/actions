# Streamer.bot actions

Twitch chat commands and channel point rewards for [Streamer.bot](https://streamer.bot). Each
file is one self-contained C# action, with no API keys or extra setup beyond what's listed below.

## Setup

1. In Streamer.bot: **Actions → right-click → Add Action**, named after the file.
2. Add a **C# Method** sub-action and paste in the file's full contents.
3. Attach the trigger from the table below. Right-click the action → **Test Trigger** to try it.

### Make it yours

- **Emotes.** The messages use this channel's emotes (`kingbre3Heart`, `kingbre3LFG`, …). Swap
  them for your own, or for plain emoji.
- **Wording.** Most commands have a medieval tavern theme. The messages sit in lists near the top
  of each file, so you can reword them, add to them or delete them.
- **Settings.** Each file starts with a few settings (`ModsOnly`, `CooldownSeconds`, …). Each
  one has a comment saying what it does.
- **`links.cs`** holds your Discord invite, socials and YouTube. Set them at the top of the file.
- **`commands.cs`** is a hand-written list for `!commands`. Remove any commands you don't
  import, and add any new commands you write.

When you change a file, paste it into its action again.

## Actions

| File | Trigger | What it does |
|---|---|---|
| `commands/commands.cs` | Command `!commands` / `!cmds` / `!cmd` | Lists the commands the asker can use (mods and VIPs also see theirs) |
| `commands/links.cs` | Command `!discord`, `!social` / `!socials`, `!youtube` / `!yt`, all in one trigger | Posts your links |
| `commands/game.cs` | Command `!game` | Posts the current category |
| `commands/lurk.cs` | Command `!lurk` and `!unlurk` in one trigger | `!unlurk` says how long they were away |
| `commands/hug.cs` | Command `!hug` | Hugs `@name`, or the whole chat if no name is given |
| `commands/tuck.cs` | Command `!tuck` | Tucks `@name` in for bed. A bare `!tuck` tucks in the asker |
| `commands/pillory.cs` | Command `!pillory` | Puts `@name` in the stocks for a random crime, as a joke. Nobody is actually timed out |
| `commands/quote.cs` | Command `!quote` | `!quote` shows a random quote, `!quote 5` shows quote 5, `!quote add <text>` adds one, and mods can `!quote remove 5` |
| `commands/bad.cs` | Command `!bad` | Roasts the streamer's gameplay with a random line |
| `commands/clip.cs` | Command `!clip` | Makes a Twitch clip and posts the link. See [Clips](#clips) |
| `commands/death.cs` | Command `!death` and `!deaths` in one trigger | Death counter shown in chat and on screen. See [Death counter](#death-counter) |
| `commands/letsgo.cs` | Command `!letsgo` / `!lg` | Counts "let's go"s for this stream and all time. See [Let's go](#lets-go) |
| `commands/raid.cs` | Command `!raid` | Posts a raid message for chat to copy, once with sub emotes and once with emoji |
| `commands/shoutout.cs` | Command `!so` / `!shoutout` (mods, VIPs), and optionally Twitch → Raid | Posts a shoutout in chat and sends Twitch's own `/shoutout`. On a Raid trigger it shouts out the raider automatically |
| `rewards/first.cs` | Channel point reward *First* | Greets the first person to redeem it each stream. See [Rewards](#rewards) |
| `rewards/checkin.cs` | Channel point reward *Check-in*, **and** Twitch → Stream Online | Posts each viewer's arrival number and total visits. See [Rewards](#rewards) |

When a command takes a name (`!hug`, `!tuck`, `!pillory`), the name must be a real Twitch user.
That stops chat from making the bot post whatever text it likes.

`!bad`, `!pillory`, `!clip` and `!letsgo` have built-in cooldowns. When several people type
the same command at once, it only fires once.

## Clips

| Typed | Does |
|---|---|
| `!clip` | Clips the last 30 seconds |
| `!clip 15` | Clips a length from 5 to 60 seconds |
| `!clip that jump was clean` | Titles the clip |
| `!clip 15 that jump was clean` | Sets both |
| `!clip last` | Posts the latest clip's link again |
| `!clip help` | Explains all of the above in chat |

- **Clips only work while you're live.**
- **Streamer.bot needs the `clips:edit` permission.** If `!clip` never works, go to
  **Platforms → Twitch** and re-authorise your broadcaster account.
- Setting a length or title needs a recent Streamer.bot. Older versions clip 30 seconds with no
  title.
- Inside the cooldown, `!clip` posts the link to the clip it just made instead of making a new one.

## Death counter

Mods and VIPs change the count. Anyone can read it.

| Typed | Does |
|---|---|
| `!death` | Adds 1 |
| `!death 3` / `!death undo` | Adds 3 / takes one back |
| `!death set 30` | Sets the exact count |
| `!death switch <name>` | Switches to another counter, or starts a new one. Each counter keeps its own count |
| `!death reset [name]` / `rename <name>` / `forget <name>` | Resets a counter to 0, renames the current one, or deletes one |
| `!death hide` | Takes the counter off screen without losing any counts |
| `!deaths` / `!deaths list` | Shows the current count, or every counter |
| `!death help` | Explains the commands in chat |

**To show the count on screen:** add a **Text (GDI+)** source named `Deaths` in OBS and connect
Streamer.bot to OBS (**Platforms → OBS Studio**). If Streamer.bot can't connect to OBS, set
`TextFilePath` instead and tick **Read from file** on the text source.

Add a **Hot Key** trigger to the same action to count deaths without typing.

## Let's go

Anyone can `!letsgo`. Mods and VIPs can use `!letsgo undo`, `!letsgo set <n>` (this stream) and
`!letsgo setall <n>` (all time). `!letsgo count` shows both counts without adding to them.

The stream count resets on its own after 6 hours with no let's-gos. All-time milestones get a
special message. To show the count on screen, add an OBS Text source named `LetsGo` and set
`UpdateObsText` to `true`.

## Rewards

Create both rewards **in Streamer.bot** (**Platforms → Twitch → Channel Point Rewards →
right-click → Create Reward**), not on the Twitch dashboard. Twitch only lets the app that made a
reward refund, edit or pause it.

| | First | Check-in |
|---|---|---|
| Cost | 1 | Up to you — keep it cheap |
| Limit | Max Per Stream: 1 | Max Per User Per Stream: 1 |
| Skip Reward Requests Queue | on | on |

For Check-in, also attach the **Twitch → Stream Online** trigger, which starts a fresh count for
each stream. If you go live again within 6 hours, for example after a crash, it counts as the
same stream.

## Adding your own commands

Streamer.bot's C# compiler only gives you mscorlib. `System.Reflection`, `Random` and
`StringBuilder` work, but anything from `System.dll` (`System.Net`, `System.Uri`,
`System.Linq`, `Regex`) won't compile. Add new commands to `commands/commands.cs` so they show
up in `!commands`.
