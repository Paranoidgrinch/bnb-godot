using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// THE SORCERER'S APPRENTICE'S WAND on the fight screen (sorcerers_apprentice_master.md §14.4): a row of slots with
// what each holds and what it will do in the next cast, the two meters — Focus and Exhaustion — and a button for each
// of the five actions with what it costs right now. A greyed-out button says why. Cast aims like a card: with more
// than one enemy it waits for a click on one, and pointing at an enemy shows what the cast would do to it.
//
// What a cast will do is the engine's own plan (WandCompiler): the order, which Speak each modifier reaches, what
// fizzles, the Focus and the Exhaustion. The numbers on the health bars are the cast played out on a copy of the fight,
// as every card preview is (SessionPreview).
public partial class SessionScreen
{
    private const string WandInscribe = "wand_inscribe";
    private const string WandCast = "wand_cast";
    private const string WandSwap = "wand_swap";
    private const string WandWithdraw = "wand_withdraw";
    private const string WandBurnOut = "wand_burn_out";
    private static readonly string[] WandActionIds = [WandInscribe, WandCast, WandSwap, WandWithdraw, WandBurnOut];

    // The cast waiting for a target click (like _armedCard for a card).
    private bool _armedCast;

    // How often the wand view was drawn — the smoke probe reads it.
    public static int WandsDrawn { get; private set; }

    private static bool IsWandAction(CardDefinitionId action) => WandActionIds.Contains(action.value);

    private HBoxContainer WandRow(ICombatView combat)
    {
        WandsDrawn++;
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore, Name = "WandRow" };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(WandView(combat));
        foreach (var action in combat.Actions.Where(IsWandAction))
            row.AddChild(WandButton(combat, action));
        return row;
    }

    private static string CardName(ICombatView combat, CardInstanceId id)
    {
        var zones = combat.State.GetCardZones(combat.HeroId);
        if (!zones.ContainsCard(id))
            return "?";
        var definition = zones.GetCard(id).DefinitionId.value;
        return GameHost.Instance.Blueprint.Cards.FirstOrDefault(c => c.Id == definition)?.NameKey ?? definition;
    }

    private static WandPlan? PlanFor(ICombatView combat, CombatantId? target)
    {
        if (combat.Registry.Wand is null || combat.State.GetWand(combat.HeroId) is null)
            return null;
        try
        {
            return WandCompiler.Compile(combat.State, combat.Registry, combat.HeroId, target);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"wand plan: {ex.Message}");
            return null;
        }
    }

    // ── the wand itself ────────────────────────────────────────────────────────────────────────────────────

    private Control WandView(ICombatView combat)
    {
        var wand = combat.State.GetWand(combat.HeroId);
        var rules = combat.Registry.Wand;
        var box = new PanelContainer { Name = "Wand", TooltipText = "The wand." };
        box.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(MoonvineTheme.BgPanelStrong, MoonvineTheme.Hairline, radius: 6));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        box.AddChild(row);
        if (wand is null || rules is null)
        {
            row.AddChild(new Label { Text = "Wand", VerticalAlignment = VerticalAlignment.Center });
            return box;
        }

        // The two meters. Exhaustion is the wand's LOAD, never called "Exhaust" (that is the card pile).
        var focus = new Label
        {
            Text = $"Focus ◆{wand.Focus}/{rules.MaxFocus}",
            Name = "WandFocus",
            VerticalAlignment = VerticalAlignment.Center,
            TooltipText = "Every card you play gives 1 Focus (up to 6). Inscribing, swapping, withdrawing and casting cost it. It is kept from turn to turn.",
        };
        focus.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
        row.AddChild(focus);
        var strain = new Label
        {
            Text = $"Exhaustion {wand.Exhaustion}/{wand.ExhaustionCap}",
            Name = "WandExhaustion",
            VerticalAlignment = VerticalAlignment.Center,
            TooltipText = $"How strained the wand is. A cast may not take it past {wand.ExhaustionCap}; it falls by {rules.Recovery} at the start of your turn.",
        };
        strain.AddThemeColorOverride("font_color",
            wand.Exhaustion >= wand.ExhaustionCap - 2 ? MoonvineTheme.Harm : MoonvineTheme.TextSoft);
        row.AddChild(strain);

        var plan = PlanFor(combat, AimedEnemy(combat));
        var roles = plan is null ? new Dictionary<int, string>() : Roles(combat, plan);
        for (var slot = 0; slot < wand.Capacity; slot++)
        {
            var held = wand.Slots[slot];
            var chip = new Label { VerticalAlignment = VerticalAlignment.Center, Name = $"WandSlot{slot + 1}" };
            chip.AddThemeFontSizeOverride("font_size", 12);
            if (held is not { } id)
            {
                chip.Text = $"[{slot + 1} ·]";
                chip.TooltipText = "An empty slot.";
                chip.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
            }
            else
            {
                var name = CardName(combat, id);
                var role = roles.GetValueOrDefault(slot);
                chip.Text = $"[{slot + 1} {name}]";
                var definition = combat.State.GetCardZones(combat.HeroId).GetCard(id).DefinitionId;
                var words = WandWords(definition.value);
                chip.TooltipText = string.Join("\n", new[] { name, words, role }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var fizzled = role?.StartsWith("fizzles", StringComparison.Ordinal) == true
                    || role?.StartsWith("cut off", StringComparison.Ordinal) == true;
                chip.AddThemeColorOverride("font_color", fizzled ? MoonvineTheme.TextMuted : OperationColor(combat, definition));
            }
            row.AddChild(chip);
        }

        if (wand.StoredSpell is { } stored)
        {
            var kept = new Label
            {
                Text = "◆ kept",
                Name = "WandStored",
                VerticalAlignment = VerticalAlignment.Center,
                TooltipText = $"A spell is kept for {StoredWhen(stored.Trigger)}: {Describe(stored.Effects)}.",
            };
            kept.AddThemeColorOverride("font_color", MoonvineTheme.Signal);
            row.AddChild(kept);
        }
        if (wand.AshTraces.Count > 0)
        {
            var ash = new Label
            {
                Text = $"ash ×{wand.AshTraces.Count}",
                VerticalAlignment = VerticalAlignment.Center,
                TooltipText = $"Your next Burn Out also deals {string.Join(", then ", wand.AshTraces)} damage.",
            };
            ash.AddThemeColorOverride("font_color", MoonvineTheme.Copper);
            row.AddChild(ash);
        }
        if (plan is not null)
            box.TooltipText = PlanText(combat, plan);
        return box;
    }

    private static string StoredWhen(WandStoreTrigger trigger) => trigger switch
    {
        WandStoreTrigger.NextBurnOut => "your next Burn Out",
        WandStoreTrigger.NextEnemyAttack => "the first enemy attack this enemy turn",
        _ => "your next cast",
    };

    private static string Describe(IEnumerable<WandFrozenEffect> effects) =>
        string.Join(", ", effects.Select(e => e.Kind switch
        {
            WandEffectKind.Damage => e.Hits > 1 ? $"{e.Amount} damage ×{e.Hits}" : $"{e.Amount} damage",
            WandEffectKind.Block => $"{e.Amount} Block",
            WandEffectKind.WardWax => $"{e.Amount} Ward Wax",
            _ => $"{e.Amount} {StatusName(e.Status?.value)}",
        }));

    private static string StatusName(string? id) =>
        id is null ? "" : GameHost.Instance.Blueprint.Statuses.FirstOrDefault(s => s.Id == id)?.NameKey ?? id;

    // What each slot does in the cast the plan describes, by slot.
    private static Dictionary<int, string> Roles(ICombatView combat, WandPlan plan)
    {
        var roles = new Dictionary<int, string>();
        foreach (var step in plan.Steps)
        {
            string role;
            if (step.Fizzled)
                role = $"fizzles: {step.FizzleReason}";
            else if (step.Imprint.Operation == WandOperation.Speak)
                role = step.JoinedInto is { } into ? $"joined into slot {plan.Steps[into].Slot + 1}" : $"speaks ({step.Kind.ToString().ToLowerInvariant()})";
            else if (step.Target is { } target)
                role = $"{step.Imprint.Operation} → slot {plan.Steps[target].Slot + 1}";
            else if (step.Source is { } source)
                role = $"{step.Imprint.Operation} copies slot {plan.Steps[source].Slot + 1}";
            else
                role = step.Imprint.Operation.ToString();
            if (step.Ground > 0)
                role += $" · Exhaustion −{step.Ground}";
            roles[step.Slot] = role;
        }
        foreach (var slot in plan.Skipped)
            roles[slot] = "cut off: a Stop ends the cast before it";
        _ = combat;
        return roles;
    }

    // The whole plan in words: the order, the bill, and why it may not happen.
    private static string PlanText(ICombatView combat, WandPlan plan)
    {
        var lines = new List<string> { "Next cast:" };
        foreach (var step in plan.Steps)
        {
            var role = Roles(combat, plan).GetValueOrDefault(step.Slot);
            lines.Add($"  {step.Slot + 1}. {CardName(combat, step.Card)} — {role}");
        }
        if (plan.Boosts.Count > 0)
            lines.Add($"  boosted by {string.Join(", ", plan.Boosts.Select(b => $"{b.Percent}%"))}");
        lines.Add($"Focus {plan.FocusCost + plan.ExtraFocus} · Exhaustion +{plan.Exhaustion} → {plan.ExhaustionAfter}/{plan.ExhaustionCap}");
        if (!plan.IsLegal)
            lines.Add($"Not now: {plan.Illegal}");
        return string.Join("\n", lines);
    }

    private static Color OperationColor(ICombatView combat, CardDefinitionId definition)
    {
        if (!combat.Registry.CardDefinitions.TryGetValue(definition, out var card) || card.Wand is not { } imprint)
            return MoonvineTheme.TextSoft;
        return imprint.Operation switch
        {
            WandOperation.Speak => WandCompiler.KindOf(imprint.Atoms, combat.Registry) switch
            {
                WandSpeakKind.Damage => MoonvineTheme.Harm,
                WandSpeakKind.Defense => MoonvineTheme.Steel,
                _ => MoonvineTheme.Copper,
            },
            WandOperation.Ground or WandOperation.Burn or WandOperation.Stop => MoonvineTheme.Signal,
            _ => MoonvineTheme.Arcane,
        };
    }

    // ── the five actions ───────────────────────────────────────────────────────────────────────────────────

    private Button WandButton(ICombatView combat, CardDefinitionId action)
    {
        var wand = combat.State.GetWand(combat.HeroId);
        var rules = combat.Registry.Wand;
        var target = AimedEnemy(combat);
        var name = GameHost.Instance.Blueprint.Cards.FirstOrDefault(c => c.Id == action.value)?.NameKey ?? action.value;
        var plan = action.value == WandCast ? PlanFor(combat, target) : null;
        // ◆ is Focus; the Exhaustion a cast adds or Burn Out takes off follows it.
        var price = action.value switch
        {
            WandInscribe => wand is { FreeInscriptionUsed: false } ? "free" : $"◆{rules?.InscribeCost ?? 1}",
            WandCast => plan is null ? "" : $"◆{plan.FocusCost + plan.ExtraFocus} +{plan.Exhaustion}",
            WandSwap => $"◆{rules?.SwapCost ?? 1}",
            WandWithdraw => $"◆{rules?.WithdrawCost ?? 1}",
            WandBurnOut => $"−{rules?.BurnOutRelief ?? 3}",
            _ => "",
        };
        var button = new Button
        {
            Text = price.Length == 0 ? name : $"{name} {price}",
            Name = $"Action_{action.value}",
            Disabled = !combat.CanUse(action, target),
            TooltipText = GameHost.Instance.Blueprint.Presentation.Cards.GetValueOrDefault(action.value)?.FlavorText ?? "",
        };
        if (action.value == WandCast && plan is not null)
            button.TooltipText = $"{PlanText(combat, plan)}\n\n{button.TooltipText}";
        if (button.Disabled && WandWhyNot(combat, action, target) is { } why)
            button.TooltipText = $"{why}\n\n{button.TooltipText}";
        if (action.value == WandCast)
        {
            if (_armedCast)
                button.Text = "Cast — choose a target";
            else if (!button.Disabled)
                button.Ready += () => Pulse(button);
            button.MouseEntered += () =>
            {
                if (Play?.Seat?.Current is { } now)
                    ShowCastPreview(now, AimedEnemy(now));
            };
            button.MouseExited += RestorePreview;
        }
        var chosen = action;
        button.Pressed += () => UseWandAction(chosen);
        return button;
    }

    private static string? WandWhyNot(ICombatView combat, CardDefinitionId action, CombatantId? target)
    {
        if (!combat.IsHeroTurn)
            return "Not now: it is not your turn.";
        if (combat.Registry.Wand is not { } rules || combat.State.GetWand(combat.HeroId) is not { } wand)
            return null;
        var move = action.value switch
        {
            WandInscribe => WandAction.Inscribe,
            WandCast => WandAction.Cast,
            WandSwap => WandAction.Swap,
            WandWithdraw => WandAction.Withdraw,
            _ => WandAction.BurnOut,
        };
        return WandActions.Why(combat.State, combat.Registry, rules, wand, combat.HeroId, target, move)
            ?? "A rule in this fight forbids it right now.";
    }

    private void UseWandAction(CardDefinitionId action)
    {
        if (Play?.Seat is not { } driver || driver.Current is not { } now)
            return;
        _armedCard = null;
        // A cast aims like a card: with more than one enemy, the next click on one is the target.
        if (action.value == WandCast && LivingEnemies(now).Count > 1)
        {
            _armedCast = !_armedCast;
            Rebuild();
            return;
        }
        _armedCast = false;
        driver.UseAction(action, AimedEnemy(now));
        SurfaceNewProblems();
        GameHost.Instance.AutoSave();
    }

    private void CastAt(CombatantId target)
    {
        _armedCast = false;
        if (Play?.Seat is not { } driver)
            return;
        driver.UseAction(new CardDefinitionId(WandCast), target);
        SurfaceNewProblems();
        GameHost.Instance.AutoSave();
    }

    // The cast played out on a copy of the fight, its changes shown on the health bars — as a card's are.
    private void ShowCastPreview(ICombatView combat, CombatantId? target)
    {
        ClearPreview();
        var cast = new CardDefinitionId(WandCast);
        if (!GameplaySettings.DamageCalculator || !combat.IsHeroTurn || !combat.CanUse(cast, target))
            return;
        Dictionary<string, Stand> before, after;
        try
        {
            before = Standing(combat);
            after = Standing(combat switch
            {
                InteractiveCombat solo => Cast(solo.Fork(), target),
                PartyCombatSeatView seat => CastSeat(seat, target),
                _ => combat,
            });
        }
        catch (Exception ex)
        {
            GD.PushWarning($"cast preview: {ex.Message}");
            return;
        }
        foreach (var (id, then) in before)
            if (after.TryGetValue(id, out var now) && _healthBars.TryGetValue(id, out var bar) && IsInstanceValid(bar.Bar))
                Decorate(bar.Bar, bar.Max, then, now, null);

        static ICombatView Cast(InteractiveCombat fork, CombatantId? at)
        {
            fork.UseAction(new CardDefinitionId(WandCast), at);
            return fork;
        }
        static ICombatView CastSeat(PartyCombatSeatView seat, CombatantId? at)
        {
            var copy = seat.Combat.Fork();
            copy.UseAction(seat.HeroId, new CardDefinitionId(WandCast), at);
            return new PartyCombatSeatView(copy, seat.HeroId);
        }
    }

    // ── a card's words, with its wand part ─────────────────────────────────────────────────────────────────

    // Does the hero this screen is drawn for carry a wand? In a fight: the fight says so. Between fights: the run's
    // character brings the Cast action into every fight.
    private bool HoldsAWand()
    {
        if (Play?.Seat?.Current is { } combat)
            return combat.Actions.Any(a => a.value == WandCast);
        return Session?.Run is { } run && run.CombatActions.Any(a => a.value == WandCast);
    }

    // The wand part of a general card in words (Extra["wand"]), or the card's own "Wand:" line.
    private static string WandWords(string definitionId)
    {
        var look = GameHost.Instance.Blueprint.Presentation.Cards.GetValueOrDefault(definitionId);
        if (look?.Extra.GetValueOrDefault("wand") is { } proxy)
            return "Wand: " + proxy;
        var (_, wand) = CardVisuals.SplitWand(look?.FlavorText ?? "");
        return wand is null ? "" : "Wand: " + wand;
    }

    // What a card face says: its rules — and, for a general card held by a wand bearer, its wand part as well.
    private string RulesFor(string definitionId, RogueDeck.Run.EntityPresentation? presentation)
    {
        var rules = presentation?.FlavorText ?? "";
        if (presentation?.Extra.GetValueOrDefault("wand") is { } proxy && HoldsAWand()
            && !rules.Contains(CardVisuals.WandLine, StringComparison.Ordinal))
            rules += CardVisuals.WandLine + proxy;
        _ = definitionId;
        return rules;
    }

    // ── THE WAND, WALKED (`--smoke-wand --character=sorcerers_apprentice`) ─────────────────────────────────
    // Into the first fight; two cards inscribed through the real Inscribe button and the card prompt; cards
    // played for Focus; a picture of the loaded wand; then the real Cast button — and what it did, in numbers.
    private async System.Threading.Tasks.Task SmokeWand()
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
        if (Play?.Seat is not { Current: { } } driver)
        {
            GD.Print("smoke-wand: no fight reached");
            GetTree().Quit();
            return;
        }
        if (!driver.Current!.Actions.Any(a => a.value == WandCast))
        {
            GD.Print("smoke-wand: this hero has no wand");
            GetTree().Quit();
            return;
        }

        await PressInscribe("first, free");
        // Focus for the second inscription and the cast: play what can be played, keeping one card back.
        for (var guard = 0; guard < 6 && driver.Current!.State.GetWand(driver.Current.HeroId)!.Focus < 3; guard++)
        {
            var now = driver.Current;
            if (now.Hand.Count <= 1 || now.Hand.FirstOrDefault(c => now.CanPlay(c.Id)) is not { } card)
                break;
            driver.PlayCard(card.Id, FirstLivingEnemy(now));
            await Frames(2);
            if (driver.PendingCardChoice is { Count: > 0 } offered)
                driver.SupplyCardChoice([offered[0].Id]);
            if (driver.PendingOptionChoice is { Count: > 0 })
                driver.SupplyOptionChoice([0]);
        }
        await PressInscribe("second, 1 Focus");
        Rebuild();
        await Frames(4);
        var combat = driver.Current!;
        var wand = combat.State.GetWand(combat.HeroId)!;
        var cast = FindChild($"Action_{WandCast}", recursive: true, owned: false) as Button;
        GD.Print($"smoke-wand: slots [{string.Join(", ", wand.Slots.Select(s => s is { } id ? CardName(combat, id) : "·"))}] "
            + $"focus {wand.Focus} exhaustion {wand.Exhaustion}/{wand.ExhaustionCap} · cast button "
            + $"'{cast?.Text}'{(cast?.Disabled == true ? " DISABLED" : "")} · views {WandsDrawn}");
        GD.Print("smoke-wand: plan " + (cast?.TooltipText ?? "").Split("\n\n")[0].Replace("\n", " | "));
        GetViewport().GetTexture().GetImage().SavePng("user://smoke-wand.png");
        GD.Print("smoke: screenshot user://smoke-wand.png");

        var enemy = FirstLivingEnemy(combat);
        var before = enemy is { } e ? combat.State.GetCombatant(e).Health.Current : 0;
        if (cast is { Disabled: false })
        {
            cast.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            if (_armedCast && enemy is { } aimed)
                CastAt(aimed);
            await Frames(2);
        }
        var after = driver.Current!;
        var afterWand = after.State.GetWand(after.HeroId)!;
        GD.Print($"smoke-wand: cast · enemy HP {before}→{(enemy is { } id2 ? after.State.GetCombatant(id2).Health.Current : 0)}"
            + $" · focus {afterWand.Focus} exhaustion {afterWand.Exhaustion} casts {afterWand.CastsThisTurn}"
            + $" · error={Session?.Error ?? Play?.Error ?? "none"}");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task PressInscribe(string when)
    {
        await Frames(2);
        var driver = Play!.Seat!;
        if (FindChild($"Action_{WandInscribe}", recursive: true, owned: false) is not Button button)
        {
            GD.Print($"smoke-wand [{when}]: no Inscribe button on screen");
            return;
        }
        var label = $"{button.Text}{(button.Disabled ? " DISABLED" : "")}";
        if (button.Disabled)
        {
            GD.Print($"smoke-wand [{when}]: button {label} · {button.TooltipText.Split('\n')[0]}");
            return;
        }
        button.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        if (driver.PendingCardChoice is { Count: > 0 } offered)
        {
            OnCardChoiceClicked(Play, offered, offered[0].Id);
            await Frames(2);
        }
        if (driver.PendingOptionChoice is { Count: > 0 })
        {
            driver.SupplyOptionChoice([0]);
            await Frames(2);
        }
        var wand = driver.Current!.State.GetWand(driver.Current.HeroId)!;
        GD.Print($"smoke-wand [{when}]: button {label} · wand holds {wand.Occupied} · focus {wand.Focus}"
            + $" · error={Session?.Error ?? Play?.Error ?? "none"}");
    }
}
