using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;

// Trigger: Twitch -> Command "!bad" (a Chat Message trigger works too). Chat-only by default —
// a viewer watching you play badly presses one button and the bot says the quiet part.
//
//   !bad   -> one roast, picked at random, aimed at the broadcaster and nobody else
//
// Anything typed after !bad is ignored outright — nothing a viewer writes is ever echoed, so
// this cannot be pointed at a person in chat. The line is picked from Roasts, which is the
// whole configurable surface of this action: edit the array, paste the action back in, done.
//
// Four placeholders are filled in when they can be: {streamer}, {user} (whoever typed it),
// {game} (the category the channel is set to right now) and {deaths} (the counter from
// death.cs). A line is only ever picked when every placeholder it uses has a value, so
// {game} lines sit out when the category is unknown and {deaths} lines sit out until there is
// a run in progress — nothing is ever posted with a hole in it.
public class CPHInline
{
    /* -----------------------------------------------------------------------------------
     * The roasts. Affectionate, PG, and aimed at the play rather than the player — this goes
     * out under your channel's name, so a line Twitch would object to is your problem and not
     * chat's. Add as many as you like; the no-repeat memory below scales with the list.
     * ----------------------------------------------------------------------------------- */
    private static readonly string[] Roasts =
    {
        // ---- always eligible ----------------------------------------------------------
        "{user} says you're playing badly. The scoreboard says {user} is being polite.",
        "Chat has watched you do this before, {streamer}. Chat has never watched it work.",
        "That's not a strategy, {streamer}, that's a series of accidents in a trench coat.",
        "Somewhere a tutorial level is watching this and feeling very good about itself.",
        "{user} pressed !bad, which is the politest available summary of the last minute.",
        "Bold of you to keep doing the thing that has never once worked.",
        "Every viewer just yelled the answer at their screen. Every single one.",
        "That wasn't a mistake, {streamer}, that's a signature move at this point.",
        "The enemies have stopped trying, {streamer}. They feel bad for you.",
        "Remember when we thought this run was going to be the good one? Simpler times.",
        "You had one job, and there was a prompt on screen telling you what it was.",
        "Positive spin: nobody could accuse you of overthinking that one.",
        "{user} would like it on record that they could do better. {user} is probably right.",
        "Genuinely impressive. Not the gameplay - the confidence.",
        "Nobody's mad, {streamer}. Everyone is just deeply, deeply confused.",
        "I'd say unlucky, but luck stopped being involved about four minutes ago.",
        "Hard to blame the lag, given the enemy was standing completely still.",
        "Chat isn't laughing at you, {streamer}. Chat is laughing near you.",
        "Let me check the settings - no, difficulty's on normal. Wild.",
        "The good news is that it can't get worse. I said that last time too.",
        "{streamer} is speedrunning the part where you learn nothing.",
        "I've seen better decision-making from a screensaver.",
        "Hey {streamer}, have you considered turning the monitor on?",
        "That was so bad the anti-cheat filed a complaint about the opposite problem.",
        "{user} has seen enough. {user} has, frankly, seen too much.",
        "This is the part where a normal person would try something different.",
        "The plan was fine. The execution filed for divorce from the plan.",
        "{user} is not wrong, and everyone here knows {user} is not wrong.",
        "That's twice now. The first one was funny.",
        "{streamer} has invented a new way to lose. Genuinely novel. Terrible, but novel.",
        "There's a word for that, and the word isn't clutch.",
        "I want to defend you here, {streamer}, and I have absolutely nothing.",
        "The tutorial pop-up came back purely out of concern.",
        "{streamer} looked directly at it and then confidently did the other thing.",
        "That was going so well for almost four entire seconds.",
        "New viewers: it isn't usually like this. Regulars: it's usually like this.",
        "You know the thing you always do? You've done it again.",
        "At some point unlucky becomes a pattern, and we sailed past that a while back.",
        "{user} typed !bad before it had even finished happening. That's experience.",
        "The good option is right there. Has been for minutes. Still is.",
        "Technically still alive, which is the nicest available thing to say about that.",
        "Hands: present. Brain: reportedly on its way.",
        "That was a choice. It was made. We all watched it get made.",
        "Honestly, the confidence going in was the funniest part.",
        "{streamer} doesn't need backseating, {streamer} needs an intervention.",
        "Nobody move. Let's all just sit quietly with what we witnessed.",
        "The enemy AI had to invent a brand new emotion for that one.",
        "{user} requested a roast. {streamer} had already provided one.",
        "If you're new here, this is the skill ceiling, and we are underneath it.",
        "Statistically, doing nothing at all would have gone better.",
        "{streamer} out here making the easy part look load-bearing.",
        "Two things can be true: you're having fun, and that was dreadful.",
        "That's not a skill issue, that's a skill absence.",
        "Apologise to the controller. The controller did nothing wrong.",
        "Somebody type !clip before {streamer} recovers and pretends that didn't happen.",
        "Rewatch that one later, {streamer}. Actually, don't.",
        "Reflexes of a man calmly reading the news.",
        "Chat would like to offer advice. Chat would like to offer it loudly.",
        "There was a correct button nearby. You two have met before.",
        "Somewhere in this room is a strategy, and it is hiding from you.",
        "That's the kind of thing you'd get told off for in a tutorial.",
        "Not the worst I've seen from you, which is its own kind of insult.",
        "{user} has moved from viewer to witness.",
        "Incredible commitment to a bit nobody asked you to do.",

        // ---- need a category ({game}) --------------------------------------------------
        "{streamer} is making {game} look like the hardest thing ever invented.",
        "{game} did not do anything to deserve this.",
        "Somewhere the {game} devs are watching this and quietly editing their CVs.",
        "There are people who have never opened {game} doing better than this right now.",
        "{game} shipped with a tutorial, {streamer}. It's right there in the menu.",
        "{user} would like to remind everyone that {streamer} has played {game} before. Allegedly.",
        "{game} is a game millions of people have finished, {streamer}. Millions.",
        "Whatever {game} was going for, this is not it.",
        "{streamer} isn't bad at {game}, {streamer} is bad at {game} in a very specific and repeatable way.",
        "Quick poll, chat: has anyone here seen {streamer} do that correctly in {game}? Anyone?",
        "{game} has a difficulty slider, and the slider is not the problem.",
        "Speedrunners clear this bit of {game} in four seconds. We're on minute three.",
        "{user} came here to watch {game} and is instead watching a cautionary tale.",
        "The {game} wiki has a page on this. It's short. It says don't.",
        "Somewhere a child is finishing {game} blind, on their first attempt.",
        "{game} did not need to be this hard, and yet.",
        "If {game} had a tutorial for this part, it would be shorter than this attempt.",
        "Refund {game}? No. Refund {streamer}.",

        // ---- need a live death counter ({deaths}) --------------------------------------
        "{deaths} deaths in, and the strategy is still the same thing but slightly worse.",
        "Reminder: the counter says {deaths}. The counter has no reason to lie.",
        "At {deaths} deaths the respawn screen basically counts as the main menu.",
        "{deaths} deaths deep, so {user} is well within their rights to call this bad.",
        "{deaths} deaths in {game}. At some point that stops being the game's fault.",
        "{deaths} and climbing. The counter is the only thing going up tonight.",
        "{deaths} deaths, and it's bold of you to still be calling this a run.",
        "That's {deaths}. Chat has quietly started betting on {deaths} plus ten.",
        "{deaths} deaths and the same plan throughout. That's commitment, or something worse.",
        "The counter says {deaths}, and it has never once been wrong about you.",
        "{deaths} deaths, {streamer}. At what point do we rename the stream after it?",
        "{deaths} deaths in and the enemy still hasn't learned your name. It doesn't need to.",
        "{user} has now watched {deaths} deaths and is legally a witness.",
        "{deaths} deaths. Somewhere a difficulty setting is waving at you.",
        "{deaths} deaths in {game}, which the {game} devs did not design for."
    };

    // Static, because a fresh Random is seeded off the clock: two built inside the same tick
    // hand back the same number, and the roasts then march round in a fixed order.
    private static readonly Random Rng = new Random();

    // Off: the whole point is that a viewer can call it. On makes this a mod-only button.
    private const bool ModsOnly = false;

    // Five people typing !bad at the same bad play are five people agreeing, not five bad
    // plays - so the ones after the first are ignored, silently. See the README.
    private const int CooldownSeconds = 45;

    private const bool PostChatMessage = true;

    // An overlay card as well as the chat line (src/alerts/). Off - a roast belongs in chat,
    // where the person being roasted is already looking.
    private const bool ShowOverlayCard = false;
    private const int AlertMs = 7000;

    // How many of the most recent roasts are off the table when picking the next one. Keeps a
    // finite list from feeling finite. A value at or above the number of *eligible* lines would
    // leave nothing to pick, which Pick handles by forgetting and starting over — so the number
    // to stay under is the count of lines with no placeholder in them (currently 64), not the
    // length of the array, since a stream with no category and no death counter sees only those.
    private const int NoRepeatMemory = 20;

    // Written by death.cs. Empty disables the {deaths} lines entirely.
    private const string DeathCountVar = "death.count";

    // Persisted, so a Streamer.bot restart mid-stream doesn't reopen the cooldown or forget
    // which roasts just went out.
    private const string LastAtGlobal = "bad.lastAt";
    private const string RecentGlobal = "bad.recent";

    public bool Execute()
    {
        string user = Arg(new[] { "userName", "user" }, "someone");

        if (ModsOnly && !Flag(new[] { "isBroadcaster", "broadcaster", "isModerator", "moderator" }))
        {
            CPH.SendMessage(user + ", only mods can use !bad");
            return true;
        }

        if (OnCooldown()) return true;

        string streamer = Arg(new[] { "broadcastUserName", "broadcastUser", "channel" }, "the streamer");
        string line = Pick(streamer, user, CurrentGame(), DeathCount());
        if (line.Length == 0) return true;

        CPH.SetGlobalVar(LastAtGlobal, DateTime.UtcNow.Ticks.ToString(), true);

        if (PostChatMessage) CPH.SendMessage(line);
        if (ShowOverlayCard) Broadcast(line);

        return true;
    }

    // True while the last roast is still recent. An unreadable or backwards clock fails *open*
    // - one extra roast costs nothing, and a stuck cooldown kills the command silently.
    private bool OnCooldown()
    {
        long ticks;
        if (!long.TryParse(Global(LastAtGlobal), out ticks)) return false;

        TimeSpan since = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
        if (since.TotalSeconds < 0) return false;
        return since.TotalSeconds < CooldownSeconds;
    }

    /**
     * One roast, filled in, and not one of the last NoRepeatMemory.
     *
     * A line is eligible only if every placeholder it uses has a value, which is what keeps
     * "{game} did not deserve this" off the screen when nothing knows what the game is.
     * Eligibility is decided on the raw line before substitution, so a category that genuinely
     * looks like an empty value can't sneak a half-filled line through.
     */
    private string Pick(string streamer, string user, string game, int deaths)
    {
        var eligible = new List<string>();
        foreach (string roast in Roasts)
        {
            if (roast.Contains("{game}") && game.Length == 0) continue;
            if (roast.Contains("{deaths}") && deaths <= 0) continue;
            eligible.Add(roast);
        }
        if (eligible.Count == 0) return "";

        var recent = Recent();
        var fresh = new List<string>();
        foreach (string roast in eligible)
            if (!recent.Contains(Hash(roast))) fresh.Add(roast);

        // Everything eligible has been said lately - forget the memory rather than deliberately
        // repeat the oldest, which is the same decision one roast earlier.
        if (fresh.Count == 0) { fresh = eligible; recent.Clear(); }

        string chosen = fresh[Rng.Next(fresh.Count)];
        Remember(recent, Hash(chosen));

        return chosen
            .Replace("{streamer}", streamer)
            .Replace("{user}", user)
            .Replace("{game}", game)
            .Replace("{deaths}", deaths.ToString());
    }

    // The recent picks, as hashes of the line rather than indexes into Roasts: editing the
    // array then can't leave a stale number pointing at some innocent bystander of a roast.
    private List<string> Recent()
    {
        var recent = new List<string>();
        foreach (string part in Global(RecentGlobal).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            recent.Add(part.Trim());
        return recent;
    }

    private void Remember(List<string> recent, string hash)
    {
        recent.Add(hash);
        while (recent.Count > NoRepeatMemory) recent.RemoveAt(0);
        CPH.SetGlobalVar(RecentGlobal, string.Join(",", recent.ToArray()), true);
    }

    // FNV-1a. String.GetHashCode is not promised to be stable between processes, and this one
    // is written to a persisted global that outlives the process. unchecked because the
    // multiply is meant to wrap, and Streamer.bot compiles with overflow checking on - without
    // it the first roast throws OverflowException and nothing is posted.
    private string Hash(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash.ToString("x8");
        }
    }

    /**
     * The category the channel is set to right now, or "" if this build won't say.
     *
     * Found by shape rather than by name, for the reason shoutout.cs finds it that way:
     * Streamer.bot hands CPH out as an interface and has renamed both the channel-info getter
     * and its category property across versions, so any single-string method that looks like a
     * channel-info getter is tried, then any property on the result that looks like a category.
     *
     * Nothing that looks like a mutator is ever invoked - a single-string method with "game" in
     * the name could just as easily be the one that *changes* your category live on stream.
     */
    private string CurrentGame()
    {
        string login = Arg(new[] { "broadcastUserName", "broadcastUser", "channel" }, "");
        if (login.Length == 0) return "";

        object info = Invoke("channelinfo", "login", login);
        if (info == null) info = Invoke("channelinfo", null, login);
        if (info == null) return "";

        return Category(info);
    }

    private string Category(object source)
    {
        foreach (var member in Members(source))
        {
            string name = member.Key.ToLowerInvariant();
            if (name != "gamename" && name != "game" && name != "categoryname" && name != "category") continue;
            if (member.Value == null) continue;

            // Some builds box the category as an object with a Name rather than flattening it.
            string text = member.Value is string
                ? Text(member.Value)
                : Prop(member.Value, new[] { "Name", "GameName", "DisplayName", "Title" }, "");
            if (text.Length > 0) return text;
        }
        return "";
    }

    // The live count from death.cs, or 0 when that action isn't imported. Written by it
    // as a string; read as <object> so an older boxed int still parses.
    private int DeathCount()
    {
        if (DeathCountVar.Length == 0) return 0;
        int count;
        int.TryParse(Convert.ToString(CPH.GetGlobalVar<object>(DeathCountVar, true)), out count);
        return count;
    }

    private void Broadcast(string line)
    {
        var payload = new Dictionary<string, object>
        {
            ["cmd"] = "alert",
            ["id"] = "bad",
            ["title"] = "Chat has notes",
            ["body"] = line,
            ["icon"] = "🔥",
            ["ms"] = AlertMs
        };
        CPH.WebsocketBroadcastJson(JsonConvert.SerializeObject(payload));
    }

    // ---- Streamer.bot plumbing ------------------------------------------------------------

    private static readonly string[] Mutators = { "set", "update", "create", "add", "delete", "remove", "ban", "timeout", "clear", "send", "start", "stop", "run", "raid" };

    // `prefer` disambiguates the ByLogin/ById pair, which take the same shape and quietly
    // return nothing when handed the other one's argument. Pass null to try either.
    private object Invoke(string nameContains, string prefer, string argument)
    {
        var type = CPH.GetType();
        var methods = new List<MethodInfo>(type.GetMethods());
        foreach (var contract in type.GetInterfaces()) methods.AddRange(contract.GetMethods());

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string)) continue;

            string name = method.Name.ToLowerInvariant();
            if (!name.Contains(nameContains)) continue;
            if (prefer != null && !name.Contains(prefer)) continue;

            bool mutates = false;
            foreach (string word in Mutators) if (name.Contains(word)) { mutates = true; break; }
            if (mutates) continue;

            try
            {
                object result = method.Invoke(CPH, new object[] { argument });
                if (result != null) return result;
            }
            catch (Exception error)
            {
                CPH.LogWarn("[bad] " + method.Name + " threw: " + (error.InnerException ?? error).Message);
            }
        }
        return null;
    }

    // Streamer.bot's records are not consistent about properties versus fields between
    // versions, and a walk that only knows about properties sees a field-based record as empty.
    private List<KeyValuePair<string, object>> Members(object source)
    {
        var members = new List<KeyValuePair<string, object>>();
        if (source == null) return members;

        var type = source.GetType();
        foreach (var property in type.GetProperties())
        {
            object value = null;
            try { value = property.GetValue(source, null); }
            catch { }
            members.Add(new KeyValuePair<string, object>(property.Name, value));
        }
        foreach (var field in type.GetFields())
        {
            object value = null;
            try { value = field.GetValue(source); }
            catch { }
            members.Add(new KeyValuePair<string, object>(field.Name, value));
        }
        return members;
    }

    private string Prop(object source, string[] memberNames, string fallback)
    {
        var members = Members(source);
        foreach (string wanted in memberNames)
            foreach (var member in members)
            {
                if (!member.Key.Equals(wanted, StringComparison.OrdinalIgnoreCase)) continue;
                string text = Text(member.Value);
                if (text.Length > 0) return text;
            }
        return fallback;
    }

    private string Text(object value)
    {
        return value == null ? "" : Convert.ToString(value).Trim();
    }

    // Explicit <T> required: this SB build only exposes GetGlobalVar<T>(string, bool), so the
    // one-arg form cannot infer T.
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
        foreach (string key in keys)
            if (args.ContainsKey(key) && args[key] != null && !string.IsNullOrEmpty(args[key].ToString()))
                return args[key].ToString();
        return fallback;
    }
}
