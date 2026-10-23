using System;
using System.Collections.Generic;
using System.Reflection;

// Trigger: Twitch -> Command "!tuck" (a Chat Message trigger works too). Chat-only — for
// sending a viewer off to bed.
//
//   !tuck @ponytailgaming   -> they get tucked in               (case and @ don't matter)
//   !tuck                   -> the tavern tucks the asker in, since they're the one off to bed
//   !tuck <yourself>        -> the same as a bare !tuck
//
// The target is held to what a Twitch login can look like and then looked up, exactly as in
// hug.cs: only a name Twitch knows is ever posted, spelled as Twitch spells it. Anything else
// gets a "couldn't find" that does not repeat what was typed.
public class CPHInline
{
    /* -----------------------------------------------------------------------------------
     * The lines. {user} is whoever typed it, {target} the one going to bed. Add as many as
     * you like; one is picked at random each time.
     * ----------------------------------------------------------------------------------- */
    private static readonly string[] Lines =
    {
        "{user} tucks {target} into a straw bed by the tavern hearth and blows out the candle. Sleep well, hero. kingbre3Heart",
        "{user} pulls a thick wool blanket up to {target}'s chin and props their sword by the door. Rest easy - the night watch has it from here. kingbre3Heart",
        "{user} fluffs {target}'s pillow, leaves a mug of warm milk on the nightstand and tiptoes out. Goodnight, {target}! kingbre3Heart",
        "{user} tucks {target} in tight and checks under the bed for goblins. All clear. Sweet dreams! kingbre3Heart",
        "{user} wraps {target} up like a tavern burrito and hums the bard's lullaby. Off to dreamland with you. kingbre3Heart",
        "{user} sets {target}'s armor on the stand, tucks them in and promises to save them a seat by the fire tomorrow. Goodnight! kingbre3Heart"
    };

    private static readonly string[] SelfLines =
    {
        "The tavern keeper tucks {user} into the warmest bed upstairs and dims the lanterns. Sleep well, hero - we'll keep the fire going. kingbre3Heart",
        "Chat pulls the blankets up over {user} and the bard plays something soft. Goodnight, {user}! kingbre3Heart",
        "{user} heads up the creaky stairs to bed. The whole tavern waves goodnight - rest up for the next adventure! kingbre3Heart"
    };

    private static readonly Random Rng = new Random();

    public bool Execute()
    {
        string user = Arg(new[] { "user", "userName" }, "someone");
        string login = Arg(new[] { "userName", "user" }, "").ToLowerInvariant();

        string target = Target();
        if (target == null)
        {
            CPH.SendMessage(user + ", couldn't find that sleepy adventurer - try !tuck @name");
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
     * Who is being tucked in: "" for nobody named, null for a name that isn't a Twitch user.
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
        if (raw.StartsWith("!tuck", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(5).Trim();

        string word = FirstWord(raw).TrimStart('@');
        if (word.Length == 0) return "";
        if (!LooksLikeLogin(word)) return null;

        object record = null;
        try { record = CPH.TwitchGetExtendedUserInfoByLogin(word); }
        catch (Exception error) { CPH.LogWarn("[tuck] user lookup threw: " + error.Message); }
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
