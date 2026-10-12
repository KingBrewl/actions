using System;
using System.Collections.Generic;

// Trigger: Twitch -> Command "!raid". Chat-only — posts the raid call twice, for chat to copy
// and paste into the channel being raided:
//
//   1. with channel emotes, for subs
//   2. with emoji, which anyone can paste anywhere
//
// Two versions because channel emotes only work in someone else's chat for subscribers: a
// non-sub pasting the emote version posts the literal word "kingbre3Crown". Each line is just
// the message with no label in front, so the whole chat line is the thing to copy — the emotes
// rendering in one of them is what tells the two apart.
public class CPHInline
{
    // Off: anyone can post it. On if chat starts posting it when there's no raid.
    private const bool ModsOnly = false;

    private const string SubRaidCall = "kingbre3Crown The tavern doors burst open and the Brewl Kingdom rides in! Tankards up, we come bearing gold and good cheer! kingbre3Coin KINGBREWL RAID kingbre3Coin";
    private const string RaidCall = "👑 The tavern doors burst open and the Brewl Kingdom rides in! Tankards up, we come bearing gold and good cheer! 🍺 KINGBREWL RAID 🍺";

    public bool Execute()
    {
        if (ModsOnly && !Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator" }))
        {
            CPH.SendMessage(Arg(new[] { "user", "userName" }, "you") + ", only mods can use !raid");
            return true;
        }

        CPH.SendMessage(SubRaidCall);
        CPH.SendMessage(RaidCall);
        return true;
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

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
