# Streamer.bot actions

Twitch chat commands, channel point rewards and alerts for [Streamer.bot](https://streamer.bot). Each
file is one self-contained C# action, with no API keys or extra setup beyond what's listed below.

## Setup

1. In Streamer.bot: **Actions → right-click → Add Action**, named after the file.
2. Add a **C# Method** sub-action and paste in the file's full contents.
3. Attach the trigger from the table below. Right-click the action → **Test Trigger** to try it.
4. Put the action in its group from [Groups](#groups).

### Groups

Grouping actions this way lets you disable a whole kind of thing at once: a quieter stream, or
a clean VOD when you're recording.

| Group | Actions | Shows up in the VOD? |
|---|---|---|
| **Core** | `!commands`, links, `!game`, `!uptime`, `!followage`, `!accountage`, `!schedule`, `!lurk`, `!clip`, `!so`, `!raid` | No, chat only |
| **Chat Fun** | `!hug`, `!pet`, `!tuck`, `!pillory`, `!bad`, `!quote`, `!pp`/`!rizz` | No, chat only |
| **Counters** | `!death`, `!letsgo` | The death counter does, if it's on screen. Use `!death hide` for a clean VOD, which keeps counting |
| **Coppers** | Check-in, Coppers Events, `!coppers`, `!gamble`, `!deathroll` | Check-in's video does: it's a Twitch alert, so pause the reward |
| **Rewards** | First, Last | No, chat only |
| **Sounds** | The sound board (`sfx.cs`) | **Yes.** Disable this group for a clean VOD |
| **Timers & Alerts** | Discord promo, ad warning | No, chat only |

"Chat only" assumes there's no chat overlay on screen. If you have one, hide it too.

### Make it yours

- **Emotes.** The messages use this channel's emotes (`kingbre3Heart`, `kingbre3LetsGo`, …). Swap
  them for your own, or for plain emoji.
- **Wording.** Most commands have a medieval tavern theme. The messages sit in lists near the top
  of each file, so you can reword them, add to them or delete them.
- **Settings.** Each file starts with a few settings (`ModsOnly`, `CooldownSeconds`, …). Each
  one has a comment saying what it does.
- **`links.cs`** holds your Discord invite, socials, YouTube and the `!smp` message. Set them at the top of the file. `alerts/discord.cs` has its own copy of the Discord invite, so update it there too.
- **`info.cs`** has `ScheduleFallback`, what `!schedule` says when your Twitch schedule is empty. Put your usual days there if you don't keep a Twitch schedule.
- **`commands.cs`** is a hand-written list for `!commands`. Remove any commands you don't
  import, and add any new commands you write.

When you change a file, paste it into its action again.

## Actions

| File | Trigger | What it does |
|---|---|---|
| `commands/commands.cs` | Command `!commands` / `!cmds` / `!cmd` | Lists the commands the asker can use (mods and VIPs also see theirs) |
| `commands/links.cs` | Command `!discord`, `!social` / `!socials`, `!youtube` / `!yt`, `!smp` / `!minecraft`, all in one trigger | Posts your links. `!smp` points chat to the Discord's #minecraft channel |
| `commands/game.cs` | Command `!game` | Posts the current category |
| `commands/info.cs` | Command `!uptime`, `!followage`, `!accountage` and `!schedule`, all in one trigger | Stream uptime, how long the asker (or `@name`) has followed, account age, and the next streams on your Twitch schedule. Asks Twitch directly through Streamer.bot's login. Set `ScheduleFallback` at the top for when the schedule is empty |
| `commands/lurk.cs` | Command `!lurk` and `!unlurk` in one trigger | `!unlurk` says how long they were away |
| `commands/hug.cs` | Command `!hug` | Hugs `@name`, or the whole chat if no name is given |
| `commands/pet.cs` | Command `!pet` | Gives `@name` gentle head pats. A bare `!pet` gets the asker pet |
| `commands/tuck.cs` | Command `!tuck` | Tucks `@name` in for bed. A bare `!tuck` tucks in the asker |
| `commands/pillory.cs` | Command `!pillory` | Puts `@name` in the stocks for a random crime, as a joke. Nobody is actually timed out |
| `commands/quote.cs` | Command `!quote` | `!quote` shows a random quote, `!quote 5` shows quote 5, `!quote add <text>` adds one, and mods can `!quote remove 5`. Each new quote records the game you were playing, shown like `#3: "text" — KingBrewl, Elden Ring`. Mods can type `!quote game` to list quotes with no game, and `!quote game 5 Elden Ring` to set one |
| `commands/bad.cs` | Command `!bad` | Roasts the streamer's gameplay with a random line |
| `commands/clip.cs` | Command `!clip` | Makes a Twitch clip and posts the link. See [Clips](#clips) |
| `commands/death.cs` | Command `!death` and `!deaths` in one trigger | Death counter shown in chat and on screen. See [Death counter](#death-counter) |
| `commands/letsgo.cs` | Command `!letsgo` / `!lg` | Counts "let's go"s for this stream and all time. See [Let's go](#lets-go) |
| `commands/rate.cs` | Command `!pp`, `!rizz` and `!girth` in one trigger | Rates the asker or `@name`. Each person's result stays the same until the next day, which starts at 6am |
| `commands/coppers.cs` | Command `!coppers` / `!copper` / `!give` | Your coppers and leaderboard place, `@name`'s, or `!coppers top`. `!give @name 5` hands coppers to someone. See [Coppers](#coppers) |
| `commands/gamble.cs` | Command `!gamble` | Bets coppers on a 1–100 roll. See [Coppers](#coppers) |
| `commands/deathroll.cs` | Command `!deathroll` / `!dr` | The WoW deathroll game between two viewers, with or without a coppers bet. See [Deathroll](#deathroll) |
| `commands/raid.cs` | Command `!raid` | Posts a raid message for chat to copy, once with sub emotes and once with emoji |
| `commands/shoutout.cs` | Command `!so` / `!shoutout` (mods, VIPs), and optionally Twitch → Raid | Posts a shoutout in chat and sends Twitch's own `/shoutout`. On a Raid trigger it shouts out the raider automatically |
| `sounds/sfx.cs` | Command with every sound's name (`!oof`, `!bruh`, …) and `!sounds`, all in one trigger | Plays sound effects on stream. `!sounds` lists them. See [Sounds](#sounds) |
| `rewards/first.cs` | Channel point reward *First* | Greets the first person to redeem it each stream. See [Rewards](#rewards) |
| `rewards/last.cs` | Channel point reward *Last* | "If you ain't first, you're last!" Names tonight's First winner, with its own joke if that winner redeems Last too. See [Rewards](#rewards) |
| `rewards/checkin.cs` | Channel point reward *Check-in*, **and** Twitch → Stream Online | Posts each viewer's arrival number and total visits, and pays 5 coppers. See [Rewards](#rewards) and [Coppers](#coppers) |
| `alerts/discord.cs` | Timed Action *Discord Promo Timer* (**Settings → Timed Actions**, repeat every 2700 seconds) | Promotes the Discord server in chat every 45 minutes |
| `alerts/ads.cs` | Twitch → Upcoming Ad, with **1 minute** ticked | Warns chat a minute before an ad break and says how long it will last |
| `alerts/coppers-events.cs` | Twitch → Subscription, Resubscription, Gift Subscription, Cheer, Raid and Follow, all on one action. **Not** Gift Bomb | Pays coppers for supporting the channel. See [Coppers](#coppers) |

When a command takes a name (`!hug`, `!pet`, `!tuck`, `!pillory`), the name must be a real Twitch user.
That stops chat from making the bot post whatever text it likes.

`!bad`, `!pillory`, `!clip`, `!letsgo` and the sounds have built-in cooldowns. When several people type
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

## Coppers

Coppers are the tavern's currency. They are separate from Crowns, the channel points. The main
way to earn them is the Check-in reward: 5 coppers per check-in. A viewer's first check-in after
you install this also pays 5 for each check-in they made before, so regulars start ahead.

Supporting the channel also pays (`alerts/coppers-events.cs`):

| Event | Coppers |
|---|---|
| Sub or resub | 25, or 50 at tier 2 and 125 at tier 3 |
| Gift sub | 10 per gift to the gifter (tier 2 ×2, tier 3 ×5). Anonymous gifts pay no one |
| Bits | 1 per 100. Leftover bits carry over to the next cheer |
| Raid | 25 to the raider |
| Follow | 5, only the first time someone follows |

Don't attach the Gift Bomb trigger. Twitch also sends a Gift Subscription for every sub in a
bomb, and those already pay the gifter, so a Gift Bomb trigger would pay twice.

**Set up a queue first.** In Streamer.bot, go to **Action Queues → Add**, name it `Coppers` and
tick **Blocking**. Then put Check-in, Coppers Events, `!coppers`, `!gamble` and `!deathroll` in
that queue (each action's **Queue** setting). That way, two of them can't change the same purse
at the same moment.

| Typed | Does |
|---|---|
| `!coppers` / `!coppers @name` | Shows a purse and its place on the leaderboard |
| `!coppers top` | The 5 richest |
| `!give @name 5` | Hands 5 of your coppers to someone. `!coppers give @name 5` works too |
| `!coppers add @name 5` / `take @name 5` | Mods: give or take up to 1000 at a time |
| `!gamble 5` / `half` / `all` | Rolls 1–100. 1–52 loses the bet, 53–97 wins the same amount, 98–100 wins double |

Gambling loses about 6% of what's bet over time, so it removes coppers from the tavern instead of
adding more. The leaderboard covers everyone who has ever held a copper.

## Deathroll

The challenger rolls 1–100 first. Each roll after that is 1 to the previous roll, and whoever
rolls a 1 loses.

| Typed | Does |
|---|---|
| `!deathroll @name 5` | Challenge someone for 5 coppers each. Leave out the number to play for honour |
| `!deathroll accept` / `decline` | Answer a challenge. Accepting puts both bets in the pot |
| `!deathroll cancel` | Take your challenge back before it's answered |
| `!deathroll` | Roll, on your turn |
| `!deathroll stop` | Mods: end the game and return both bets |

A challenge nobody answers lapses after 60 seconds. A player who hasn't rolled for 2 minutes
loses if their opponent types `!deathroll`. If someone starts a new challenge, the stale game
also ends, and the player who was waiting wins.

## Sounds

`sounds/sfx.cs` plays every sound. The sound files aren't in this repo. Save them in
`C:\Stream\Sounds`, or change `SoundsFolder` at the top of the file.

**To add a sound:**

1. Download it into the sounds folder, for example from [myinstants](https://www.myinstants.com).
2. Add a line to `Sounds` in `sfx.cs`: the command, the file name, the volume and the cooldown.
3. Add the command to `Unlocked` in `sfx.cs` if chat should be able to play it.
4. Add the command (e.g. `!bruh`) to the action's trigger, then paste `sfx.cs` in again.

Chat can only play the sounds in `Unlocked`: for now `!oof`, `!bruh`, `!faaah` and `!meow`. The
other sounds stay in `Sounds` with their volumes, but they're locked: their commands do nothing
and `!sounds` doesn't list them.

If a sound's file is missing, the sound is skipped and left out of `!sounds`, so you can add
the line before you have the file.

Each sound has its own cooldown. After any sound plays, all sounds are also blocked for 10
seconds, so chat can't stack them. Mods, VIPs and you skip both cooldowns. myinstants clips
vary a lot in loudness, so set each sound's volume by playing it next to your mic.

Put this action in the **Sounds** group. Disabling that group silences every sound at once, for
a clean VOD.

**On stream:** the sounds play through Windows' default output. In OBS, add an **Application
Audio Capture** source for `Streamer.bot.exe`. That gives the sounds their own volume slider,
a place for a limiter, and the option to leave them off the Twitch VOD track. If Desktop Audio
also captures them, stream viewers hear every sound twice.

## Rewards

Create the rewards **in Streamer.bot** (**Platforms → Twitch → Channel Point Rewards →
right-click → Create Reward**), not on the Twitch dashboard. Twitch only lets the app that made a
reward refund, edit or pause it.

| | First | Last | Check-in |
|---|---|---|---|
| Cost | 1 | Up to you — keep it cheap | Up to you — keep it cheap |
| Limit | Max Per Stream: 1 | Max Per User Per Stream: 1 | Max Per User Per Stream: 1 |
| Skip Reward Requests Queue | on | on | on |

For Check-in, also attach the **Twitch → Stream Online** trigger, which starts a fresh count for
each stream. If you go live again within 6 hours, for example after a crash, it counts as the
same stream.

Last gets the First winner from what `first.cs` saves, so paste the new `first.cs` in as well. It
uses Check-in's stream count to tell tonight's First from an old one. Without Check-in it goes by
time instead: a First claimed in the last 6 hours counts as tonight's.

## Adding your own commands

Streamer.bot's C# compiler only gives you mscorlib. `System.Reflection`, `Random` and
`StringBuilder` work, but anything from `System.dll` (`System.Net`, `System.Uri`,
`System.Linq`, `Regex`) won't compile. Newtonsoft's `JsonConvert` works, but `JObject` and
`JToken` don't: they need `System.Core` (error CS0012), so read JSON into small classes
instead, as `info.cs` does. Add new commands to `commands/commands.cs` so they show
up in `!commands`.
