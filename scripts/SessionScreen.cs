using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// The run screen: renders whichever state the engine session parked in — the exact dispatch order of
// the Studio's RunSessionView (error → event choice → entity pick → path fork → interlude → combat →
// complete) — and forwards every input to the session/driver. The whole screen rebuilds on
// GameHost.StateChanged; the layouts are simple enough that a full rebuild per answer is fine.
public partial class SessionScreen : Control
{
    private VBoxContainer _main = null!;
    private VBoxContainer _sidebar = null!;
    private RichTextLabel _log = null!;

    // Transient pick state owned by the view (mirrors RunSessionView's _selected/_cardPicks/_combatTarget).
    private readonly HashSet<int> _selectedEntities = [];
    private readonly HashSet<string> _selectedCards = [];
    private CardInstanceId? _armedCard; // the hand card waiting for a target click
    private int _seenProblems;

    private static RunPlayback? Play => GameHost.Instance.Play;
    private static InteractiveRunSession? Session => Play?.Session;

    public override void _Ready()
    {
        Theme = MoonvineTheme.Build();
        var background = new ColorRect { Color = MoonvineTheme.Bg };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var split = new HBoxContainer();
        split.SetAnchorsPreset(LayoutPreset.FullRect);
        split.AddThemeConstantOverride("separation", 16);
        AddChild(split);

        var mainPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var mainScroll = new ScrollContainer();
        _main = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _main.AddThemeConstantOverride("separation", 10);
        mainScroll.AddChild(_main);
        mainPanel.AddChild(mainScroll);
        split.AddChild(mainPanel);

        var side = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
        side.AddThemeConstantOverride("separation", 10);
        var sidePanel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        var sideScroll = new ScrollContainer();
        _sidebar = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sideScroll.AddChild(_sidebar);
        sidePanel.AddChild(sideScroll);
        side.AddChild(sidePanel);
        var logPanel = new PanelContainer { CustomMinimumSize = new Vector2(320, 200) };
        _log = new RichTextLabel { FitContent = false, ScrollFollowing = true, BbcodeEnabled = false };
        logPanel.AddChild(_log);
        side.AddChild(logPanel);
        split.AddChild(side);

        GameHost.Instance.StateChanged += Rebuild;
        Rebuild();

        if (OS.GetCmdlineUserArgs().Contains("--smoke-run"))
            SmokeRun();
    }

    // Headless proof of the whole Godot-side loop: walk to the first fight THROUGH the same methods the
    // buttons call, play one affordable card at the default target, and report the resulting state.
    private void SmokeRun()
    {
        var session = Session;
        if (session is null)
        {
            GD.Print("smoke-run: NO SESSION");
            GetTree().Quit();
            return;
        }

        for (var guard = 0; guard < 10 && Play?.CombatDriver?.Current is null; guard++)
        {
            if (session.IsAwaitingNodeChoice)
                session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else
                break;
        }

        var combat = Play?.CombatDriver?.Current;
        if (combat is null)
        {
            GD.Print($"smoke-run: no fight reached (error={session.Error ?? Play?.Error ?? "none"})");
            GetTree().Quit();
            return;
        }

        var hero = combat.State.GetCombatant(combat.HeroId);
        var enemy = combat.State.Combatants.First(c => c.Id != combat.HeroId);
        var hpBefore = enemy.Health.Current;
        _armedCard = combat.Hand.FirstOrDefault(c => CanPay(hero, c.DefinitionId.value))?.Id;
        if (_armedCard is not null)
            PlayArmedCardAt(null);

        var after = Play?.CombatDriver?.Current;
        var enemyAfter = after?.State.Combatants.First(c => c.Id != after.HeroId);
        GD.Print("smoke-run: "
            + $"fight={combat.State.Combatants.Count(c => c.Id != combat.HeroId)}v1 "
            + $"hand={combat.Hand.Count}→{after?.Hand.Count ?? -1} "
            + $"enemyHp={hpBefore}→{enemyAfter?.Health.Current ?? -1} "
            + $"intent={combat.UpcomingIntentFor(enemy.Id)?.Label ?? "-"} "
            + $"error={session.Error ?? Play?.Error ?? "none"}");

        // Windowed run only: let the freshly-built UI render a few frames, capture the combat screen so
        // the look can be eyeballed, then quit. Headless has no framebuffer to read, so just quit.
        if (DisplayServer.GetName().Contains("headless"))
        {
            GetTree().Quit();
            return;
        }
        _ = CaptureThenQuit();
    }

    private async System.Threading.Tasks.Task CaptureThenQuit()
    {
        for (var i = 0; i < 3; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng("user://smoke-combat.png");
        GD.Print($"smoke-run: screenshot user://smoke-combat.png ({image.GetWidth()}x{image.GetHeight()})");
        GetTree().Quit();
    }

    public override void _ExitTree() => GameHost.Instance.StateChanged -= Rebuild;

    // ── the dispatcher ───────────────────────────────────────────────────────────

    private void Rebuild()
    {
        foreach (var child in _main.GetChildren())
            child.QueueFree();
        foreach (var child in _sidebar.GetChildren())
            child.QueueFree();

        var session = Session;
        if (Play is null || session is null)
        {
            Title("No run active.");
            return;
        }

        if (Play.Error is { } hostError)
            Title($"Error: {hostError}", MoonvineTheme.Danger);
        else if (session.Error is { } runError)
            Title($"Run error: {runError}", MoonvineTheme.Danger);
        else if (session.IsAwaitingChoice && session.PendingSituation is { } situation)
            RenderChoices(session, situation);
        else if (session.IsAwaitingEntities && session.PendingEntities is { } entities)
            RenderEntityPick(session, entities);
        else if (session.IsAwaitingNodeChoice)
            RenderNodeFork(session);
        else if (session.IsAwaitingInterlude)
            RenderInterlude(session);
        else if (Play.CombatDriver?.Current is { } combat)
            RenderCombat(session, combat);
        else if (session.IsComplete)
            RenderComplete(session);
        else
            Title("…");

        RenderSidebar(session);
        _log.Text = string.Join("\n", session.Run.Log.TakeLast(60).Select(entry => entry.Message));
    }

    // ── run-level states ─────────────────────────────────────────────────────────

    private void RenderChoices(InteractiveRunSession session, EventSituation situation)
    {
        Title(situation.TextKey ?? situation.Id);
        foreach (var choice in session.PendingChoices)
        {
            var id = choice.Id;
            AddButton(choice.TextKey ?? id, () => session.Pick(id));
        }
    }

    private void RenderEntityPick(InteractiveRunSession session, EntitySelectionRequest entities)
    {
        Title(entities.Purpose);
        Muted($"Pick {entities.Count}");
        for (var i = 0; i < entities.Displays.Count; i++)
        {
            var index = i;
            var selected = _selectedEntities.Contains(index);
            AddButton((selected ? "✓ " : "") + Display(entities.Displays[index]), () =>
            {
                if (!_selectedEntities.Remove(index))
                {
                    if (entities.Count == 1)
                        _selectedEntities.Clear();
                    if (_selectedEntities.Count < entities.Count)
                        _selectedEntities.Add(index);
                }
                Rebuild();
            });
        }
        var confirm = AddButton("Confirm", () =>
        {
            var picks = _selectedEntities.ToList();
            _selectedEntities.Clear();
            session.PickEntities(picks);
        });
        confirm.Disabled = _selectedEntities.Count != entities.Count;
    }

    private string Display(string raw) =>
        Play is { } play && play.CardNames.TryGetValue(raw, out var name) ? name : raw;

    private void RenderNodeFork(InteractiveRunSession session)
    {
        Title("Choose your path");
        foreach (var node in session.PendingNodeChoices)
        {
            var id = node.Id.Value;
            AddButton($"{id}  ({node.Type.Value})", () => session.PickNode(id));
        }
    }

    private void RenderInterlude(InteractiveRunSession session)
    {
        Title("Between rooms");
        foreach (var consumable in session.Run.Consumables.Where(c => c.UseEffects.Count > 0))
        {
            var id = consumable.Id;
            AddButton($"Use {consumable.DefinitionId.Value}", () => session.UseConsumable(id));
        }
        AddButton("Continue ▸", session.Continue);
        AddButton("Save run", () => Toast(GameHost.Instance.SaveRun() ?? "Saved."));
    }

    private void RenderComplete(InteractiveRunSession session)
    {
        var victory = session.Run.Result == RunResult.Victory;
        Title(victory ? "Victory!" : $"Run over — {session.Run.Result}",
            victory ? MoonvineTheme.Accent : MoonvineTheme.Danger);
        AddButton("Back to title", () => GetTree().ChangeSceneToFile("res://scenes/Boot.tscn"));
    }

    // ── combat ───────────────────────────────────────────────────────────────────

    private void RenderCombat(InteractiveRunSession session, InteractiveCombat combat)
    {
        var play = Play!;
        var hero = combat.State.GetCombatant(combat.HeroId);
        Title($"Combat — round {combat.Round}");

        foreach (var combatant in combat.State.Combatants)
        {
            var isHero = combatant.Id == combat.HeroId;
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(
                isHero ? MoonvineTheme.BgControl : MoonvineTheme.BgPanel,
                _armedCard is not null && !isHero && combatant.IsAlive ? MoonvineTheme.Accent : null));
            var column = new VBoxContainer();

            column.AddChild(new Label
            {
                Text = $"{Name(combatant, combat)}   {combatant.Health.Current}/{combatant.Health.Max} HP"
                    + (Block(combatant) > 0 ? $"  🛡{Block(combatant)}" : "")
                    + (combatant.IsAlive ? "" : "  💀"),
            });

            if (!isHero && combat.UpcomingIntentFor(combatant.Id) is { } intent)
            {
                var intentLabel = new Label { Text = $"▸ {intent.Label}" };
                intentLabel.AddThemeColorOverride("font_color", MoonvineTheme.IntentColor(intent.Kind));
                column.AddChild(intentLabel);
            }
            if (combatant.Statuses.Count > 0)
                column.AddChild(MutedLabel(StatusLine(combatant)));
            if (isHero)
                column.AddChild(MutedLabel(ResourcePoolsLine(combatant)));

            if (_armedCard is not null && !isHero && combatant.IsAlive)
            {
                var targetId = combatant.Id;
                var pick = new Button { Text = "◎ Target" };
                pick.Pressed += () => PlayArmedCardAt(targetId);
                column.AddChild(pick);
            }
            panel.AddChild(column);
            _main.AddChild(panel);
        }

        if (play.CombatDriver!.PendingCardChoice is { } cardChoice)
        {
            RenderCardChoice(play, cardChoice);
            return;
        }
        if (!combat.IsHeroTurn)
        {
            Muted("Resolving enemy actions…");
            return;
        }

        Muted(_armedCard is not null
            ? "Choose a target — or click the card again for the first enemy."
            : "Your hand:");
        var handRow = new HFlowContainer();
        foreach (var card in combat.Hand)
        {
            var cardId = card.Id;
            var definition = card.DefinitionId.value;
            var button = new Button
            {
                Text = $"{CardName(definition)}\n{CostLabel(definition)}",
                Disabled = !CanPay(hero, definition),
                CustomMinimumSize = new Vector2(150, 72),
            };
            var presentation = GameHost.Instance.Blueprint.Presentation.Cards.GetValueOrDefault(definition);
            button.AddThemeColorOverride("font_color", MoonvineTheme.RarityColor(presentation?.Rarity));
            if (_armedCard is { } armed && armed.value == cardId.value)
                button.AddThemeStyleboxOverride("normal", MoonvineTheme.Panel(MoonvineTheme.BgControl, MoonvineTheme.Accent, 8));
            button.Pressed += () => OnCardClicked(cardId);
            handRow.AddChild(button);
        }
        if (combat.Hand.Count == 0)
            Muted("(empty hand)");
        _main.AddChild(handRow);

        AddButton("End turn ▸", () =>
        {
            _armedCard = null;
            play.CombatDriver.EndTurn();
            SurfaceNewProblems();
        });

        foreach (var consumable in session.Run.Consumables.Where(c => c.CombatUse is not null))
        {
            var id = consumable.Id;
            AddButton($"Use {consumable.DefinitionId.Value}", () => play.UseConsumableInCombat(id));
        }
    }

    private void RenderCardChoice(RunPlayback play, IReadOnlyList<CardInstance> candidates)
    {
        var driver = play.CombatDriver!;
        Title(driver.PendingCardChoicePurpose);
        Muted($"Pick {driver.PendingCardChoiceCount}");
        foreach (var candidate in candidates)
        {
            var id = candidate.Id;
            var selected = _selectedCards.Contains(id.value);
            AddButton((selected ? "✓ " : "") + CardName(candidate.DefinitionId.value), () =>
            {
                if (driver.PendingCardChoiceCount == 1)
                {
                    _selectedCards.Clear();
                    driver.SupplyCardChoice([id]);
                    return;
                }
                if (!_selectedCards.Remove(id.value) && _selectedCards.Count < driver.PendingCardChoiceCount)
                    _selectedCards.Add(id.value);
                Rebuild();
            });
        }
        if (driver.PendingCardChoiceCount > 1)
        {
            var confirm = AddButton("Confirm", () =>
            {
                var picks = candidates.Where(c => _selectedCards.Contains(c.Id.value)).Select(c => c.Id).ToList();
                _selectedCards.Clear();
                driver.SupplyCardChoice(picks);
            });
            confirm.Disabled = _selectedCards.Count != driver.PendingCardChoiceCount;
        }
    }

    // Click a card: arm it for a target click; clicking the SAME card again plays it at the default
    // target (first living enemy — the reference behavior for untargeted plays).
    private void OnCardClicked(CardInstanceId cardId)
    {
        if (_armedCard is { } armed && armed.value == cardId.value)
        {
            PlayArmedCardAt(null);
            return;
        }
        _armedCard = cardId;
        Rebuild();
    }

    private void PlayArmedCardAt(CombatantId? target)
    {
        var driver = Play?.CombatDriver;
        var combat = driver?.Current;
        if (driver is null || combat is null || _armedCard is not { } armed)
            return;
        _armedCard = null;
        target ??= combat.State.Combatants
            .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
        driver.PlayCard(armed, target);
        SurfaceNewProblems();
    }

    // Rejected plays are recorded as step problems, not thrown — surface newly-appeared ones as a toast.
    private void SurfaceNewProblems()
    {
        var steps = Play?.CombatDriver?.Current?.Steps;
        if (steps is null)
        {
            _seenProblems = 0;
            return;
        }
        var problems = steps.Where(s => s.HasProblems).SelectMany(s => s.Problems).ToList();
        if (problems.Count > _seenProblems)
            Toast(problems[^1]);
        _seenProblems = problems.Count;
    }

    // ── sidebar + widgets ────────────────────────────────────────────────────────

    private void RenderSidebar(InteractiveRunSession session)
    {
        var run = session.Run;
        _sidebar.AddChild(new Label { Text = Play?.HeroName ?? "You" });
        _sidebar.AddChild(MutedLabel($"HP {run.Health.Current}/{run.Health.Max}"));
        foreach (var (resource, amount) in run.Resources.OrderBy(r => r.Key.Value, StringComparer.Ordinal))
            _sidebar.AddChild(MutedLabel($"{resource.Value}: {amount}"));

        if (run.Relics.Count > 0)
        {
            _sidebar.AddChild(new Label { Text = "Relics" });
            foreach (var relic in run.Relics)
                _sidebar.AddChild(MutedLabel($"• {relic.Definition.DisplayName}{(relic.Enabled ? "" : " (off)")}"));
        }
        if (run.Consumables.Count > 0)
        {
            _sidebar.AddChild(new Label { Text = "Consumables" });
            foreach (var consumable in run.Consumables)
                _sidebar.AddChild(MutedLabel($"• {consumable.DefinitionId.Value}"));
        }

        _sidebar.AddChild(new Label { Text = $"Deck ({run.Deck.Count})" });
        foreach (var group in run.Deck
            .GroupBy(card => CardName(card.DefinitionId.value) + new string('+', card.UpgradeLevel))
            .OrderBy(g => g.Key, StringComparer.Ordinal))
            _sidebar.AddChild(MutedLabel(group.Count() > 1 ? $"{group.Key} ×{group.Count()}" : group.Key));
    }

    private void Title(string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeFontSizeOverride("font_size", 22);
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        _main.AddChild(label);
    }

    private void Muted(string text) => _main.AddChild(MutedLabel(text));

    private static Label MutedLabel(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        return label;
    }

    private Button AddButton(string text, Action onPressed)
    {
        var button = new Button { Text = text };
        button.Pressed += () => onPressed();
        _main.AddChild(button);
        return button;
    }

    private void Toast(string message)
    {
        var label = new Label { Text = message };
        label.AddThemeColorOverride("font_color", MoonvineTheme.Warning);
        label.SetAnchorsPreset(LayoutPreset.CenterBottom);
        label.Position -= new Vector2(0, 48);
        AddChild(label);
        GetTree().CreateTimer(2.5).Timeout += () => label.QueueFree();
    }

    // ── ported display helpers (RunSessionView.razor) ────────────────────────────

    private string Name(CombatantState combatant, InteractiveCombat combat) =>
        combatant.Id == combat.HeroId
            ? Play?.HeroName ?? "You"
            : Play!.EnemyNames.TryGetValue(combatant.Id.value, out var name) ? name : combatant.Id.value;

    private string CardName(string definitionId)
    {
        var play = Play!;
        if (play.CardNames.TryGetValue(definitionId, out var name))
            return name;
        if (definitionId.StartsWith("shred:", StringComparison.Ordinal))
            return string.Join(" + ", definitionId["shred:".Length..].Split('+')
                .Select(part => play.ShredNames.TryGetValue(part, out var partName) ? partName : part));
        return definitionId;
    }

    private IReadOnlyList<ResourceCost> FullCosts(string definitionId)
    {
        var play = Play!;
        if (play.CardFullCosts.TryGetValue(definitionId, out var costs))
            return costs;
        return play.ComposedCostsFor(definitionId)
            ?? [new ResourceCost(StandardCombatIds.EnergyResource, play.CardCosts.GetValueOrDefault(definitionId))];
    }

    private string ResourceLabel(ResourceId id) =>
        id == StandardCombatIds.EnergyResource ? "⚡"
        : Play!.ResourceNames.TryGetValue(id.value, out var name) ? name + " "
        : id.value + " ";

    private string CostLabel(string definitionId)
    {
        var costs = FullCosts(definitionId);
        return costs.Count == 0
            ? "⚡0"
            : string.Join(" · ", costs.Select(c => $"{ResourceLabel(c.ResourceId)}{c.Amount}"));
    }

    private bool CanPay(CombatantState payer, string definitionId) =>
        FullCosts(definitionId).All(cost =>
            payer.Resources.TryGetValue(cost.ResourceId, out var pool) && pool.Current >= cost.Amount);

    private string ResourcePoolsLine(CombatantState combatant)
    {
        var pools = combatant.Resources
            .OrderBy(p => p.Key == StandardCombatIds.EnergyResource ? 0 : 1)
            .ThenBy(p => p.Key.value, StringComparer.Ordinal)
            .Select(p => $"{ResourceLabel(p.Key).TrimEnd()} {p.Value.Current}{(p.Value.Max is { } max ? $"/{max}" : "")}");
        var line = string.Join(" · ", pools);
        return line.Length == 0 ? "—" : line;
    }

    private static int Block(CombatantState combatant) =>
        combatant.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool) ? pool.Current : 0;

    private static string StatusLine(CombatantState combatant) =>
        string.Join("  ", combatant.Statuses.Select(s =>
        {
            var name = s.DefinitionId.value.Replace("standard.", "");
            var magnitude = s.Stacks > 0 ? $"×{s.Stacks}"
                : s.DurationTurns > 0 ? $" {s.DurationTurns}t"
                : s.Charges > 0 ? $" {s.Charges}c" : "";
            return $"{name}{magnitude}";
        }));
}
