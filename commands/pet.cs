using System;
using System.Collections.Generic;
using System.Reflection;

// Trigger: Twitch -> Command "!pet" (a Chat Message trigger works too). Chat-only — for
// giving another viewer some gentle head pats.
//
//   !pet @ponytailgaming   -> they get pet                      (case and @ don't matter)
//   !pet                   -> the tavern pets the asker, since they're the one asking for it
//   !pet <yourself>        -> the same as a bare !pet
//
// The target is held to what a Twitch login can look like and then looked up, exactly as in
// hug.cs: only a name Twitch knows is ever posted, spelled as Twitch spells it. Anything else
// gets a "couldn't find" that does not repeat what was typed.
public class CPHInline
{
    /* -----------------------------------------------------------------------------------
     * The lines. {user} is whoever typed it, {target} the one being pet. Add as many as
     * you like; one is picked at random each time.
     * ----------------------------------------------------------------------------------- */
    private static readonly string[] Lines =
    {
        "{user} gently pats {target} on the head. Good adventurer. The best adventurer. kingbre3Heart",
        "{user} slips off a gauntlet and gives {target} the softest head pats in the whole realm. kingbre3Heart",
        "{user} ruffles {target}'s hair and tells them they did a wonderful job today. You did, {target}. kingbre3Heart",
        "{user} gives {target} slow, loving head pats until every worry from the road melts away. kingbre3Heart",
        "{user} pats {target} on the head and slides them the last honey cake from the tavern kitchen. You've earned it. kingbre3Heart",
        "{user} reaches over and pets {target} like the precious tavern cat they are. Purring is allowed. kingbre3Heart",
        "{user} gives {target} a pat on the head and a proud nod. The whole tavern is glad you're here, {target}. kingbre3Heart"
    };

    private static readonly string[] SelfLines =
    {
        "The tavern keeper leans over the bar and gives {user} a warm pat on the head. You're doing great, hero. kingbre3Heart",
        "The tavern cat hops into {user}'s lap and headbutts their hand until they get the pets they deserve. kingbre3Heart",
        "Chat lines up to give {user} gentle head pats, one by one. Everyone deserves some love. kingbre3Heart"
    };

    private static readonly Random Rng = new Random();

    public bool Execute()
    {
        string user = Arg(new[] { "user", "userName" }, "someone");
        string login = Arg(new[] { "userName", "user" }, "").ToLowerInvariant();

        string target = Target();
        if (target == null)
        {
            CPH.SendMessage(user + ", couldn't find that adventurer to pet - try !pet @name");
            return true;
        }

        bool self = target.Length == 0 || target.ToLowerInvariant() == login || target.ToLowerInvariant() == user.ToLowerInvariant();
        string line = self ? Pick(SelfLines) : Pick(Lines);

        CPH.SendMessage(line.Replace("{user}", user).Replace("{target}", target));
        return true;
    }

    private string Pick(string[] options)
    {
        return options[Rng.Next(options.Length)];
    }

    /**
     * Who is being pet: "" for nobody named, null for a name that isn't a Twitch user.
     *
     * Streamer.bot fills targetUser itself when the first word is a user it recognises, and
     * that is taken as-is. Otherwise the word has to look like a login before it is looked up,
     * which is what keeps links, spaces and anything else chat can type out of the reply.
     */
    private string Target()
    {
        string known = Arg(new[] { "targetUser", "targetUserName" }, "");
        if (known.Length > 0) return known;

        string raw = Arg(new[] { "rawInput", "message" }, "").Trim();
        if (raw.StartsWith("!pet", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(4).Trim();

        string word = FirstWord(raw).TrimStart('@');
        if (word.Length == 0) return "";
        if (!LooksLikeLogin(word)) return null;

        object record = null;
        try { record = CPH.TwitchGetExtendedUserInfoByLogin(word); }
        catch (Exception error) { CPH.LogWarn("[pet] user lookup threw: " + error.Message); }
        if (record == null) return null;

        return Prop(record, new[] { "UserName", "DisplayName", "UserLogin", "Login" }, word);
    }

    // Twitch logins are 4-25 of [A-Za-z0-9_]; a few legacy accounts are shorter, hence 1.
    // Checked by hand because Regex lives in System.dll, which the inline compiler can't see.
    private bool LooksLikeLogin(string word)
    {
        if (word.Length < 1 || word.Length > 25) return false;
        foreach (char c in word)
            if (!(c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                return false;
        return true;
    }

    // ---- Streamer.bot plumbing ------------------------------------------------------------

    private string FirstWord(string raw)
    {
        var parts = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "" : parts[0];
    }

    // Property names on TwitchUserInfoEx have moved between Streamer.bot versions - see
    // shoutout.cs - so the display name is read by name with fallbacks.
    private string Prop(object source, string[] memberNames, string fallback)
    {
        if (source == null) return fallback;
        var type = source.GetType();
        foreach (var wanted in memberNames)
        {
            object value = null;
            var property = type.GetProperty(wanted, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null) { try { value = property.GetValue(source, null); } catch { } }
            else
            {
                var field = type.GetField(wanted, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null) { try { value = field.GetValue(source); } catch { } }
            }
            string text = value == null ? "" : Convert.ToString(value).Trim();
            if (text.Length > 0) return text;
        }
        return fallback;
    }

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
