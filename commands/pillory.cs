using System;
using System.Collections.Generic;
using System.Reflection;

// Trigger: Twitch -> Command "!pillory" (a Chat Message trigger works too). Chat-only, and
// purely for show — nobody is actually timed out.
//
//   !pillory @ponytailgaming   -> into the stocks, for a random crime and a random sentence
//   !pillory <yourself>        -> a self-confession instead
//   !pillory                   -> how to use it
//
// The target is held to what a Twitch login can look like and then looked up, exactly as in
// hug.cs: only a name Twitch knows is ever posted, spelled as Twitch spells it. The crime and
// the sentence come from the arrays below, never from chat, so nothing else a viewer types
// reaches the reply. Each target's trips to the stocks are counted in a persisted user variable.
public class CPHInline
{
    /* -----------------------------------------------------------------------------------
     * The bits. {user} is whoever typed it, {target} the one in the stocks, {crime} and
     * {sentence} are picked from the lists below. Keep them silly rather than mean — anyone
     * can aim this at anyone, and it goes out under your channel's name.
     * ----------------------------------------------------------------------------------- */
    private static readonly string[] Lines =
    {
        "{user} drags {target} to the town square and locks them in the pillory for {crime}! Sentence: {sentence}. Chat, ready your tomatoes. 🍅",
        "HEAR YE, HEAR YE! {target} stands accused of {crime}. {user} has seen enough - to the stocks with them, for {sentence}! 🍅",
        "The tavern falls silent as {user} points at {target}. The charge: {crime}. The punishment: the pillory, for {sentence}. 🍅",
        "By order of {user}, {target} is clapped in the stocks for {crime}. They shall remain there for {sentence}. Throw responsibly. 🍅",
        "{target} has been found guilty of {crime} in the court of {user}. Off to the pillory for {sentence}! 🍅",
        "The town crier rings the bell: {target}, for {crime}, is sentenced by {user} to {sentence} in the stocks. 🍅",
        "{user} files the paperwork. {target} - {crime}. Verdict: guilty. Sentence: {sentence} in the pillory. Next! 🍅",
        "Seize them! {user} has caught {target} red-handed in the act of {crime}. The pillory awaits, for {sentence}. 🍅"
    };

    private static readonly string[] Crimes =
    {
        "backseating",
        "lurking with intent",
        "a crime against the tavern's good name",
        "spoilers",
        "laughing at the death counter",
        "eating the last of the bread",
        "insulting a goose",
        "asking what game this is",
        "excessive emote usage",
        "being suspiciously wholesome",
        "pressing !bad on a perfectly fine play",
        "telling the streamer to just jump",
        "dodging the tavern tab",
        "letting the dragon in",
        "typing 'first' when they were not first",
        "selling cursed potions",
        "a bard performance so bad it was a war crime",
        "sleeping through the boss fight",
        "drinking the healing potions recreationally",
        "loitering near the castle with a ladder",
        "calling it 'a bit of a mid run'",
        "ignoring the fire safety rules of the hearth"
    };

    private static readonly string[] Sentences =
    {
        "one (1) minute",
        "three fortnights",
        "however long the tomatoes last",
        "until the next death",
        "the rest of the stream",
        "seven business days",
        "until somebody says 'let's go'",
        "the length of one very long bard song",
        "until the goose forgives them",
        "exactly as long as the streamer takes to read the next quest log",
        "a time and a half",
        "until the tavern runs out of rotten cabbages"
    };

    // A lone line for pillorying yourself - no crime picked, they've confessed to something.
    private const string SelfLine = "{user} walks into the town square, locks THEMSELVES in the pillory, and asks chat to throw gently. Respect the honesty. kingbre3PayRespects";

    private static readonly Random Rng = new Random();

    // A pile-on of five people pillorying at once is one bit, not five. Calls inside this
    // window are ignored, silently - the same rule !bad uses. 0 switches it off.
    private const int CooldownSeconds = 15;

    // Each target's trips to the stocks, so the line can say it's their 4th time.
    private const string CountVar = "pillory.count";
    private const bool ShowCount = true;

    private const string LastAtGlobal = "pillory.lastAt";

    public bool Execute()
    {
        string user = Arg(new[] { "user", "userName" }, "someone");
        string login = Arg(new[] { "userName", "user" }, "").ToLowerInvariant();

        string targetName, targetLogin;
        if (!Target(out targetName, out targetLogin))
        {
            CPH.SendMessage(user + ", couldn't find that villager - try !pillory @name");
            return true;
        }
        if (targetName.Length == 0)
        {
            CPH.SendMessage(user + ", the stocks need someone in them - try !pillory @name 🍅");
            return true;
        }

        if (OnCooldown()) return true;
        CPH.SetGlobalVar(LastAtGlobal, DateTime.UtcNow.Ticks.ToString(), true);

        bool self = targetLogin == login || targetName.ToLowerInvariant() == user.ToLowerInvariant();
        string line = self ? SelfLine : Pick(Lines);
        line = line
            .Replace("{user}", user)
            .Replace("{target}", targetName)
            .Replace("{crime}", Pick(Crimes))
            .Replace("{sentence}", Pick(Sentences));

        int count = Count(targetLogin);
        if (ShowCount && count > 1)
            line += " (" + targetName + "'s " + Ordinal(count) + " time in the stocks)";

        CPH.SendMessage(line);
        return true;
    }

    // An unreadable or backwards clock fails open - one extra bit costs nothing.
    private bool OnCooldown()
    {
        if (CooldownSeconds <= 0) return false;

        long ticks;
        if (!long.TryParse(Global(LastAtGlobal), out ticks)) return false;

        TimeSpan since = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
        if (since.TotalSeconds < 0) return false;
        return since.TotalSeconds < CooldownSeconds;
    }

    // Adds one to the target's tally and returns the new total, or 0 when there's no login
    // to keep it against.
    private int Count(string targetLogin)
    {
        if (targetLogin.Length == 0) return 0;

        int count;
        int.TryParse(Convert.ToString(CPH.GetTwitchUserVar<object>(targetLogin, CountVar, true)), out count);
        count++;
        CPH.SetTwitchUserVar(targetLogin, CountVar, count.ToString(), true);
        return count;
    }

    private string Ordinal(int n)
    {
        int lastTwo = n % 100;
        if (lastTwo >= 11 && lastTwo <= 13) return n + "th";
        switch (n % 10)
        {
            case 1: return n + "st";
            case 2: return n + "nd";
            case 3: return n + "rd";
            default: return n + "th";
        }
    }

    private string Pick(string[] options)
    {
        return options[Rng.Next(options.Length)];
    }

    /**
     * Who is going in the stocks. False for a name that isn't a Twitch user; true with an
     * empty name for nobody named.
     *
     * Streamer.bot fills targetUser itself when the first word is a user it recognises, and
     * that is taken as-is. Otherwise the word has to look like a login before it is looked up,
     * which is what keeps links, spaces and anything else chat can type out of the reply.
     */
    private bool Target(out string name, out string login)
    {
        name = Arg(new[] { "targetUser", "targetUserName" }, "");
        login = Arg(new[] { "targetUserName", "targetUser" }, "").ToLowerInvariant();
        if (name.Length > 0) return true;

        string raw = Arg(new[] { "rawInput", "message" }, "").Trim();
        if (raw.StartsWith("!pillory", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(8).Trim();

        string word = FirstWord(raw).TrimStart('@');
        if (word.Length == 0) return true;
        if (!LooksLikeLogin(word)) return false;

        object record = null;
        try { record = CPH.TwitchGetExtendedUserInfoByLogin(word); }
        catch (Exception error) { CPH.LogWarn("[pillory] user lookup threw: " + error.Message); }
        if (record == null) return false;

        name = Prop(record, new[] { "UserName", "DisplayName", "UserLogin", "Login" }, word);
        login = Prop(record, new[] { "UserLogin", "Login" }, word).ToLowerInvariant();
        return true;
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
    // shoutout.cs - so they are read by name with fallbacks.
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

    // Explicit <T> required: this SB build only exposes GetGlobalVar<T>(string, bool).
    private string Global(string key)
    {
        string value = CPH.GetGlobalVar<string>(key, true);
        return value == null ? "" : value.Trim();
    }

    private string Arg(string[] keys, string fallback)
    {
        foreach (var key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
