using System;
using System.Collections.Generic;
using System.IO;

// Trigger: Twitch -> Command, with every sound's command and "!sounds" in the command box —
// one action plays every sound, the way links.cs posts every link.
//
//   !oof, !bruh, ...   -> plays that sound on stream
//   !sounds            -> lists the sounds chat can play
//
// Adding a sound:
//   1. Save the file (.mp3 or .wav) into SoundsFolder. myinstants.com has most of the classics.
//   2. Add a line to Sounds below: the command, the file name, its volume and its cooldown.
//   3. Add the command to this action's trigger in Streamer.bot.
// A sound whose file is missing is skipped and left off !sounds, so the list can run ahead of
// the folder without breaking anything.
//
// Two cooldowns, both silent:
//   - Each sound has its own, so one sound can't be played on repeat.
//   - GlobalCooldownSeconds blocks every sound for a moment after any sound, so chat can't
//     stack five on top of each other.
// Mods, VIPs and the broadcaster skip both, and their sounds don't start either cooldown —
// the same rule !oof always had.
//
// For a clean VOD, disable this action (or its group) and every sound goes quiet at once.
public class CPHInline
{
    // Where the sound files live. Use @"..." so the backslashes don't need doubling.
    private const string SoundsFolder = @"C:\Stream\Sounds";

    // After any viewer's sound, no viewer can play another for this long.
    private const int GlobalCooldownSeconds = 10;

    private class Sound
    {
        public string File;
        public float Volume;          // 0.0 silent to 1.0 full
        public int CooldownSeconds;   // this sound only

        public Sound(string file, float volume, int cooldownSeconds)
        {
            File = file;
            Volume = volume;
            CooldownSeconds = cooldownSeconds;
        }
    }

    /* -----------------------------------------------------------------------------------
     * The sounds, by command (without the !), in the order !sounds lists them.
     *
     * Clips from myinstants vary a lot in loudness, so each Volume was measured to match the
     * others: oof at 0.6 is the reference, and the rest are set to come out as loud as it
     * does. A new sound's volume can be worked out the same way with ffmpeg:
     *
     *   ffmpeg -i new.mp3 -af ebur128 -f null -     (read "I:", the loudness in LUFS)
     *   Volume = 10 ^ ((-23.2 - LUFS) / 20)          (-23.2 is oof's loudness at 0.6)
     *
     * Or just play it next to the others and adjust by ear. To make every sound louder or
     * quieter together, use the Streamer.bot slider in OBS instead.
     * ----------------------------------------------------------------------------------- */
    private static readonly List<KeyValuePair<string, Sound>> Sounds = new List<KeyValuePair<string, Sound>>
    {
        Entry("oof",             "oof.mp3",                     0.60f, 60),
        Entry("bruh",            "bruh.mp3",                    0.24f, 60),
        Entry("vineboom",        "vine-boom.mp3",               0.19f, 60),
        Entry("airhorn",         "airhorn.mp3",                 0.21f, 120),
        Entry("wompwomp",        "womp-womp.mp3",               0.33f, 60),
        Entry("emotionaldamage", "emotional-damage.mp3",        0.19f, 120),
        Entry("faaah",           "faaah.mp3",                   0.27f, 60),
        Entry("wow",             "anime-wow-sound-effect.mp3",  0.40f, 90),
        Entry("meow",            "m-e-o-w.mp3",                 0.45f, 60),
        Entry("heehee",          "michael-jackson-hee-hee.mp3", 0.56f, 60),
        Entry("getout",          "tuco-get-out.mp3",            0.31f, 60),
        // !rizz is taken by the rizz rating in commands/rate.cs.
        Entry("rizzup",          "rizz-sound-effect.mp3",       0.27f, 60)
    };

    private static KeyValuePair<string, Sound> Entry(string command, string file, float volume, int cooldownSeconds)
    {
        return new KeyValuePair<string, Sound>(command, new Sound(file, volume, cooldownSeconds));
    }

    // Persisted, so restarting Streamer.bot doesn't reset the cooldowns.
    private const string LastAtPrefix = "sfx.lastAt.";
    private const string AnyLastAt = "sfx.lastAt";

    public bool Execute()
    {
        string command = Invoked();

        if (command == "sounds" || command == "sfx")
        {
            CPH.SendMessage(List());
            return true;
        }

        Sound sound = Find(command);
        if (sound == null)
        {
            CPH.LogWarn("[sfx] no sound for \"" + command + "\" - add it to Sounds in sfx.cs");
            return true;
        }

        string path = Path.Combine(SoundsFolder, sound.File);
        if (!File.Exists(path))
        {
            CPH.LogWarn("[sfx] sound file not found: " + path);
            return true;
        }

        bool exempt = Exempt();
        if (!exempt && (OnCooldown(AnyLastAt, GlobalCooldownSeconds) || OnCooldown(LastAtPrefix + command, sound.CooldownSeconds)))
            return true;

        CPH.PlaySound(path, sound.Volume, false);

        if (!exempt)
        {
            string now = DateTime.UtcNow.Ticks.ToString();
            CPH.SetGlobalVar(AnyLastAt, now, true);
            CPH.SetGlobalVar(LastAtPrefix + command, now, true);
        }
        return true;
    }

    private Sound Find(string command)
    {
        foreach (var entry in Sounds)
            if (entry.Key == command) return entry.Value;
        return null;
    }

    // Only the sounds whose files are actually there.
    private string List()
    {
        var names = new List<string>();
        foreach (var entry in Sounds)
            if (File.Exists(Path.Combine(SoundsFolder, entry.Value.File))) names.Add("!" + entry.Key);

        if (names.Count == 0) return "The tavern's sound board is empty for now. kingbre3O7";
        return "kingbre3Dance Sounds: " + string.Join(" ", names.ToArray());
    }

    // Mods, VIPs and the broadcaster skip the cooldowns. A Hot Key or Test Trigger has no user,
    // so that's you, and you skip them too.
    private bool Exempt()
    {
        bool fromChat = args.ContainsKey("userName") || args.ContainsKey("user");
        if (!fromChat) return true;
        return Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator", "isVip", "vip" });
    }

    // True while the time saved under key is less than seconds ago. A missing, unreadable or
    // future time returns false, so the sound still plays.
    private bool OnCooldown(string key, int seconds)
    {
        if (seconds <= 0) return false;

        long ticks;
        if (!long.TryParse(Global(key), out ticks)) return false;

        TimeSpan since = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
        if (since.TotalSeconds < 0) return false;
        return since.TotalSeconds < seconds;
    }

    // ---- Streamer.bot plumbing ------------------------------------------------------------

    // The command that fired, e.g. "!oof" -> "oof". A Chat Message trigger has no command
    // argument, so the first word of the message stands in for it.
    private string Invoked()
    {
        string invoked = Arg(new[] { "command", "message" }, "").Trim().TrimStart('!');
        int space = invoked.IndexOf(' ');
        if (space >= 0) invoked = invoked.Substring(0, space);
        return invoked.ToLowerInvariant();
    }

    // Explicit <T> required: this SB build only exposes GetGlobalVar<T>(string, bool).
    private string Global(string key)
    {
        string value = CPH.GetGlobalVar<string>(key, true);
        return value == null ? "" : value.Trim();
    }

    private bool Flag(string[] keys)
    {
        foreach (string key in keys)
        {
            if (!args.ContainsKey(key) || args[key] == null) continue;
            string value = Convert.ToString(args[key]);
            if (value.Equals("True", StringComparison.OrdinalIgnoreCase) || value == "1") return true;
        }
        return false;
    }

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
