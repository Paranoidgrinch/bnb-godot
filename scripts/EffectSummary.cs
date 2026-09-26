using System.Text.RegularExpressions;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace BnbGodot;

// WHAT A CHOICE DOES, IN PLAIN WORDS. An event's branches are written in the game's own voice ("Let it refile the
// application.") and the player asked to be told, beside the colour, what taking one will actually do (playtest
// 2026-09-26). Every branch carries its effects as data, so the sentence is READ OFF those effects rather than
// written a second time by hand: "+50 Gold · Transform a card · Next fight: you start with Act One Markings".
//
// It says what it can see and no more. An effect it cannot put into words (a conditional, a per-card program)
// becomes "something else happens" instead of a guess; bookkeeping the player never feels (a flag, an expiry
// program) is left out.
public static class EffectSummary
{
    public static string Of(EventChoice choice, RunPlayback? play)
    {
        var parts = new List<string>();
        foreach (var cost in choice.Costs ?? [])
            foreach (var pay in cost.Pay)
                if (Describe(pay, play, paying: true) is { } paid)
                    parts.Add(paid);
        foreach (var effect in choice.Effects)
            if (Describe(effect, play, paying: false) is { } said)
                parts.Add(said);
        return string.Join(" · ", parts.Distinct());
    }

    private static string? Describe(IRunEffectRequest effect, RunPlayback? play, bool paying)
    {
        switch (effect)
        {
            case ChangeResourceRunEffect change:
                var resource = Capital(play?.ResourceNames.GetValueOrDefault(change.Resource.Value) ?? change.Resource.Value);
                if (paying)
                    return $"Pay {Math.Abs(change.Delta)} {resource}";
                return change.Delta >= 0 ? $"+{change.Delta} {resource}" : $"−{-change.Delta} {resource}";
            case ComputedResourceRunEffect:
                return "Gain or lose Gold";
            case ApplyRunDamageRunEffect damage:
                return $"Lose {damage.Amount} HP";
            case ComputedDamageRunEffect computedDamage:
                return Share(computedDamage) is { } lost ? $"Lose {lost}" : "Lose HP";
            case HealRunEffect heal:
                return $"Heal {heal.Amount} HP";
            case ComputedHealRunEffect computedHeal:
                return Share(computedHeal) is { } healed ? $"Heal {healed}" : "Heal";
            case ChangeMaxHealthRunEffect max:
                return max.Delta >= 0 ? $"+{max.Delta} Max HP" : $"−{-max.Delta} Max HP";
            case AddCardToDeckRunEffect add:
                return $"Add {CardName(add.Card.value)} to your deck";
            case AddRelicByIdRunEffect relic:
                return $"Gain the relic {play?.RelicNames.GetValueOrDefault(relic.Relic.Value) ?? relic.Relic.Value}";
            case RemoveCardsRunEffect remove:
                return $"Remove {Cards(remove.Selector)} from your deck";
            case UpgradeCardsRunEffect upgrade:
                return $"Improve {Cards(upgrade.Selector)}";
            case TransformCardsRunEffect transform:
                return $"Transform {Cards(transform.Selector)} into a random card";
            case DuplicateCardsRunEffect duplicate:
                return $"Copy {Cards(duplicate.Selector)}";
            case RestoreRemovedCardRunEffect:
                return "Get back a card you removed";
            case OfferRewardRunEffect reward:
                return Reward(reward);
            case InstallNextCombatOpeningRunEffect opening:
                return Opening(opening);
            case ConditionalRunEffect:
            case ForEachCardRunEffect:
                return "Something else happens";
            default:
                // A flag, a program that expires what an opening installed, a tag on a card: the run keeping its
                // own books. Nothing a player would recognise as having happened.
                return null;
        }
    }

    private static string Capital(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];

    // The document writes "a share of your max HP" as (maxHealth × P + 99) / 100 — a rounded-up percentage. Said
    // as that percentage; a plain constant as its number; anything else is left to the caller's vaguer word.
    private static readonly Regex Percent = new(
        "\"kind\":\"multiply\",\"value\":\\{\"Left\":\\{\"kind\":\"maxHealth\",\"value\":\\{\\}\\},\"Right\":\\{\"kind\":\"const\",\"value\":\\{\"Value\":(\\d+)\\}",
        RegexOptions.Compiled);
    private static readonly Regex Constant = new(
        "^\\{\"Amount\":\\{\"kind\":\"const\",\"value\":\\{\"Value\":(\\d+)\\}\\}\\}$", RegexOptions.Compiled);

    private static string? Share(IRunEffectRequest effect)
    {
        string json;
        try
        {
            json = System.Text.Json.JsonSerializer.Serialize(effect, effect.GetType(), RunJson.CreateOptions(indented: false));
        }
        catch (Exception)
        {
            return null;
        }
        if (Percent.Match(json) is { Success: true } share)
            return $"{share.Groups[1].Value}% of Max HP";
        if (Constant.Match(json) is { Success: true } flat)
            return $"{flat.Groups[1].Value} HP";
        return null;
    }

    private static string CardName(string id) =>
        GameHost.Instance.Blueprint.Cards.FirstOrDefault(c => c.Id == id)?.NameKey is { Length: > 0 } name
            ? name
            : id;

    // "a card", "2 cards", "a random card", "every card" — read off the selector's outer shape.
    private static string Cards<T>(IRunSelector<T> selector) => selector switch
    {
        ChooseSelector<T> choose => choose.Count == 1 ? "a card" : $"{choose.Count} cards",
        RandomSelector<T> random => random.Count == 1 ? "a random card" : $"{random.Count} random cards",
        TakeSelector<T> take => take.Count == 1 ? "a card" : $"{take.Count} cards",
        _ => "cards",
    };

    private static string Reward(OfferRewardRunEffect reward)
    {
        var offers = reward.Source switch
        {
            FixedRewardSource fixedOffers => fixedOffers.Offers,
            PoolRewardSource pool => pool.Pool.Entries.Select(e => e.Value).ToList(),
            _ => [],
        };
        var shown = reward.Source is PoolRewardSource p ? p.Count : offers.Count;
        var relics = offers.Count > 0 && offers.All(o => o.Grant.Any(g => g is AddRelicByIdRunEffect));
        var cards = offers.Count > 0 && offers.All(o => o.Grant.Any(g => g is AddCardToDeckRunEffect));
        var what = relics ? "relic" : cards ? "card" : "reward";
        if (shown <= 1)
            return relics ? "Gain a random relic" : cards ? "Gain a random card" : "A reward";
        return reward.PickCount >= shown ? $"Gain {shown} {what}s" : $"Choose a {what} of {shown}";
    }

    // "At the next fight's first turn, apply Act One Markings ×1 to you" is a program; what the player needs is
    // the status it hands over, WHO gets it and how much, by the name the fight will show it under.
    private static readonly Regex AppliedStatus = new(
        "\"TargetSelector\":\\{\"kind\":\"sel\\.([A-Za-z]+)\"[^}]*\\}\\},\"StatusDefinitionId\":\\{\"value\":\"([^\"]+)\"\\}(?:,\"Stacks\":\\{\"kind\":\"const\",\"value\":\\{\"Value\":(\\d+)\\})?",
        RegexOptions.Compiled);

    private static string Opening(InstallNextCombatOpeningRunEffect opening)
    {
        string json;
        try
        {
            json = RunJson.ToJson(opening, RunJson.CreateOptions(indented: false));
        }
        catch (Exception)
        {
            return "Something happens in your next fight";
        }
        var said = AppliedStatus.Matches(json)
            .Select(m =>
            {
                var id = m.Groups[2].Value;
                var name = GameHost.Instance.Blueprint.Statuses.FirstOrDefault(s => s.Id == id)?.NameKey is { Length: > 0 } n
                    ? n : id;
                var stacks = m.Groups[3].Success && m.Groups[3].Value != "1" && m.Groups[3].Value != "0"
                    ? $" {m.Groups[3].Value}" : "";
                var who = m.Groups[1].Value switch
                {
                    "source" or "self" => "you",
                    "allEnemies" or "enemies" => "every enemy",
                    "randomEnemy" => "a random enemy",
                    _ => "",
                };
                return who.Length > 0 ? $"{name}{stacks} on {who}" : $"{name}{stacks}";
            })
            .Distinct()
            .ToList();
        return said.Count == 0
            ? "Something happens in your next fight"
            : $"Next fight: {string.Join(", ", said)}";
    }
}
