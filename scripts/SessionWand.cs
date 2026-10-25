using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// THE SORCERER'S APPRENTICE'S WAND on the fight screen (sorcerers_apprentice_master.md §14.4; playtest 3, B8). It is
// part of her, not a row of buttons: Focus and Exhaustion stand beside the Energy under her body, and over her effects
// she holds a STAFF with its four slots. Everything is done to the staff with the pointer:
//
//   drag a card from the hand onto a slot   Inscribe (into that slot, or the first free one)
//   drag a slot onto another slot            Swap the two
//   drag a slot off the staff                Withdraw it
//   click the staff                          Cast (aimed like a card when there is more than one enemy)
//   right-click a slot                       Burn Out
//
// The gesture answers the prompts the engine's action raises (which card, which slot) with what was dragged, so
// there is no second way of doing anything. What a cast will do is the engine's own plan (WandCompiler): the order,
// which Speak each modifier reaches, what fizzles, the Focus and the Exhaustion, on the staff's hover. The numbers on
// the health bars are the cast played out on a copy of the fight, as every card preview is (SessionPreview).
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

    // ── the wand itself: two meters beside the Energy, and the staff ─────────────────────────────────────────

    private Control? WandMeters(ICombatView combat)
    {
        if (combat.State.GetWand(combat.HeroId) is not { } wand || combat.Registry.Wand is not { } rules)
            return null;
        WandsDrawn++;
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 10);
        var focus = new Label
        {
            Text = $"◆{wand.Focus}/{rules.MaxFocus}",
            Name = "WandFocus",
            TooltipText = $"Focus. Every card you play gives 1 (up to {rules.MaxFocus}). Inscribing, swapping, withdrawing "
                + "and casting cost it. It is kept from turn to turn.",
            MouseFilter = MouseFilterEnum.Pass,
        };
        focus.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
        row.AddChild(focus);
        // Exhaustion is the wand's LOAD, never called "Exhaust" (that is the card pile).
        var strain = new Label
        {
            Text = $"Exh {wand.Exhaustion}/{wand.ExhaustionCap}",
            Name = "WandExhaustion",
            TooltipText = $"Exhaustion: how strained the wand is. A cast may not take it past {wand.ExhaustionCap}; "
                + $"it falls by {rules.Recovery} at the start of your turn.",
            MouseFilter = MouseFilterEnum.Pass,
        };
        strain.AddThemeColorOverride("font_color",
            wand.Exhaustion >= wand.ExhaustionCap - 2 ? MoonvineTheme.Harm : MoonvineTheme.TextSoft);
        row.AddChild(strain);
        return row;
    }

    // The staff's sockets as last drawn, for the drop test.
    private readonly List<(int Slot, Control Socket)> _wandSockets = [];
    private Control? _wandStaff;
    private const int StaffHeight = 40;

    private Control? WandStaff(ICombatView combat, int width)
    {
        _wandSockets.Clear();
        _wandStaff = null;
        if (combat.State.GetWand(combat.HeroId) is not { } wand || combat.Registry.Wand is null)
            return null;

        var plan = PlanFor(combat, AimedEnemy(combat));
        var roles = plan is null ? new Dictionary<int, string>() : Roles(combat, plan);
        var castable = combat.CanUse(new CardDefinitionId(WandCast), AimedEnemy(combat));

        // The staff: a length of dark wood with brass at the ends, the slots set into it.
        var staff = new PanelContainer
        {
            Name = "WandRow",
            CustomMinimumSize = new Vector2(width, StaffHeight),
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = castable ? CursorShape.PointingHand : CursorShape.Arrow,
        };
        var wood = new StyleBoxFlat
        {
            BgColor = new Color("3b2616"),
            BorderColor = _armedCast ? MoonvineTheme.Signal : castable ? MoonvineTheme.Copper : new Color("5a3d24"),
            CornerRadiusTopLeft = 14, CornerRadiusBottomLeft = 14, CornerRadiusTopRight = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 8, ContentMarginRight = 6, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        wood.BorderWidthTop = wood.BorderWidthBottom = 2;
        wood.BorderWidthLeft = wood.BorderWidthRight = 3;
        staff.AddThemeStyleboxOverride("panel", wood);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", 4);
        staff.AddChild(row);

        for (var slot = 0; slot < wand.Capacity; slot++)
        {
            var held = wand.Slots[slot];
            var socket = new PanelContainer
            {
                Name = $"WandSlot{slot + 1}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Pass,
            };
            var inset = MoonvineTheme.Panel(new Color("1c120a"), new Color("6e4b2a"), radius: 4);
            inset.ContentMarginLeft = inset.ContentMarginRight = 2;
            inset.ContentMarginTop = inset.ContentMarginBottom = 1;
            socket.AddThemeStyleboxOverride("panel", inset);
            // ⚠ NOT a wrapping label: wrapped, it is given no width inside the socket and draws nothing. A name too
            // long for the socket is cut with an ellipsis; the hover has it whole.
            var label = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CustomMinimumSize = new Vector2((width - 30) / wand.Capacity - 6, 26),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                ClipText = true,
                MouseFilter = MouseFilterEnum.Pass,
            };
            label.AddThemeFontSizeOverride("font_size", 10);
            if (held is not { } id)
            {
                label.Text = $"{slot + 1}";
                label.AddThemeColorOverride("font_color", new Color("6e4b2a"));
                socket.TooltipText = $"Slot {slot + 1}, empty. Drag a card from your hand here to inscribe it.";
            }
            else
            {
                var name = CardName(combat, id);
                var role = roles.GetValueOrDefault(slot);
                label.Text = name;
                var definition = combat.State.GetCardZones(combat.HeroId).GetCard(id).DefinitionId;
                socket.TooltipText = string.Join("\n", new[]
                {
                    $"Slot {slot + 1}: {name}", WandWords(definition.value), role,
                    "Drag onto another slot to swap, off the staff to withdraw. Right-click: Burn Out.",
                }.Where(t => !string.IsNullOrWhiteSpace(t)));
                var fizzled = role?.StartsWith("fizzles", StringComparison.Ordinal) == true
                    || role?.StartsWith("cut off", StringComparison.Ordinal) == true;
                label.AddThemeColorOverride("font_color", fizzled ? MoonvineTheme.TextMuted : OperationColor(combat, definition));
                var card = id;
                var at = slot;
                socket.GuiInput += input =>
                {
                    if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } press)
                    {
                        _wandPress = (card, at, press.GlobalPosition);
                        socket.AcceptEvent();
                    }
                    else if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
                    {
                        WandDo(WandBurnOut, [card], null);
                        socket.AcceptEvent();
                    }
                };
            }
            socket.AddChild(label);
            row.AddChild(socket);
            _wandSockets.Add((slot, socket));
        }

        // A kept spell and ash traces are written on the staff's hover with the plan.
        var notes = new List<string>();
        if (plan is not null)
            notes.Add(PlanText(combat, plan));
        if (wand.StoredSpell is { } stored)
            notes.Add($"A spell is kept for {StoredWhen(stored.Trigger)}: {Describe(stored.Effects)}.");
        if (wand.AshTraces.Count > 0)
            notes.Add($"Your next Burn Out also deals {string.Join(", then ", wand.AshTraces)} damage.");
        notes.Add(castable ? "Click the staff to cast." : WandWhyNot(combat, new CardDefinitionId(WandCast), AimedEnemy(combat)) ?? "");
        staff.TooltipText = string.Join("\n\n", notes.Where(n => n.Length > 0));

        // A click on the staff itself (not dragging a slot) casts.
        staff.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } press)
            {
                _wandPress = (null, null, press.GlobalPosition);
                staff.AcceptEvent();
            }
        };
        staff.MouseEntered += () =>
        {
            if (Play?.Seat?.Current is { } now)
                ShowCastPreview(now, AimedEnemy(now));
        };
        staff.MouseExited += RestorePreview;
        if (castable && !_armedCast)
            staff.Ready += () => Pulse(staff);
        _wandStaff = staff;
        return staff;
    }

    // ── the gesture ────────────────────────────────────────────────────────────────────────────────────────

    // What the pointer went down on: a card (from the hand when Slot is null, from the staff when it is not), or the
    // staff itself (no card). It becomes a drag once it has moved far enough to not be a click.
    private (CardInstanceId? Card, int? Slot, Vector2 Start)? _wandPress;
    private bool _wandDragging;
    private Control? _wandGhost;
    private const float DragThreshold = 10f;

    // A card in the hand can be picked up for the staff or the pot (BuildHand calls this for a hero carrying either).
    private void LetWandTake(Control face, CardInstanceId card)
    {
        var listener = face.GetChildren().OfType<Button>().LastOrDefault() as Control ?? face;
        listener.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } press)
                _wandPress = (card, null, press.GlobalPosition);
        };
    }

    // Called first from _Input. True when the event was the staff's and nothing else should see it.
    private bool WandGesture(InputEvent @event)
    {
        if (_wandPress is not { } press)
            return false;
        switch (@event)
        {
            case InputEventMouseMotion motion:
                if (!_wandDragging && press.Card is not null && motion.GlobalPosition.DistanceTo(press.Start) > DragThreshold)
                {
                    _wandDragging = true;
                    _wandGhost = WandGhost(press.Card.Value);
                }
                if (_wandGhost is { } ghost && IsInstanceValid(ghost))
                    ghost.GlobalPosition = motion.GlobalPosition + new Vector2(12, -10);
                return false;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } release:
                var dragged = _wandDragging;
                var onPot = _pressOnPot;
                _pressOnPot = false;
                _wandPress = null;
                _wandDragging = false;
                if (_wandGhost is { } old && IsInstanceValid(old))
                    old.QueueFree();
                _wandGhost = null;
                if (!dragged)
                {
                    // A click on the pot brews.
                    if (onPot)
                    {
                        PotDo(BrewAction, null);
                        return true;
                    }
                    // A click on the staff or one of its slots casts; a click on a hand card is the card's own.
                    if (press.Card is null || press.Slot is not null)
                    {
                        UseWandAction(new CardDefinitionId(WandCast));
                        return true;
                    }
                    return false;
                }
                Drop(press.Card!.Value, press.Slot, release.GlobalPosition);
                return true;
            default:
                return false;
        }
    }

    private void Drop(CardInstanceId card, int? fromSlot, Vector2 at)
    {
        // A hand card dropped on the pot goes in.
        if (fromSlot is null && _potView is { } potView && IsInstanceValid(potView) && potView.GetGlobalRect().Grow(6).HasPoint(at))
        {
            PotDo(CauldronAction, card);
            return;
        }
        var onSocket = _wandSockets.FirstOrDefault(s => IsInstanceValid(s.Socket) && s.Socket.GetGlobalRect().HasPoint(at));
        var onStaff = _wandStaff is { } staff && IsInstanceValid(staff) && staff.GetGlobalRect().Grow(6).HasPoint(at);
        if (Play?.Seat?.Current is not { } now || now.State.GetWand(now.HeroId) is not { } wand)
            return;
        if (fromSlot is null)
        {
            if (!onStaff && onSocket.Socket is null)
                return;
            int? slot = onSocket.Socket is not null && wand.Slots[onSocket.Slot] is null ? onSocket.Slot : null;
            WandDo(WandInscribe, [card], slot);
            return;
        }
        if (onSocket.Socket is not null && onSocket.Slot != fromSlot && wand.Slots[onSocket.Slot] is { } other)
            WandDo(WandSwap, [card, other], null);
        else if (!onStaff)
            WandDo(WandWithdraw, [card], null);
    }

    // What follows the pointer while a card is carried: its name on a small plate.
    private Control WandGhost(CardInstanceId card)
    {
        var ghost = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 100, TopLevel = true };
        ghost.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(MoonvineTheme.BgRaised, MoonvineTheme.Copper, radius: 4));
        var label = new Label
        {
            Text = Play?.Seat?.Current is { } now ? CardName(now, card) : "",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", MoonvineTheme.AccentLight);
        ghost.AddChild(label);
        AddChild(ghost);
        ghost.GlobalPosition = GetViewport().GetMousePosition();
        return ghost;
    }

    // Use one of the wand's actions, answering the prompts it raises with what the gesture named: the cards (in the
    // order given) and, for an inscription, the slot.
    private void WandDo(string action, IReadOnlyList<CardInstanceId> cards, int? slot)
    {
        if (Play?.Seat is not { } driver || driver.Current is not { } now)
            return;
        var id = new CardDefinitionId(action);
        if (!now.CanUse(id, AimedEnemy(now)))
        {
            Toast(WandWhyNot(now, id, AimedEnemy(now)) ?? "The wand cannot do that now.");
            return;
        }
        _armedCard = null;
        _armedCast = false;
        driver.UseAction(id, AimedEnemy(now));
        for (var asked = 0; asked < 4; asked++)
        {
            if (driver.PendingCardChoice is { } offered)
            {
                var wanted = cards.Where(c => offered.Any(o => o.Id == c)).Take(driver.PendingCardChoiceCount).ToList();
                if (wanted.Count != driver.PendingCardChoiceCount)
                    break;   // the player answers what the gesture cannot, on the ordinary prompt
                driver.SupplyCardChoice(wanted);
            }
            else if (driver.PendingOptionChoice is { } options && slot is { } chosen
                && options.ToList().IndexOf($"Slot {chosen + 1}") is var index and >= 0)
                driver.SupplyOptionChoice([index]);
            else
                break;
        }
        SurfaceNewProblems();
        GameHost.Instance.AutoSave();
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

    // ── why not, casting, and the preview ────────────────────────────────────────────────────────────────

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
    // Into the first fight, and every gesture done with real mouse events through the real input path: two hand cards
    // dragged onto the staff, cards played for Focus, the two slots swapped by a drag, a picture of the loaded staff,
    // then a click on the staff — and what each did, in numbers.
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
        if (Play?.Seat is not { Current: { } } driver || driver.Current!.State.GetWand(driver.Current.HeroId) is null)
        {
            GD.Print("smoke-wand: no fight with a wand reached");
            GetTree().Quit();
            return;
        }
        await Frames(4);

        await DragHandCardToSlot(0, "first, free");
        // Focus for the second inscription and the cast: play what can be played, keeping one card back.
        for (var guard = 0; guard < 6 && driver.Current!.State.GetWand(driver.Current.HeroId)!.Focus < 4; guard++)
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
        Rebuild();
        await Frames(4);
        await DragHandCardToSlot(1, "second, 1 Focus");

        var combat = driver.Current!;
        var wand = combat.State.GetWand(combat.HeroId)!;
        var before = string.Join(", ", wand.Slots.Select(s => s is { } id ? CardName(combat, id) : "·"));
        await Drag(SocketCentre(0), SocketCentre(1));
        combat = driver.Current!;
        wand = combat.State.GetWand(combat.HeroId)!;
        GD.Print($"smoke-wand [swap]: [{before}] → [{string.Join(", ", wand.Slots.Select(s => s is { } id ? CardName(combat, id) : "·"))}]"
            + $" · focus {wand.Focus} · error={Session?.Error ?? Play?.Error ?? "none"}");

        Rebuild();
        await Frames(4);
        GD.Print($"smoke-wand: staff {(_wandStaff is null ? "MISSING" : "drawn")} · meters "
            + $"{(FindChild("WandFocus", true, false) as Label)?.Text} {(FindChild("WandExhaustion", true, false) as Label)?.Text}");
        GD.Print("smoke-wand: plan " + (_wandStaff?.TooltipText ?? "").Split("\n\n")[0].Replace("\n", " | "));
        if (!DisplayServer.GetName().Contains("headless"))
        {
            GetViewport().GetTexture().GetImage().SavePng("user://smoke-wand.png");
            GD.Print("smoke: screenshot user://smoke-wand.png");
        }

        var enemy = FirstLivingEnemy(combat);
        var hp = enemy is { } e ? combat.State.GetCombatant(e).Health.Current : 0;
        if (_wandStaff is { } staff && IsInstanceValid(staff))
        {
            var centre = staff.GetGlobalRect().GetCenter();
            await Click(centre);
            if (_armedCast && enemy is { } aimed)
                CastAt(aimed);
            await Frames(2);
        }
        if (driver.Current is not { } after)
            GD.Print($"smoke-wand [cast by click]: the cast ended the fight · error={Session?.Error ?? Play?.Error ?? "none"}");
        else
        {
            var afterWand = after.State.GetWand(after.HeroId)!;
            GD.Print($"smoke-wand [cast by click]: enemy HP {hp}→{(enemy is { } id2 ? after.State.GetCombatant(id2).Health.Current : 0)}"
                + $" · focus {afterWand.Focus} exhaustion {afterWand.Exhaustion} casts {afterWand.CastsThisTurn}"
                + $" · error={Session?.Error ?? Play?.Error ?? "none"}");
        }
        GetTree().Quit();
    }

    private Vector2 SocketCentre(int slot) =>
        _wandSockets.FirstOrDefault(s => s.Slot == slot).Socket is { } socket && IsInstanceValid(socket)
            ? socket.GetGlobalRect().GetCenter()
            : Vector2.Zero;

    private async System.Threading.Tasks.Task DragHandCardToSlot(int slot, string when)
    {
        var driver = Play!.Seat!;
        var face = _handFaces.FirstOrDefault(f => IsInstanceValid(f));
        if (face is null || SocketCentre(slot) == Vector2.Zero)
        {
            GD.Print($"smoke-wand [{when}]: nothing to drag (hand {_handFaces.Count}, sockets {_wandSockets.Count})");
            return;
        }
        var from = face.GetGlobalRect().GetCenter() + new Vector2(0, 30);
        await Drag(from, SocketCentre(slot));
        var wand = driver.Current!.State.GetWand(driver.Current.HeroId)!;
        GD.Print($"smoke-wand [{when}]: dragged onto slot {slot + 1} · wand holds {wand.Occupied} · focus {wand.Focus}"
            + $" · slot {slot + 1} = {(wand.Slots[slot] is { } id ? CardName(driver.Current, id) : "empty")}"
            + $" · error={Session?.Error ?? Play?.Error ?? "none"}");
        Rebuild();
        await Frames(4);
    }

    // A press, a move in steps and a release, fed to the viewport as a player's mouse would be.
    private async System.Threading.Tasks.Task Drag(Vector2 from, Vector2 to)
    {
        Mouse(from, pressed: true);
        await Frames(2);
        for (var step = 1; step <= 6; step++)
        {
            var at = from.Lerp(to, step / 6f);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = MouseButtonMask.Left });
            await Frames(1);
        }
        Mouse(to, pressed: false);
        await Frames(4);
    }

    private async System.Threading.Tasks.Task Click(Vector2 at)
    {
        Mouse(at, pressed: true);
        await Frames(1);
        Mouse(at, pressed: false);
        await Frames(4);
    }

    private static void Mouse(Vector2 at, bool pressed) =>
        Input.ParseInputEvent(new InputEventMouseButton
        {
            Position = at, GlobalPosition = at, ButtonIndex = MouseButton.Left, Pressed = pressed,
            ButtonMask = pressed ? MouseButtonMask.Left : 0,
        });
}
