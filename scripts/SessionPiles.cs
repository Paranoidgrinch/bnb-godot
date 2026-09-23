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
        || GetNodeOrNull(ArchivePanel.OverlayName) is not null;

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
        var combat = Play?.CombatDriver?.Current;
        if (pile != Pile.Deck && combat is null)
            return;

        var (title, sections) = pile switch
        {
            Pile.Deck => ("Your deck", DeckSections()),
            Pile.Draw => ("Draw pile", DrawSections(combat!)),
            Pile.Discard => ("Discard pile", ZoneSections(combat!, CardZone.DiscardPile, "most recent last")),
            _ => ("Exhausted", ZoneSections(combat!, CardZone.ExhaustPile, "gone for the rest of this fight")),
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

    private List<PileSection> DrawSections(InteractiveCombat combat)
    {
        var pile = combat.State.GetCardZones(combat.HeroId).GetCardsInZone(CardZone.DrawPile);
        var revealed = combat.RevealedDrawPile;
        var seen = revealed.Select(card => card.Id.value).ToHashSet();
        var sections = new List<PileSection>();
        if (revealed.Count > 0)
            sections.Add(new PileSection("On top, in the order you will draw them",
                revealed.Select(card => (card.DefinitionId.value, 0, (string?)null)).ToList()));
        var rest = pile.Where(card => !seen.Contains(card.Id.value))
            .Select(card => (card.DefinitionId.value, 0, (string?)null))
            .OrderBy(card => CardName(card.value), StringComparer.Ordinal)
            .ToList();
        sections.Add(new PileSection(
            revealed.Count > 0 ? "The rest, in no particular order" : "In no particular order", rest));
        return sections;
    }

    private static List<PileSection> ZoneSections(InteractiveCombat combat, CardZone zone, string note) =>
        [new PileSection(note, combat.State.GetCardZones(combat.HeroId).GetCardsInZone(zone)
            .Select(card => (card.DefinitionId.value, 0, (string?)null)).ToList())];

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

    // The small buttons beside End turn: the two piles a fight has no picture for, and the whole deck.
    private void AddPileButtons(HBoxContainer controls, InteractiveCombat combat)
    {
        var zones = combat.State.GetCardZones(combat.HeroId);
        controls.AddChild(PileButton($"Discard {zones.GetCardsInZone(CardZone.DiscardPile).Count}",
            "What you have played or discarded this shuffle.", Pile.Discard));
        var exhausted = zones.GetCardsInZone(CardZone.ExhaustPile).Count;
        if (exhausted > 0)
            controls.AddChild(PileButton($"Exhausted {exhausted}",
                "Cards gone for the rest of this fight.", Pile.Exhaust));
        controls.AddChild(PileButton("Deck", "Every card you own.", Pile.Deck));
    }

    private Button PileButton(string text, string tooltip, Pile pile)
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += () => TogglePile(pile);
        return button;
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
            var card = combat.Hand.FirstOrDefault(c => CanPay(hero, c.DefinitionId.value) && combat.CanPlay(c.Id));
            if (card is null) break;
            _armedCard = card.Id;
            PlayArmedCardAt(null);
        }
        var report = new List<string>();
        foreach (var pile in new[] { Pile.Draw, Pile.Discard, Pile.Deck })
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
        var ok = report.All(r => !r.Contains("CLOSED") && !r.Contains("STILL"));
        GD.Print($"smoke-piles: {string.Join(" ", report)} {(ok ? "PASS" : "FAIL")}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
