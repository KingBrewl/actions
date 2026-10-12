using System;
using System.Collections.Generic;

// Trigger: Twitch -> Command, with "!discord", "!social", "!socials", "!youtube" and "!yt" all
// in the command box — one action serves every link command, the way !death and !deaths share
// death.cs. Chat-only.
//
//   !discord             -> the Discord invite
//   !socials / !social   -> every social, one line
//   !youtube / !yt       -> the YouTube channel
//
// Each link is written once, below, and every message that mentions it is built from that —
// so a new Discord invite or a changed handle is a one-line edit.
public class CPHInline
{
    // ---- the links ------------------------------------------------------------------------

    private const string Discord = "https://discord.gg/WVffXrJt8h";
    private const string YouTube = "youtube.com/@KingBrewl";
    private const string TikTok = "tiktok.com/@KingBrewl";
    private const string X = "x.com/KingBrewl";
    private const string Instagram = "instagram.com/KingBrewl";

    // ---- the messages ---------------------------------------------------------------------

    private static readonly string DiscordLine =
        "Pull up a stool at The Brewl Tavern! " + Discord + " kingbre3Crown";

    private static readonly string SocialsLine =
        YouTube + " | " + TikTok + " | " + X + " | " + Instagram;

    private static readonly string YouTubeLine =
        "Vault the archives! Catch game replays, high-scoring highlights, and full VODs over on YouTube at https://" + YouTube + " kingbre3Coin";

    // Which message each command gets. Adding a command name here also needs it added to the
    // trigger's command box in Streamer.bot.
    private static readonly Dictionary<string, string> Commands = new Dictionary<string, string>
    {
        ["discord"] = DiscordLine,
        ["social"] = SocialsLine,
        ["socials"] = SocialsLine,
        ["youtube"] = YouTubeLine,
        ["yt"] = YouTubeLine
    };

    public bool Execute()
    {
        // The command that fired, e.g. "!yt" -> "yt". A Chat Message trigger has no command
        // argument, so the first word of the message stands in for it.
        string invoked = Arg(new[] { "command", "message" }, "").Trim().TrimStart('!');
        int space = invoked.IndexOf(' ');
        if (space >= 0) invoked = invoked.Substring(0, space);

        string line;
        if (!Commands.TryGetValue(invoked.ToLowerInvariant(), out line))
        {
            // Attached to a command name that isn't in the table: the socials line covers
            // every link but Discord, which is the most useful wrong answer available.
            CPH.LogWarn("[links] no message for \"" + invoked + "\" - add it to Commands");
            line = SocialsLine;
        }

        CPH.SendMessage(line);
        return true;
    }

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
