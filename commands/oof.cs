using System;
using System.IO;

// Trigger: Twitch -> Command "!oof". Plays the oof sound on stream.
//
//   !oof   -> plays SoundFile through Streamer.bot's audio output
//
// Mods, VIPs and the broadcaster can oof as often as they like. Everyone else shares one
// cooldown: after a viewer's oof, the next viewer oof is ignored, silently, until
// CooldownSeconds have passed. A mod or VIP oof never starts or resets that cooldown, so mods
// can't accidentally lock chat out of it.
//
// The sound file isn't included in this repo. Save your own copy (an .mp3 or .wav) somewhere
// on this PC and put its full path in SoundFile below.
public class CPHInline
{
    // Full path to the sound. Use @"..." so the backslashes don't need doubling.
    private const string SoundFile = @"C:\Stream\Sounds\oof.mp3";

    // 0.0 is silent and 1.0 is full volume. Turn it down if the oof is louder than your mic.
    private const float Volume = 0.6f;

    // How long viewers wait between oofs. Mods and VIPs skip it.
    private const int CooldownSeconds = 60;

    // Persisted, so restarting Streamer.bot doesn't reset the cooldown.
    private const string LastAtGlobal = "oof.lastAt";

    public bool Execute()
    {
        if (!File.Exists(SoundFile))
        {
            CPH.LogWarn("[oof] Sound file not found: " + SoundFile + " (set SoundFile at the top of oof.cs)");
            return true;
        }

        bool exempt = Exempt();
        if (!exempt && OnCooldown()) return true;

        CPH.PlaySound(SoundFile, Volume, false);

        if (!exempt) CPH.SetGlobalVar(LastAtGlobal, DateTime.UtcNow.Ticks.ToString(), true);
        return true;
    }

    // Mods, VIPs and the broadcaster skip the cooldown. A Hot Key or Test Trigger has no user,
    // so that's you, and you skip it too.
    private bool Exempt()
    {
        bool fromChat = args.ContainsKey("userName") || args.ContainsKey("user");
        if (!fromChat) return true;
        return Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator", "isVip", "vip" });
    }

    // True while the last viewer oof is still recent. If the saved time can't be read, or is
    // in the future, this returns false so the command still works.
    private bool OnCooldown()
    {
        long ticks;
        if (!long.TryParse(Global(LastAtGlobal), out ticks)) return false;

        TimeSpan since = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
        if (since.TotalSeconds < 0) return false;
        return since.TotalSeconds < CooldownSeconds;
    }

    // ---- Streamer.bot plumbing ------------------------------------------------------------

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
}
