using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// THE HEDGE WITCH'S CAULDRON on the fight screen (Hedge Witch plan G2; playtest 3, B9). It is part of her, not a row
// of buttons: a pot over her effects, with what is in it, whether she is Sheltering, Brewing or Ready, and what it is
// about to make. Everything is done to the pot with the pointer:
//
//   drag a card from the hand onto the pot   Into the Pot (the first each turn free, then 1 Energy)
//   click the pot                            Brew (1 Energy, three in the pot)
//
// The gesture answers the card prompt "Into the Pot" raises with the card that was dragged (SessionWand's gesture,
// shared), so there is no second way of putting a card in. A hero without a cauldron gets none of this.
public partial class SessionScreen
{
    private const string CauldronAction = "cauldron_add";
    private const string BrewAction = "cauldron_brew";
    private static readonly string[] FamilyTags = ["fam_fang", "fam_hex", "fam_husk", "fam_hearth", "fam_fortune"];

    private void BuildHeroActions(HBoxContainer controls, ICombatView combat)
    {
        if (combat.Actions.Count == 0)
            return;

        if (combat.Actions.Any(a => a.value == BrewAction))
        {
            var book = new Button { Text = "Recipes", Name = "RecipeBookButton", TooltipText = "The Recipe Book." };
            book.Pressed += () => RecipeBookPanel.Open(this);
            controls.AddChild(book);
        }

        foreach (var action in combat.Actions.Where(a => !IsWandAction(a) && a.value is not (CauldronAction or BrewAction)))
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
                if (Play?.Seat is not { } driver || driver.Current is not { } now)
                    return;
                _armedCard = null;
                driver.UseAction(chosen, FirstLivingEnemy(now));
                SurfaceNewProblems();
                GameHost.Instance.AutoSave();
            };
            controls.AddChild(button);
        }
    }

    private static string? WhyNot(ICombatView combat, CardDefinitionId action, int cost)
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

    private static CombatantId? FirstLivingEnemy(ICombatView combat) =>
        combat.State.Combatants
            .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;

    // The pot as last drawn, for the drop test.
    private Control? _potView;
    private const int PotHeight = 64;

    // The pot: an iron bowl with its three ingredients in it, steam for each, a glow when it is Ready, and under it
    // what it is about to make.
    private Control? CauldronPot(ICombatView combat, int width)
    {
        _potView = null;
        if (!combat.Actions.Any(a => a.value == BrewAction))
            return null;
        var pot = combat.State.GetCardZones(combat.HeroId).SetAside;
        var cards = GameHost.Instance.Blueprint.Cards.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var brew = new CardDefinitionId(BrewAction);
        var brewable = combat.CanUse(brew, FirstLivingEnemy(combat));

        var column = new VBoxContainer { Name = "Cauldron", MouseFilter = MouseFilterEnum.Stop };
        column.AddThemeConstantOverride("separation", 0);
        column.MouseDefaultCursorShape = brewable ? CursorShape.PointingHand : CursorShape.Arrow;

        var vessel = new Control { CustomMinimumSize = new Vector2(width, PotHeight), MouseFilter = MouseFilterEnum.Pass };
        var ready = pot.Count >= 3;
        vessel.Draw += () =>
        {
            var w = vessel.Size.X;
            var top = 18f;
            // the bowl: a half-ellipse of dark iron under a lighter rim
            var bowl = new List<Vector2>();
            for (var i = 0; i <= 24; i++)
            {
                var t = Mathf.Pi * i / 24f;
                bowl.Add(new Vector2(w / 2 + Mathf.Cos(t) * (w / 2 - 6), top + Mathf.Sin(t) * (PotHeight - top - 4)));
            }
            vessel.DrawColoredPolygon([.. bowl], new Color("26262c"));
            vessel.DrawPolyline([.. bowl], ready ? MoonvineTheme.AccentLight : new Color("4a4a55"), 2f, antialiased: true);
            vessel.DrawRect(new Rect2(4, top - 4, w - 8, 7), ready ? MoonvineTheme.AccentLight : new Color("5a5a66"));
            // little feet
            vessel.DrawRect(new Rect2(w * 0.28f, PotHeight - 6, 6, 6), new Color("3a3a44"));
            vessel.DrawRect(new Rect2(w * 0.72f - 6, PotHeight - 6, 6, 6), new Color("3a3a44"));
            // a thread of steam per ingredient
            for (var thread = 0; thread < Math.Min(pot.Count, 3); thread++)
            {
                var x = w * (0.32f + 0.18f * thread);
                var steam = new Vector2[6];
                for (var k = 0; k < 6; k++)
                    steam[k] = new Vector2(x + (k % 2 == 0 ? -3 : 3), top - 6 - k * 2.4f);
                vessel.DrawPolyline(steam, new Color(MoonvineTheme.TextSoft, 0.55f), 1.5f, antialiased: true);
            }
        };
        // The three ingredients, in their families' colours, sit in the bowl.
        var inside = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Pass };
        inside.AddThemeConstantOverride("separation", 3);
        inside.SetAnchorsPreset(LayoutPreset.FullRect);
        inside.OffsetTop = 26;
        inside.OffsetBottom = -10;
        inside.OffsetLeft = 18;
        inside.OffsetRight = -18;
        for (var slot = 0; slot < Math.Max(3, pot.Count); slot++)
        {
            var data = slot < pot.Count ? cards.GetValueOrDefault(pot[slot].DefinitionId.value) : null;
            var family = data?.Tags.Select(t => t.value).FirstOrDefault(FamilyTags.Contains);
            var chip = new Label
            {
                Text = slot < pot.Count ? data?.NameKey ?? pot[slot].DefinitionId.value : "·",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CustomMinimumSize = new Vector2((width - 40) / 3f - 3, 20),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                ClipText = true,
                MouseFilter = MouseFilterEnum.Pass,
                TooltipText = slot < pot.Count ? $"{data?.NameKey}: {FamilyName(family)}" + (slot >= 3 ? " (reserve)" : "") : "An empty place.",
            };
            chip.AddThemeFontSizeOverride("font_size", 10);
            chip.AddThemeColorOverride("font_color", slot < pot.Count ? FamilyColor(family) : MoonvineTheme.TextMuted);
            inside.AddChild(chip);
        }
        vessel.AddChild(inside);
        column.AddChild(vessel);

        // Under the pot: its state, and what it is about to make.
        var preview = ready ? Preview(pot, cards) : null;
        var state = new Label
        {
            Text = pot.Count == 0 ? "Sheltering" : ready ? $"Ready: {preview?.Name ?? "brew"}" : $"Brewing {pot.Count}/3",
            HorizontalAlignment = HorizontalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            ClipText = true,
            CustomMinimumSize = new Vector2(width, 0),
            MouseFilter = MouseFilterEnum.Pass,
        };
        state.AddThemeFontSizeOverride("font_size", 11);
        state.AddThemeColorOverride("font_color", ready ? MoonvineTheme.AccentLight : MoonvineTheme.TextSoft);
        column.AddChild(state);

        var add = new CardDefinitionId(CauldronAction);
        column.TooltipText = string.Join("\n\n", new[]
        {
            preview is { } brewed ? $"{brewed.Name}: {brewed.Effect}" : null,
            pot.Count == 0 ? "Empty: she shelters behind the lid." : null,
            "Drag a card from your hand onto the pot to put it in (the first each turn is free, then 1 Energy).",
            brewable ? "Click the pot to brew (1 Energy)." : WhyNot(combat, brew, combat.ActionCost(brew, FirstLivingEnemy(combat)).Sum(c => c.Amount)),
            combat.CanUse(add, null) ? null : WhyNot(combat, add, combat.ActionCost(add, null).Sum(c => c.Amount)),
        }.Where(t => !string.IsNullOrWhiteSpace(t)));

        column.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } press)
            {
                _wandPress = (null, null, press.GlobalPosition);
                _pressOnPot = true;
                column.AcceptEvent();
            }
        };
        if (brewable)
            column.Ready += () => Pulse(vessel);
        _potView = column;
        return column;
    }

    private bool _pressOnPot;

    // Put a card in, or brew, answering the card prompt with the card that was dragged.
    private void PotDo(string action, CardInstanceId? card)
    {
        if (Play?.Seat is not { } driver || driver.Current is not { } now)
            return;
        var id = new CardDefinitionId(action);
        var target = FirstLivingEnemy(now);
        if (!now.CanUse(id, target))
        {
            Toast(WhyNot(now, id, now.ActionCost(id, target).Sum(c => c.Amount)) ?? "The cauldron cannot do that now.");
            return;
        }
        _armedCard = null;
        driver.UseAction(id, target);
        if (card is { } chosen && driver.PendingCardChoice is { } offered && offered.Any(o => o.Id == chosen))
            driver.SupplyCardChoice([chosen]);
        SurfaceNewProblems();
        GameHost.Instance.AutoSave();
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

    // THE CAULDRON, WALKED (`--smoke-cauldron --character=hedge_witch`): into the first fight, cards dragged from the
    // hand onto the pot with real mouse events — two, a turn ended, the third with no Energy left (it is free) — a
    // picture of the Ready pot, then a click on it, and what the brew did, in numbers.
    private async System.Threading.Tasks.Task SmokeCauldron()
    {
        var session = Session;
        for (var guard = 0; guard < 10 && session is not null && Play?.Seat?.Current is null; guard++)
        {
            if (session.IsAwaitingNodeChoice)
                session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else
                break;
        }
        if (Play?.Seat is not { Current: { } } driver || !driver.Current!.Actions.Any(a => a.value == CauldronAction))
        {
            GD.Print("smoke-cauldron: no fight with a cauldron reached");
            GetTree().Quit();
            return;
        }
        await Frames(4);
        await DragIntoThePot("turn 1, first (free)");
        await DragIntoThePot("turn 1, second (1 Energy)");
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
        Rebuild();
        await Frames(4);
        await DragIntoThePot("turn 2, no Energy left");

        Rebuild();
        await Frames(4);
        var combat = driver.Current!;
        var pot = combat.State.GetCardZones(combat.HeroId).SetAside;
        GD.Print($"smoke-cauldron: pot={pot.Count} [{string.Join(", ", pot.Select(c => c.DefinitionId.value))}] · "
            + (_potView?.TooltipText.Split("\n\n")[0] ?? "(no pot shown)"));
        if (!DisplayServer.GetName().Contains("headless"))
        {
            GetViewport().GetTexture().GetImage().SavePng("user://smoke-cauldron.png");
            GD.Print("smoke: screenshot user://smoke-cauldron.png");
        }

        var enemy = FirstLivingEnemy(combat);
        var before = enemy is { } e ? combat.State.GetCombatant(e).Health.Current : 0;
        var brewable = combat.CanUse(new CardDefinitionId(BrewAction), enemy);
        if (_potView is { } view && IsInstanceValid(view))
            await Click(view.GetGlobalRect().GetCenter());
        if (driver.Current is not { } after)
            GD.Print($"smoke-cauldron [brew by click]: allowed={brewable} · the brew ended the fight");
        else
            GD.Print($"smoke-cauldron [brew by click]: allowed={brewable} · pot now {after.State.GetCardZones(after.HeroId).SetAside.Count}"
                + $" · enemy HP {before}→{(enemy is { } id ? after.State.GetCombatant(id).Health.Current : 0)}"
                + $" · hero block {after.HeroGuard} · error={Session?.Error ?? Play?.Error ?? "none"}");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // One card dragged from the hand onto the pot, and what the pot and the Energy said before and after.
    private async System.Threading.Tasks.Task DragIntoThePot(string when)
    {
        var driver = Play!.Seat!;
        var before = driver.Current!;
        var potBefore = before.State.GetCardZones(before.HeroId).SetAside.Count;
        var energyBefore = before.HeroEnergy;
        var face = _handFaces.FirstOrDefault(f => IsInstanceValid(f));
        if (face is null || _potView is not { } pot || !IsInstanceValid(pot))
        {
            GD.Print($"smoke-cauldron [{when}]: nothing to drag (hand {_handFaces.Count}, pot {(_potView is null ? "missing" : "drawn")})");
            return;
        }
        await Drag(face.GetGlobalRect().GetCenter() + new Vector2(0, 30), pot.GetGlobalRect().GetCenter());
        var after = driver.Current!;
        GD.Print($"smoke-cauldron [{when}]: pot {potBefore}→{after.State.GetCardZones(after.HeroId).SetAside.Count}"
            + $" · energy {energyBefore}→{after.HeroEnergy} · error={Session?.Error ?? Play?.Error ?? "none"}");
        Rebuild();
        await Frames(4);
    }
}
