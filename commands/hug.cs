using System;
using System.Collections.Generic;
using System.Reflection;

// Trigger: Twitch -> Command "!hug" (a Chat Message trigger works too). Chat-only.
//
//   !hug @ponytailgaming   -> the armor hug, aimed at them          (case and @ don't matter)
//   !hug                   -> the whole tavern gets one
//   !hug <yourself>        -> a self-hug, rather than "X pulls X close"
//
// The target is the one piece of viewer text that reaches chat, so it is held to what a
// Twitch login can look like (letters, digits, underscore) and then looked up: only a name
// Twitch knows is ever posted, and it is posted as Twitch spells it. Anything else gets a
// "couldn't find" that does not repeat what was typed.
public class CPHInline
{
    public bool Execute()
    {
        string user = Arg(new[] { "user", "userName" }, "someone");
        string login = Arg(new[] { "userName", "user" }, "").ToLowerInvariant();

        string target = Target();
        if (target == null)
        {
            CPH.SendMessage(user + ", couldn't find that adventurer - try !hug @name");
            return true;
        }

        if (target.Length == 0)
            CPH.SendMessage(user + " throws open their arms and pulls the whole tavern into a heavy, armor-clanking group hug! May your health bars stay full and your shields hold strong. kingbre3Heart");
        else if (target.ToLowerInvariant() == login || target.ToLowerInvariant() == user.ToLowerInvariant())
            CPH.SendMessage(user + " wraps themselves in a heavy, armor-clanking hug. Even heroes need one sometimes - may your health bar stay full and your shield hold strong. kingbre3Heart");
        else
            CPH.SendMessage(user + " pulls " + target + " close into a heavy, tavern-side armor hug! May your health bars stay full and your shields hold strong. kingbre3Heart");

        return true;
    }

    /**
     * Who is being hugged: "" for nobody named, null for a name that isn't a Twitch user.
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
        if (raw.StartsWith("!hug", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(4).Trim();

        string word = FirstWord(raw).TrimStart('@');
        if (word.Length == 0) return "";
        if (!LooksLikeLogin(word)) return null;

        object record = null;
        try { record = CPH.TwitchGetExtendedUserInfoByLogin(word); }
        catch (Exception error) { CPH.LogWarn("[hug] user lookup threw: " + error.Message); }
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
