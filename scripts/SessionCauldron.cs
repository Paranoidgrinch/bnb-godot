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
            controls.AddChild(CauldronView(combat));

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

        var state = pot.Count == 0 ? "Sheltering" : pot.Count >= 3 ? "Ready" : "Brewing";
        var stateLabel = new Label { Text = $"Cauldron · {state}", VerticalAlignment = VerticalAlignment.Center };
        stateLabel.AddThemeColorOverride("font_color", pot.Count >= 3 ? MoonvineTheme.AccentLight : MoonvineTheme.TextSoft);
        row.AddChild(stateLabel);

        var cards = GameHost.Instance.Blueprint.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
        for (var slot = 0; slot < 3; slot++)
        {
            var text = slot < pot.Count
                ? cards.GetValueOrDefault(pot[slot].DefinitionId.value)?.NameKey ?? pot[slot].DefinitionId.value
                : "·";
            var chip = new Label { Text = $"[{text}]", VerticalAlignment = VerticalAlignment.Center };
            chip.AddThemeFontSizeOverride("font_size", 12);
            chip.AddThemeColorOverride("font_color", slot < pot.Count ? MoonvineTheme.Text : MoonvineTheme.TextMuted);
            row.AddChild(chip);
        }

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

    // What the full pot would brew, by its families — read from BREW's own presentation, where the content lists
    // every family recipe. A pot with dregs in it has no family recipe; it says so.
    private static (string Name, string Effect)? Preview(
        IReadOnlyList<CardInstance> pot, IReadOnlyDictionary<string, RogueDeck.Scenario.Authoring.CardData> cards)
    {
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
        return bar < 0 ? (said, "") : (said[..bar], said[(bar + 1)..]);
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
        var energy = driver.Current.HeroEnergy;
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
}
