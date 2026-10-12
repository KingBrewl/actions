using System;
using System.Collections.Generic;

// Trigger: Twitch -> Command, with "!commands", "!cmds" and "!cmd" in the command box. Chat-only.
//
//   !commands   -> every command the asker can use, one line
//
// Streamer.bot can't reliably list its own commands to an action, so the list below is
// written by hand — add a command here when you add one to the repo. Each person sees only
// what they can use: a viewer isn't shown commands that would just tell them no, VIPs also see
// what VIPs can do, and mods see everything.
public class CPHInline
{
    // Who can use a command. Each level sees its own commands and everything below it.
    private const int Everyone = 0;
    private const int Vips = 1;
    private const int Mods = 2;

    // In the order chat sees them. A command with aliases is listed once, under the name
    // chat should remember; the others still work.
    private static readonly object[][] Entries =
    {
        // ---- everyone -------------------------------------------------------------------
        new object[] { "!discord", Everyone },
        new object[] { "!socials", Everyone },
        new object[] { "!youtube", Everyone },
        new object[] { "!lurk", Everyone },
        new object[] { "!unlurk", Everyone },
        new object[] { "!hug", Everyone },
        new object[] { "!quote", Everyone },
        new object[] { "!clip", Everyone },
        new object[] { "!bad", Everyone },
        new object[] { "!game", Everyone },
        new object[] { "!deaths", Everyone },
        new object[] { "!letsgo", Everyone },
        new object[] { "!raid", Everyone },

        // ---- mods and VIPs --------------------------------------------------------------
        new object[] { "!death", Vips },
        new object[] { "!so", Vips }
    };

    // The commands with a help of their own, pointed at on the end of the line.
    private const string Footer = "!death help and !letsgo help explain more";

    // Twitch cuts a chat message off at 500 characters. A list longer than this is sent as more
    // than one message rather than losing its tail.
    private const int MaxMessageLength = 480;

    public bool Execute()
    {
        int level = Level();

        var everyone = Names(Everyone, Everyone);
        var extra = Names(Vips, level);

        var parts = new List<string>();
        parts.Add("kingbre3LFG Commands: " + string.Join(" ", everyone.ToArray()));
        if (extra.Count > 0)
            parts.Add((level == Mods ? "Mods" : "VIPs") + ": " + string.Join(" ", extra.ToArray()));
        parts.Add(Footer);

        foreach (string message in Pack(parts))
            CPH.SendMessage(message);
        return true;
    }

    // The names whose level is between `from` and `to`, inclusive.
    private List<string> Names(int from, int to)
    {
        var names = new List<string>();
        foreach (object[] entry in Entries)
        {
            int level = (int)entry[1];
            if (level >= from && level <= to) names.Add((string)entry[0]);
        }
        return names;
    }

    // Joins the parts with " | ", starting a new message only where the next part won't fit.
    private List<string> Pack(List<string> parts)
    {
        var messages = new List<string>();
        string current = "";
        foreach (string part in parts)
        {
            string joined = current.Length == 0 ? part : current + " | " + part;
            if (joined.Length > MaxMessageLength && current.Length > 0)
            {
                messages.Add(current);
                current = part;
            }
            else current = joined;
        }
        if (current.Length > 0) messages.Add(current);
        return messages;
    }

    // A Hot Key or a test trigger carries no user — that's you, so you see everything.
    private int Level()
    {
        bool fromChat = args.ContainsKey("userName") || args.ContainsKey("user");
        if (!fromChat) return Mods;
        if (Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator" })) return Mods;
        if (Flag(new[] { "isVip", "vip" })) return Vips;
        return Everyone;
    }

    private bool Flag(string[] keys)
    {
        foreach (var key in keys)
        {
            if (!args.ContainsKey(key) || args[key] == null) continue;
            string value = Convert.ToString(args[key]);
            if (value.Equals("True", StringComparison.OrdinalIgnoreCase) || value == "1") return true;
        }
        return false;
    }
}
