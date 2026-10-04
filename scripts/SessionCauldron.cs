using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// THE HERO'S OWN ACTIONS on the fight screen (Hedge Witch plan G2): her cauldron — three slots, what is in them,
// whether she is Sheltering, Brewing or Ready, and what the pot is about to make — beside a button for each
// action ("Into the pot", "Brew") with what it costs right now. A hero without actions (the Bureaucrat) gets none
// of this; the row is exactly what it was.
//
// "Into the pot" asks WHICH card through the fight's own card-choice prompt, the same one any card that says
// "choose a card" raises — so there is no second way of picking a card to learn.
public partial class SessionScreen
{
    private const string CauldronAction = "cauldron_add";
    private const string BrewAction = "cauldron_brew";
    private static readonly string[] FamilyTags = ["fam_fang", "fam_hex", "fam_husk", "fam_hearth", "fam_fortune"];

    private void BuildHeroActions(HBoxContainer controls, InteractiveCombat combat)
    {
        if (combat.Actions.Count == 0)
            return;

        if (combat.Actions.Any(a => a.value == BrewAction))
        {
            controls.AddChild(CauldronView(combat));
            var book = new Button { Text = "Recipes", Name = "RecipeBookButton", TooltipText = "The Recipe Book." };
            book.Pressed += () => RecipeBookPanel.Open(this);
            controls.AddChild(book);
        }

        foreach (var action in combat.Actions)
        {
            var target = FirstLivingEnemy(combat);
            var cost = combat.ActionCost(action, target).Sum(c => c.Amount);
            var name = GameHost.Instance.Blueprint.Cards.FirstOrDefault(c => c.Id == action.value)?.NameKey ?? action.value;
            var button = new Button
            {
                Text = $"{name} ({cost}⚡)",
                Name = $"Action_{action.value}",
                Disabled = !combat.CanUse(action, target),
                TooltipText = GameHost.Instance.Blueprint.Presentation.Cards.GetValueOrDefault(action.value)?.FlavorText ?? "",
            };
            // A greyed-out button says WHY (player report 2026-10-04: "ausgegraut" with no way to tell a rule from
            // a bug).
            if (button.Disabled && WhyNot(combat, action, cost) is { } why)
                button.TooltipText = $"{why}\n\n{button.TooltipText}";
            // §18: when the pot is Ready, BREW pulses.
            if (action.value == BrewAction && !button.Disabled)
                button.Ready += () => Pulse(button);
            var chosen = action;
            button.Pressed += () =>
            {
                if (Play?.CombatDriver is not { } driver || driver.Current is not { } now)
                    return;
                _armedCard = null;
                driver.UseAction(chosen, FirstLivingEnemy(now));
                SurfaceNewProblems();
                GameHost.Instance.AutoSave();
            };
            controls.AddChild(button);
        }
    }

    private static string? WhyNot(InteractiveCombat combat, CardDefinitionId action, int cost)
    {
        if (!combat.IsHeroTurn)
            return "Not now: it is not your turn.";
        var pot = combat.State.GetCardZones(combat.HeroId).SetAside.Count;
        if (action.value == CauldronAction)
        {
            var cookable = combat.Hand.Count(c => !(GameHost.Instance.Blueprint.Cards
                .FirstOrDefault(d => d.Id == c.DefinitionId.value)?.Tags.Any(t => t.value == "uncookable") ?? false));
            if (pot >= 3)
                return "The cauldron is full: Brew first.";
            if (cookable == 0)
                return "No card in your hand will go into the cauldron.";
        }
        else if (action.value == BrewAction && pot < 3)
        {
            return $"Brewing wants three in the cauldron ({pot} now).";
        }
        if (combat.HeroEnergy < cost)
            return $"Costs {cost} Energy, you have {combat.HeroEnergy}."
                + (action.value == CauldronAction ? " (Only the first card each turn is free.)" : "");
        return "A rule in this fight forbids it right now.";
    }

    private static CombatantId? FirstLivingEnemy(InteractiveCombat combat) =>
        combat.State.Combatants
            .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;

    // The pot: three slots, its state, and what it would brew.
    private static Control CauldronView(InteractiveCombat combat)
    {
        var pot = combat.State.GetCardZones(combat.HeroId).SetAside;
        var box = new PanelContainer { Name = "Cauldron", TooltipText = "The cauldron." };
        box.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(MoonvineTheme.BgPanelStrong, MoonvineTheme.Hairline, radius: 6));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        box.AddChild(row);

        // §18, without pictures yet: the lid and the steam say the state — lid down while she shelters, a thread of
        // steam per ingredient, a rattling lid when it is Ready — and each ingredient wears its family's colour.
        var state = pot.Count == 0 ? "Sheltering" : pot.Count >= 3 ? "Ready" : "Brewing";
        var lid = pot.Count == 0 ? "lid down" : pot.Count >= 3 ? "lid rattling" : "lid open";
        var stateLabel = new Label
        {
            Text = $"Cauldron · {state}",
            VerticalAlignment = VerticalAlignment.Center,
            TooltipText = pot.Count == 0 ? "Empty: she shelters behind it." : $"{pot.Count} in the pot ({lid}).",
        };
        stateLabel.AddThemeColorOverride("font_color", pot.Count >= 3 ? MoonvineTheme.AccentLight : MoonvineTheme.TextSoft);
        row.AddChild(stateLabel);

        var steam = new Label { Text = pot.Count == 0 ? "◡" : new string('≀', Math.Min(pot.Count, 3)), Name = "Steam",
            VerticalAlignment = VerticalAlignment.Center, TooltipText = lid };
        steam.AddThemeColorOverride("font_color", pot.Count == 0 ? MoonvineTheme.TextMuted : MoonvineTheme.TextSoft);
        if (pot.Count > 0)
            steam.Ready += () => Steam(steam, pot.Count);
        row.AddChild(steam);

        var cards = GameHost.Instance.Blueprint.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
        for (var slot = 0; slot < Math.Max(3, pot.Count); slot++)
        {
            var data = slot < pot.Count ? cards.GetValueOrDefault(pot[slot].DefinitionId.value) : null;
            var text = slot < pot.Count ? data?.NameKey ?? pot[slot].DefinitionId.value : "·";
            var family = data?.Tags.Select(t => t.value).FirstOrDefault(FamilyTags.Contains);
            var chip = new Label
            {
                Text = slot >= 3 ? $"[{text} · reserve]" : $"[{text}]",
                VerticalAlignment = VerticalAlignment.Center,
                TooltipText = slot < pot.Count ? FamilyName(family) : "an empty slot",
            };
            chip.AddThemeFontSizeOverride("font_size", 12);
            chip.AddThemeColorOverride("font_color", slot < pot.Count ? FamilyColor(family) : MoonvineTheme.TextMuted);
            row.AddChild(chip);
        }
        if (pot.Count >= 3)
            box.Ready += () => Rattle(stateLabel);

        if (pot.Count >= 3 && Preview(pot, cards) is { } preview)
        {
            var said = new Label { Text = $"→ {preview.Name}", VerticalAlignment = VerticalAlignment.Center };
            said.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
            said.TooltipText = preview.Effect;
            box.TooltipText = $"{preview.Name}: {preview.Effect}";
            row.AddChild(said);
        }
        return box;
    }

    // Each family's colour, as the rest of the screen already speaks it: damage, a hex, defence, the hearth, luck.
    private static Color FamilyColor(string? family) => family switch
    {
        "fam_fang" => MoonvineTheme.Harm,
        "fam_hex" => MoonvineTheme.Arcane,
        "fam_husk" => MoonvineTheme.Steel,
        "fam_hearth" => MoonvineTheme.Copper,
        "fam_fortune" => MoonvineTheme.Signal,
        _ => MoonvineTheme.TextMuted,
    };

    private static string FamilyName(string? family) => family switch
    {
        "fam_fang" => "Fang",
        "fam_hex" => "Hex",
        "fam_husk" => "Husk",
        "fam_hearth" => "Hearth",
        "fam_fortune" => "Fortune",
        _ => "Dregs: no family",
    };

    // The motions, each a looping tween on the node it moves — freed with it when the screen redraws.
    private static void Steam(Label steam, int strength)
    {
        var tween = steam.CreateTween().SetLoops();
        var period = strength >= 3 ? 0.45f : strength == 2 ? 0.7f : 1.1f;
        tween.TweenProperty(steam, "modulate:a", 0.35f, period);
        tween.TweenProperty(steam, "modulate:a", 1f, period);
    }

    private static void Rattle(Control lid)
    {
        var tween = lid.CreateTween().SetLoops();
        tween.TweenProperty(lid, "rotation_degrees", 2.5f, 0.06f);
        tween.TweenProperty(lid, "rotation_degrees", -2.5f, 0.06f);
        tween.TweenProperty(lid, "rotation_degrees", 0f, 0.06f);
        tween.TweenInterval(0.9f);
    }

    private static void Pulse(Control button)
    {
        var tween = button.CreateTween().SetLoops();
        tween.TweenProperty(button, "modulate", new Color(1.25f, 1.15f, 0.8f), 0.5f);
        tween.TweenProperty(button, "modulate", Colors.White, 0.5f);
    }

    // What the full pot would brew, by its families — read from BREW's own presentation, where the content lists
    // every family recipe. A pot with dregs in it has no family recipe; it says so.
    private static (string Name, string Effect)? Preview(
        IReadOnlyList<CardInstance> pot, IReadOnlyDictionary<string, RogueDeck.Scenario.Authoring.CardData> cards)
    {
        // A Hidden Recipe the player has FOUND is named for what it is; one not yet found never is — the pot
        // previews the ordinary family brew until the book knows better (§9).
        var ids = pot.Take(3).Select(card => cards.GetValueOrDefault(card.DefinitionId.value) is { } data
            && data.Tags.Any(t => FamilyTags.Contains(t.value)) ? card.DefinitionId.value.TrimEnd('+') : "dregs").ToList();
        foreach (var recipe in RecipeBook.Hidden(GameHost.Instance.Blueprint).Where(r => RecipeBook.Discovered(r.Number)))
            if (recipe.Cards.Order(StringComparer.Ordinal).SequenceEqual(ids.Order(StringComparer.Ordinal)))
                return (recipe.Name, recipe.Effect + Pinches(pot, cards));

        var families = pot.Take(3)
            .Select(card => cards.GetValueOrDefault(card.DefinitionId.value)?.Tags
                .Select(t => t.value).FirstOrDefault(FamilyTags.Contains))
            .ToList();
        if (families.Any(f => f is null))
            return ("A brew with dregs", "Some of what is in the pot is not an ingredient of any family.");
        var key = "recipe:" + string.Join("+", families.OrderBy(f => Array.IndexOf(FamilyTags, f)));
        var said = GameHost.Instance.Blueprint.Presentation.Cards.GetValueOrDefault(BrewAction)?.Extra.GetValueOrDefault(key);
        if (said is null)
            return null;
        var bar = said.IndexOf('|');
        var (name, effect) = bar < 0 ? (said, "") : (said[..bar], said[(bar + 1)..]);
        return (name, effect + Pinches(pot, cards));
    }

    // An upgraded card's PINCH (W8) comes on top of whatever is brewed; the card's own words say what it adds, so
    // the preview quotes them rather than keeping a second copy of the numbers.
    private static string Pinches(IReadOnlyList<CardInstance> pot, IReadOnlyDictionary<string, RogueDeck.Scenario.Authoring.CardData> cards)
    {
        var lines = pot.Take(3)
            .Select(card => cards.GetValueOrDefault(card.DefinitionId.value)?.DescriptionKey ?? "")
            .Select(text => text.IndexOf("A pinch of", StringComparison.Ordinal) is var at and >= 0 ? text[at..] : null)
            .OfType<string>().ToList();
        return lines.Count == 0 ? "" : " " + string.Join(" ", lines);
    }

    // THE CAULDRON, WALKED (`--smoke-cauldron --character=hedge_witch`): into the first fight, three cards into the
    // pot through the same prompt a player answers, a picture of the Ready pot with its preview, then the Brew —
    // and what it did, in numbers.
    private async System.Threading.Tasks.Task SmokeCauldron()
    {
        var session = Session;
        for (var guard = 0; guard < 10 && session is not null && Play?.CombatDriver?.Current is null; guard++)
        {
            if (session.IsAwaitingNodeChoice)
                session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else
                break;
        }
        if (Play?.CombatDriver is not { Current: { } } driver)
        {
            GD.Print("smoke-cauldron: no fight reached");
            GetTree().Quit();
            return;
        }

        var add = driver.Current!.Actions.FirstOrDefault(a => a.value == CauldronAction);
        if (add.value is null)
        {
            GD.Print("smoke-cauldron: this hero has no cauldron");
            GetTree().Quit();
            return;
        }
        // FIRST THROUGH THE BUTTON, the way a player does it (player report 2026-10-04: a second card into the pot
        // sometimes failed, and so did the free one with no Energy left). Pressing the real "Into the pot" button
        // runs everything behind it — the recorder, the save — which calling the driver directly skips.
        await PressIntoThePot("turn 1, first");
        await PressIntoThePot("turn 1, second");
        driver.EndTurn();
        await Frames(4);
        // Spend the turn's Energy on cards first: the free ingredient must still go in at 0.
        if (driver.Current is { } next && FirstLivingEnemy(next) is { } foe)
            for (var guard = 0; guard < 10 && driver.Current!.HeroEnergy > 0; guard++)
            {
                var spent = driver.Current.HeroEnergy;
                if (driver.Current.Hand.FirstOrDefault(c => driver.Current.CanPlay(c.Id)) is not { } card)
                    break;
                driver.PlayCard(card.Id, foe);
                await Frames(2);
                if (driver.PendingCardChoice is not null || driver.PendingOptionChoice is not null
                    || driver.Current?.HeroEnergy == spent)
                    break;
            }
        await PressIntoThePot("turn 2, no Energy left");

        var energy = driver.Current!.HeroEnergy;
        for (var i = 0; i < 3 && driver.Current!.CanUse(add); i++)
        {
            driver.UseAction(add, null);
            if (driver.PendingCardChoice is { Count: > 0 } offered)
                driver.SupplyCardChoice([offered[0].Id]);
        }
        Rebuild();
        for (var i = 0; i < 4; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var combat = driver.Current!;
        var pot = combat.State.GetCardZones(combat.HeroId).SetAside;
        var preview = FindChild("Cauldron", recursive: true, owned: false) is Control view ? view.TooltipText : "(no cauldron shown)";
        GD.Print($"smoke-cauldron: pot={pot.Count} [{string.Join(", ", pot.Select(c => c.DefinitionId.value))}] "
            + $"energy {energy}→{combat.HeroEnergy} · {preview}");
        GetViewport().GetTexture().GetImage().SavePng("user://smoke-cauldron.png");
        GD.Print("smoke: screenshot user://smoke-cauldron.png");

        var enemy = FirstLivingEnemy(combat);
        var before = enemy is { } e ? combat.State.GetCombatant(e).Health.Current : 0;
        var brew = combat.Actions.First(a => a.value == BrewAction);
        var brewable = combat.CanUse(brew, enemy);
        driver.UseAction(brew, enemy);
        var after = driver.Current!;
        GD.Print($"smoke-cauldron: brew allowed={brewable} · pot now {after.State.GetCardZones(after.HeroId).SetAside.Count}"
            + $" · enemy HP {before}→{(enemy is { } id ? after.State.GetCombatant(id).Health.Current : 0)}"
            + $" · hero block {after.HeroGuard} · error={Session?.Error ?? Play?.Error ?? "none"}");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // One press of the real "Into the pot" button, then a click on the first card the prompt offers — and what the
    // pot, the Energy and the button said before and after.
    private async System.Threading.Tasks.Task PressIntoThePot(string when)
    {
        await Frames(2);
        var driver = Play!.CombatDriver!;
        var before = driver.Current!;
        var potBefore = before.State.GetCardZones(before.HeroId).SetAside.Count;
        var energyBefore = before.HeroEnergy;
        if (FindChild($"Action_{CauldronAction}", recursive: true, owned: false) is not Button button)
        {
            GD.Print($"smoke-cauldron [{when}]: no 'Into the pot' button on screen");
            return;
        }
        var label = $"{button.Text}{(button.Disabled ? " DISABLED" : "")}";
        if (button.Disabled)
        {
            GD.Print($"smoke-cauldron [{when}]: button {label} · pot {potBefore} · energy {energyBefore}");
            return;
        }
        button.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        var offered = driver.PendingCardChoice;
        // Click the card on SCREEN, not the handler: a face dimmed as unaffordable has a disabled click, and the
        // handler alone never saw that (the 0-Energy pot bug, 2026-10-04).
        var clickable = "none";
        if (offered is { Count: > 0 })
        {
            clickable = FindChild($"Choice_{offered[0].Id.value}", recursive: true, owned: false) is { } face
                && face.FindChildren("*", nameof(Button), recursive: true, owned: false)
                    .OfType<Button>().FirstOrDefault() is { } overlay
                ? overlay.Disabled ? "DISABLED" : "yes"
                : "not found";
            if (clickable == "yes")
                OnCardChoiceClicked(Play, offered, offered[0].Id);
            await Frames(2);
        }
        var after = driver.Current!;
        GD.Print($"smoke-cauldron [{when}]: button {label} · prompt offered {offered?.Count ?? 0} (card clickable: {clickable}) · pot {potBefore}→"
            + $"{after.State.GetCardZones(after.HeroId).SetAside.Count} · energy {energyBefore}→{after.HeroEnergy}"
            + $" · error={Session?.Error ?? Play?.Error ?? "none"}");
    }
}
