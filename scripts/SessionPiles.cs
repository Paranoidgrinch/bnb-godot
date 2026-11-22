using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// ── LOOKING AT A PILE ────────────────────────────────────────────────────────────
// What is left to draw, what has been discarded, what is gone for the fight, and the whole deck between
// rooms. The question "is my Defend still in there" is asked every turn in this genre, and until now the
// only answer was a count on the pile and a list of names in the sidebar that a fight hides.
//
// ⚠ THE DRAW PILE IS SHOWN IN NO ORDER. Its order is the one thing about it the player is not meant to know,
// so it is sorted by name — EXCEPT for what a disclosure (the Article of Full Disclosure and anything like
// it) lets the hero see: those top cards come first, in the order they will be drawn, under their own heading.
// The viewer never shows more than the table already tells.
//
// Like the map, it hangs off the SCREEN on a canvas layer of its own, so an enemy acting underneath redraws the
// fight and leaves the viewer standing.
public partial class SessionScreen
{
    private const string PileOverlayName = "PileOverlay";

    internal enum Pile { Draw, Discard, Exhaust, Deck }

    private bool AnyMenuOpen =>
        GetNodeOrNull("SettingsOverlay") is not null
        || GetNodeOrNull(BugReportPanel.OverlayName) is not null
        || GetNodeOrNull(ArchivePanel.OverlayName) is not null
        || GetNodeOrNull(CompendiumPanel.OverlayName) is not null;

    // Opening the pile that is already open closes it, so the same key or click is on and off.
    private void TogglePile(Pile pile)
    {
        if (GetNodeOrNull(PileOverlayName) is { } open)
        {
            var same = open.HasMeta("pile") && open.GetMeta("pile").AsInt32() == (int)pile;
            open.QueueFree();
            RemoveChild(open);
            if (same)
                return;
        }
        if (AnyMenuOpen || Session is null)
            return;
        var combat = Play?.Seat?.Current;
        if (pile != Pile.Deck && combat is null)
            return;

        var (title, sections) = pile switch
        {
            Pile.Deck => ("Your deck", DeckSections()),
            Pile.Draw => ("Draw pile", DrawSections(combat!)),
            Pile.Discard => ("Discard pile", ZoneSections(combat!, CardZone.DiscardPile, "most recent last")),
            // The exhausted cards have their own stack beside the discard pile again (user, 2026-11-20).
            _ => ("Exhausted", [
                .. ZoneSections(combat!, CardZone.ExhaustPile, "gone for the rest of this fight"),
                // Cards an enemy holds (the Warden of Sealed Volumes), shown only while it holds any.
                .. (combat!.State.GetCardZones(combat.HeroId).GetCardsInZone(CardZone.BanishedPile).Count > 0
                    ? ZoneSections(combat, CardZone.BanishedPile, "Sealed — held by an enemy until you free them")
                    : [])]),
        };
        var layer = CardOverlay(title, sections);
        layer.SetMeta("pile", (int)pile);
        AddChild(layer);
    }

    private void ClosePile() => GetNodeOrNull(PileOverlayName)?.QueueFree();

    private sealed record PileSection(string? Heading, IReadOnlyList<(string Definition, int Upgrade, string? Caption)> Cards);

    // The deck between rooms is the RUN's deck; in a fight it is still the run's deck (what you own), not the
    // fight's cards — a card created for this fight alone is in a combat pile, and it is there it is shown.
    // ONE FACE PER KIND OF CARD, with how many copies and — the Nutzungsnachweis — how often that card has been
    // played this run (RunTally.Plays, by base card: an improved copy's plays are the card's plays).
    private List<PileSection> DeckSections()
    {
        var plays = RunTally.Plays;
        var deck = Session!.Run.Deck
            .GroupBy(card => (Definition: card.DefinitionId.value, card.UpgradeLevel))
            .OrderBy(group => CardName(group.Key.Definition), StringComparer.Ordinal)
            .ThenBy(group => group.Key.UpgradeLevel)
            .Select(group =>
            {
                var copies = group.Count() > 1 ? $"×{group.Count()} · " : "";
                var played = plays.GetValueOrDefault(group.Key.Definition);
                return (group.Key.Definition, group.Key.UpgradeLevel,
                    (string?)$"{copies}{(played == 0 ? "not played yet" : $"played {played}×")}");
            })
            .ToList();
        return [new PileSection("How often each card was played in this run's finished fights (all copies together)", deck)];
    }

    private List<PileSection> DrawSections(ICombatView combat)
    {
        var pile = combat.State.GetCardZones(combat.HeroId).GetCardsInZone(CardZone.DrawPile);
        var revealed = combat.RevealedDrawPile;
        var seen = revealed.Select(card => card.Id.value).ToHashSet();
        var sections = new List<PileSection>();
        if (revealed.Count > 0)
            sections.Add(new PileSection("On top, in the order you will draw them",
                revealed.Select(card => (card.DefinitionId.value, 0, MarkCaption(card))).ToList()));
        var rest = pile.Where(card => !seen.Contains(card.Id.value))
            .Select(card => (card.DefinitionId.value, 0, MarkCaption(card)))
            .OrderBy(card => CardName(card.value), StringComparer.Ordinal)
            .ToList();
        sections.Add(new PileSection(
            revealed.Count > 0 ? "The rest, in no particular order" : "In no particular order", rest));
        return sections;
    }

    private static List<PileSection> ZoneSections(ICombatView combat, CardZone zone, string note) =>
        [new PileSection(note, combat.State.GetCardZones(combat.HeroId).GetCardsInZone(zone)
            .Select(card => (card.DefinitionId.value, 0, MarkCaption(card))).ToList())];

    private CanvasLayer CardOverlay(string title, IReadOnlyList<PileSection> sections)
    {
        var layer = new CanvasLayer { Name = PileOverlayName, Layer = 50 };
        var veil = new Control { MouseFilter = MouseFilterEnum.Stop, Theme = Theme };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        layer.AddChild(veil);
        var dim = new ColorRect { Color = new Color(MoonvineTheme.Bg, 0.94f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        // A click on the empty dark closes it, the way a click beside a hand of cards puts them down.
        dim.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                ClosePile();
        };
        veil.AddChild(dim);

        var column = new VBoxContainer();
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        column.OffsetLeft = 24;
        column.OffsetRight = -24;
        column.OffsetTop = 16;
        column.OffsetBottom = -16;
        column.AddThemeConstantOverride("separation", 10);
        column.MouseFilter = MouseFilterEnum.Ignore;
        veil.AddChild(column);

        var count = title == "Your deck" ? Session?.Run.Deck.Count ?? 0 : sections.Sum(section => section.Cards.Count);
        var head = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        head.AddThemeConstantOverride("separation", 16);
        var heading = new Label { Text = $"{title} ({count})" };
        heading.AddThemeFontSizeOverride("font_size", 20);
        head.AddChild(heading);
        var close = new Button { Text = "Close (Esc)" };
        close.Pressed += ClosePile;
        head.AddChild(close);
        column.AddChild(head);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(body);
        column.AddChild(scroll);

        if (count == 0)
        {
            var empty = MutedLabel("Empty.");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            body.AddChild(empty);
        }
        foreach (var section in sections.Where(section => section.Cards.Count > 0))
        {
            if (section.Heading is { } words)
            {
                var label = MutedLabel(words);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                body.AddChild(label);
            }
            var gallery = Gallery();
            foreach (var (definition, upgrade, caption) in section.Cards)
                gallery.AddChild(CardPick(definition, upgrade, selected: false, caption: caption, onClick: null));
            body.AddChild(gallery);
        }
        return layer;
    }

    // ── THE DISCARD AND EXHAUST PILES, IN THE RIGHT-HAND CORNER (user, 2026-09-28; split 2026-11-20) ────────
    // The mirror of the draw pile: small stacks of backs with their counts under them, and each picture is the
    // button that opens it. ⚠ TWO STACKS, NOT ONE CAPTION (user, 2026-11-20): "Discard 7 · Exhausted 3" under a
    // single pile ran past both of its edges and could not be read. The discard pile keeps the corner; the
    // exhausted cards stand beside it, dimmed, with the cards an enemy has sealed away counted over them.
    private const string DiscardPileName = "DiscardPile";
    private const string ExhaustPileName = "ExhaustPile";
    private const int PileGap = 10;   // the air between the two stacks
    // What the hand keeps clear on the right: both stacks, the air between them and the corner's own inset.
    private static int RightPileBand => (int)(PileCorner + 2 * (PileW + 12) + PileGap + 12);

    private sealed class PileStack
    {
        public Control? Holder;
        public readonly List<Control> Stills = [];
        public Control? Top;
        public Label? Count;
        public Label? Over;
    }

    private readonly PileStack _discardStack = new();
    private readonly PileStack _exhaustStack = new();
    private Control? _discardHolder => _discardStack.Holder;
    private Control? _exhaustHolder => _exhaustStack.Holder;

    private void ForgetRightPiles()
    {
        foreach (var stack in new[] { _discardStack, _exhaustStack })
        {
            if (stack.Holder is { } holder && IsInstanceValid(holder))
                holder.QueueFree();
            stack.Holder = null;
            stack.Stills.Clear();
            stack.Top = null;
            stack.Count = null;
            stack.Over = null;
        }
    }

    private void BuildDiscardPile(ICombatView combat)
    {
        var zones = combat.State.GetCardZones(combat.HeroId);
        var discarded = zones.GetCardsInZone(CardZone.DiscardPile).Count;
        var exhausted = zones.GetCardsInZone(CardZone.ExhaustPile).Count;
        var sealedAway = zones.GetCardsInZone(CardZone.BanishedPile).Count;

        BuildRightPile(_discardStack, DiscardPileName, slot: 0, dimmed: false,
            "Discard pile — click to see the cards you have played and discarded.", Pile.Discard,
            discarded, $"Discard {discarded}", null);
        BuildRightPile(_exhaustStack, ExhaustPileName, slot: 1, dimmed: true,
            "Exhausted — click to see the cards gone for the rest of this fight.", Pile.Exhaust,
            exhausted, $"Exhausted {exhausted}", sealedAway > 0 ? $"Sealed {sealedAway}" : null);
    }

    // `slot` counts stacks from the corner leftward.
    private void BuildRightPile(PileStack stack, string name, int slot, bool dimmed, string tooltip, Pile opens,
        int count, string caption, string? over)
    {
        const int lift = 3;
        const int band = 22;
        const int lean = 4 * lift;
        var footprint = new Vector2(PileW + lean, PileH + lean + band);

        if (stack.Holder is null || !IsInstanceValid(stack.Holder))
        {
            var holder = new Control { Name = name, CustomMinimumSize = footprint, Size = footprint };
            holder.SetAnchorsPreset(LayoutPreset.BottomRight);
            holder.Position = new Vector2(-PileCorner - footprint.X - slot * (footprint.X + PileGap), -footprint.Y - PileCorner);
            _combatRoot.AddChild(holder);
            stack.Holder = holder;

            stack.Stills.Clear();
            for (var i = 0; i < 3; i++)
            {
                var still = CardVisuals.Back(PileScale);
                holder.AddChild(still);
                stack.Stills.Add(still);
            }
            stack.Top = CardVisuals.Back(PileScale);
            holder.AddChild(stack.Top);
            if (dimmed)
                foreach (var back in stack.Stills.Append(stack.Top))
                    back.Modulate = new Color(0.55f, 0.52f, 0.5f);

            // The caption may run a little past the stack's own width ("Exhausted 12"), never into its neighbour.
            stack.Count = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 1f, AnchorBottom = 1f,
                OffsetLeft = -PileGap / 2f, OffsetRight = PileGap / 2f, OffsetTop = -band, OffsetBottom = 0f,
            };
            stack.Count.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
            holder.AddChild(stack.Count);

            stack.Over = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 0f,
                OffsetLeft = -PileGap / 2f, OffsetRight = PileGap / 2f, OffsetTop = -band, OffsetBottom = 0f,
            };
            stack.Over.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
            holder.AddChild(stack.Over);

            var hit = new Button
            {
                Flat = true,
                TooltipText = tooltip,
                MouseDefaultCursorShape = CursorShape.PointingHand,
            };
            foreach (var style in new[] { "normal", "hover", "pressed", "focus" })
                hit.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
            hit.SetAnchorsPreset(LayoutPreset.FullRect);
            hit.Pressed += () => TogglePile(opens);
            holder.AddChild(hit);
        }

        // Leaning up and to the LEFT, the draw pile's lean turned round, so the two corners face each other. The
        // stack grows from the bottom as cards arrive.
        var backing = Math.Min(count > 0 ? count - 1 : 0, 3);
        for (var i = 0; i < stack.Stills.Count; i++)
        {
            stack.Stills[i].Visible = i < backing;
            stack.Stills[i].Position = new Vector2(lean - i * lift, lean - i * lift);
        }
        if (stack.Top is not null && IsInstanceValid(stack.Top))
        {
            stack.Top.Visible = count > 0;
            stack.Top.Position = new Vector2(lean - backing * lift, lean - backing * lift);
        }
        if (stack.Count is not null && IsInstanceValid(stack.Count))
            stack.Count.Text = caption;
        if (stack.Over is not null && IsInstanceValid(stack.Over))
        {
            stack.Over.Text = over ?? "";
            stack.Over.Visible = over is not null;
        }
    }

    // The draw pile's own picture is the button for it: a flat, see-through Button over the whole stack.
    private void MakeDeckPileClickable(Control holder)
    {
        var hit = new Button
        {
            Flat = true,
            TooltipText = "Draw pile — click to see what is left in it.",
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        hit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        hit.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        hit.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        hit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        hit.SetAnchorsPreset(LayoutPreset.FullRect);
        hit.Pressed += () => TogglePile(Pile.Draw);
        holder.AddChild(hit);
    }

    // `--smoke-piles`: reach the first fight, play what can be played, then open each pile the way a player
    // does — the draw pile by its picture's button, the others by theirs — and photograph each.
    private async System.Threading.Tasks.Task SmokePiles()
    {
        var combat = WalkToFirstFight();
        if (combat is null)
        {
            GD.Print("smoke-piles: no fight reached");
            GetTree().Quit(1);
            return;
        }
        for (var guard = 0; guard < 10 && combat.IsHeroTurn; guard++)
        {
            var hero = combat.State.GetCombatant(combat.HeroId);
            var card = combat.Hand.FirstOrDefault(c => CanAfford(c) && combat.CanPlay(c.Id));
            if (card is null) break;
            _armedCard = card.Id;
            PlayArmedCardAt(null);
        }
        var report = new List<string>();
        // The board itself first, so the corner's captions can be read with no overlay over them.
        if (!DisplayServer.GetName().Contains("headless"))
        {
            await ToSignal(GetTree().CreateTimer(0.8), SceneTreeTimer.SignalName.Timeout);
            GetViewport().GetTexture().GetImage().SavePng("user://smoke-pile-board.png");
        }
        foreach (var pile in new[] { Pile.Draw, Pile.Discard, Pile.Exhaust, Pile.Deck })
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            TogglePile(pile);
            var open = GetNodeOrNull(PileOverlayName);
            report.Add($"{pile}={(open is null ? "CLOSED" : "open")}");
            if (!DisplayServer.GetName().Contains("headless"))
            {
                await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
                GetViewport().GetTexture().GetImage().SavePng($"user://smoke-pile-{pile.ToString().ToLowerInvariant()}.png");
            }
            // Esc closes it, the way a player puts it down.
            _UnhandledInput(new InputEventAction { Action = "ui_cancel", Pressed = true });
            report.Add($"after-esc={(GetNodeOrNull(PileOverlayName) is { } left && !left.IsQueuedForDeletion() ? "STILL OPEN" : "closed")}");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // ⚠ AND BY THE MOUSE, through the viewport — the way a player opens them. Calling TogglePile says the
        // viewer works; only a real click says the picture is not covered by something else that eats it (the
        // draw pile was, 2026-09-28, while every check above passed).
        foreach (var (name, holder) in new[] { ("click-draw", _deckHolder), ("click-discard", _discardHolder), ("click-exhaust", _exhaustHolder) })
        {
            ClosePile();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (holder is null || !IsInstanceValid(holder))
            {
                report.Add($"{name}=NO-PILE CLOSED");
                continue;
            }
            var at = holder.GetGlobalRect().GetCenter();
            foreach (var pressed in new[] { true, false })
                GetViewport().PushInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at,
                });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            report.Add($"{name}={(GetNodeOrNull(PileOverlayName) is { } shown && !shown.IsQueuedForDeletion() ? "open" : "CLOSED")}");
        }
        ClosePile();

        var ok = report.All(r => !r.Contains("CLOSED") && !r.Contains("STILL"));
        GD.Print($"smoke-piles: {string.Join(" ", report)} {(ok ? "PASS" : "FAIL")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
