using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Reporting;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// ── WHAT JUST HAPPENED, SHOWN WHERE IT HAPPENED ──────────────────────────────────
// A fight is redrawn whole on every change, so a blow used to be a number on a bar that was simply different
// the next time the player looked. Now every step the fight took since the last drawing — a card, the end of
// the turn, each enemy's action — is read back from its trace and shown ON the body it touched: a number that
// rises off it (red for a wound, steel "🛡" for what block caught, green for a mend, "+N 🛡" for block gained,
// the status's colour for a buff or debuff put on it), a red flash and a shudder for a wound, a pulse of the
// bar for block.
//
// ⚠ THE STEPS ARE PLAYED ONE AFTER ANOTHER. An enemy turn resolves in a single call and is drawn once, with
// every bar already at its end; its steps are then shown a beat apart, so three enemies read as three blows
// and not one sum. The bars do not wait for them — what the numbers tell is how they got there.
//
// ⚠ ONLY WHAT WAS DONE TO A BODY IS SHOWN. Block falling away at the turn's start and a debuff ticking down
// are the fight's weather; a number for each would bury the blows the player needs to see. A neutral marker
// ("Struck Last Round") is bookkeeping: its chip flares when it changes, it gets no number of its own.
public partial class SessionScreen
{
    // Each body as drawn: its figure (flashed on a wound) and its health bar (pulsed on block).
    private readonly Dictionary<string, (Control Panel, Control Body, Control Bar)> _bodies = new(StringComparer.Ordinal);
    private string _stepsFight = "";
    private int _stepsSeen;
    private InteractiveRunSession? _watchedSession;
    private IReadOnlyList<ScenarioStepReport>? _handedOver;
    private Control? _effectLayer;

    private const double Beat = 0.45;      // between two steps (two enemies acting)
    private const double HitBeat = 0.16;   // between two hits on one body inside a step

    private void RegisterBody(CombatantState combatant, Control panel, Control body, Control bar) =>
        _bodies[combatant.Id.value] = (panel, body, bar);

    // ⚠⚠ THE FIGHT ON THE SCREEN IS A REPLAY. Every answer replays the fight from the last turn boundary, so the
    // combat is a new object on every drawing and its steps count from the start of the turn. Ending the turn
    // MOVES that boundary: the fight is captured and replayed once more from the new turn — and the enemies'
    // turn, which was in the replay before, is not in the one that gets drawn. The session asks the screen's
    // own playback for that capture (CaptureCombat), and at that moment the driver still holds the fight that
    // played the enemies out; so the question is wrapped, and the steps of the turn handed over are kept.
    private void WatchHandovers(InteractiveRunSession session)
    {
        if (ReferenceEquals(session, _watchedSession) || session.CaptureCombat is not { } capture)
            return;
        _watchedSession = session;
        session.CaptureCombat = () =>
        {
            var steps = Play?.CombatDriver?.Current?.Steps;
            var fight = capture();
            if (fight is not null && steps is not null)
                _handedOver = steps;
            return fight;
        };
    }

    // Called after a fight is drawn: shows every step the fight took since the last drawing of the SAME fight.
    private void ShowCombatChanges(InteractiveRunSession session, InteractiveCombat combat)
    {
        WatchHandovers(session);
        var key = session.Run.CurrentNodeId?.Value ?? "";
        var steps = combat.Steps;
        var handed = _handedOver;
        _handedOver = null;
        // A new fight — or the first drawing of one loaded from a save — has nothing to show yet.
        var fresh = key != _stepsFight;
        var shown = new List<ScenarioStepReport>();
        if (!fresh && handed is not null)
        {
            // The rest of the turn just handed over, then whatever the new turn has done since it began.
            shown.AddRange(handed.Skip(_stepsSeen));
            shown.AddRange(steps);
        }
        else if (!fresh && steps.Count >= _stepsSeen)
            shown.AddRange(steps.Skip(_stepsSeen));
        _stepsFight = key;
        _stepsSeen = steps.Count;
        if (_fastForward || shown.Count == 0)
            return;

        var worn = session.Run.Relics.Select(r => r.Id.Value).ToHashSet(StringComparer.Ordinal);
        var beat = 0;
        foreach (var step in shown)
        {
            // How many numbers each body has had in this step: the next one rises a little after and lower.
            var row = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var trace in step.Trace)
            {
                switch (trace)
                {
                    case DamageResolvedTraceEvent hit when hit.HealthLost > 0 || hit.BlockedAmount > 0:
                        // Pop staggers by the body's row itself; the flash keeps time with its number.
                        var who = hit.TargetCombatantId.value;
                        var at = beat * Beat + row.GetValueOrDefault(who) * HitBeat;
                        if (hit.HealthLost > 0)
                        {
                            Wound(who, at);
                            Pop(who, $"−{hit.HealthLost}", MoonvineTheme.Harm, 34, beat * Beat, row);
                        }
                        if (hit.BlockedAmount > 0)
                        {
                            Shield(who, at);
                            Pop(who, $"🛡 {hit.BlockedAmount}", MoonvineTheme.Steel, 24, beat * Beat, row);
                        }
                        break;
                    case HealResolvedTraceEvent mend when mend.HealedAmount > 0:
                        Pop(mend.TargetCombatantId.value, $"+{mend.HealedAmount}", Heal, 30, beat * Beat, row);
                        break;
                    case BlockGainResolvedTraceEvent guard when guard.BlockAfter > guard.BlockBefore:
                        Pop(guard.TargetCombatantId.value, $"+{guard.BlockAfter - guard.BlockBefore} 🛡",
                            MoonvineTheme.Steel, 26, beat * Beat, row);
                        Shield(guard.TargetCombatantId.value, beat * Beat);
                        break;
                    case StatusApplicationResolvedTraceEvent status
                        when StatusWord(combat, status, worn) is { } said:
                        Pop(status.TargetCombatantId.value, said.Text, said.Colour, 20, beat * Beat, row);
                        break;
                }
            }
            if (row.Count > 0)
                beat++;
        }
    }

    private static readonly Color Heal = new("6fbf73");

    // What a status put on a body says as it lands, or null when it says nothing: a status the chips do not
    // show, a neutral marker, a relic's own rule, a mechanic that has its plate in the middle of the room.
    private (string Text, Color Colour)? StatusWord(
        InteractiveCombat combat, StatusApplicationResolvedTraceEvent applied, HashSet<string> worn)
    {
        StatusDefinition? definition = null;
        if (combat.State.DefinitionRegistry?.TryGetStatus(applied.StatusDefinitionId, out definition) != true
            || definition is null
            || definition.DefaultVisibility != StatusVisibility.Visible
            || definition.Polarity is not (StatusPolarity.Buff or StatusPolarity.Debuff)
            || OfAWornRelic(applied.StatusDefinitionId.value, worn)
            || _ruled.Any(r => r.Owner.Id == applied.TargetCombatantId && r.Status.DefinitionId == applied.StatusDefinitionId))
            return null;
        var name = !string.IsNullOrWhiteSpace(definition.DisplayNameKey)
            ? definition.DisplayNameKey
            : Humanized(applied.StatusDefinitionId.value);
        var colour = definition.Polarity == StatusPolarity.Buff ? MoonvineTheme.Accent : MoonvineTheme.Harm;
        if (applied.Outcome == StatusApplicationOutcome.BlockedByInterceptor)
            return ($"✕ {name}", MoonvineTheme.TextMuted);
        if (applied.Outcome is not (StatusApplicationOutcome.Applied or StatusApplicationOutcome.Merged))
            return null;
        var amount = applied.RequestedStacks > 0 ? applied.RequestedStacks : applied.RequestedDurationTurns;
        return (amount > 1 ? $"{name} +{amount}" : name, colour);
    }

    // The layer the numbers rise on. It is the screen's own, not the fight's: a fight's nodes are freed on the
    // next redraw, which can come within the same second, and a number hung on them would vanish mid-rise.
    private Control EffectLayer()
    {
        if (_effectLayer is { } layer && IsInstanceValid(layer))
            return layer;
        _effectLayer = new Control { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 40, Name = "Effects" };
        _effectLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_effectLayer);
        return _effectLayer;
    }

    // ⚠ A body that was just drawn has not been laid out yet: its place is known one frame later. And by then
    // it may have been drawn AGAIN — so the body is looked up by id when the effect starts, not held on to.
    private async System.Threading.Tasks.Task<Control?> BodyAfter(string id, double delay, bool bar = false)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (delay > 0)
            await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
        if (!_bodies.TryGetValue(id, out var body))
            return null;
        var node = bar ? body.Bar : body.Body;
        return IsInstanceValid(node) ? node : null;
    }

    // One number rising off a body. `row` counts the numbers this body has had in the step; each one more
    // starts a line lower and a little to the side, so two in a row do not print over each other.
    private async void Pop(string id, string text, Color colour, int size, double delay, Dictionary<string, int> row)
    {
        var line = row.GetValueOrDefault(id);
        row[id] = line + 1;
        if (await BodyAfter(id, delay + line * HitBeat) is not { } figure)
            return;
        var rect = figure.GetGlobalRect();
        var layer = EffectLayer();
        var origin = layer.GetGlobalTransform().AffineInverse()
            * (rect.Position + new Vector2(rect.Size.X / 2, rect.Size.Y * 0.3f));
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0),
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(label);
        var width = label.GetMinimumSize().X;
        var start = origin + new Vector2(-width / 2 + (line % 2 == 0 ? -1 : 1) * 12 * Math.Min(line, 3), (line % 4) * 26);
        label.Position = start;
        var tween = label.CreateTween();
        tween.TweenProperty(label, "modulate:a", 1.0f, 0.1);
        tween.Parallel().TweenProperty(label, "position:y", start.Y - 70, 1.1)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(label, "modulate:a", 0.0f, 0.45);
        tween.TweenCallback(Callable.From(label.QueueFree));
    }

    // A wound: the body flashes red and shudders.
    private async void Wound(string id, double delay)
    {
        if (await BodyAfter(id, delay) is not { } figure)
            return;
        figure.Modulate = new Color(2.2f, 0.55f, 0.55f);
        var home = figure.Position;
        var tween = figure.CreateTween();
        tween.TweenProperty(figure, "modulate", Colors.White, 0.4).SetTrans(Tween.TransitionType.Sine);
        var shake = figure.CreateTween();
        foreach (var dx in new[] { -9f, 7f, -5f, 3f, 0f })
            shake.TweenProperty(figure, "position:x", home.X + dx, 0.045);
    }

    // Block gained or block that caught a blow: the health bar (which carries the block number) pulses steel.
    private async void Shield(string id, double delay)
    {
        if (await BodyAfter(id, delay, bar: true) is not { } bar)
            return;
        bar.PivotOffset = bar.Size / 2;
        bar.Modulate = new Color(0.8f, 1.3f, 2.0f);
        bar.Scale = new Vector2(1.08f, 1.25f);
        var tween = bar.CreateTween().SetParallel();
        tween.TweenProperty(bar, "modulate", Colors.White, 0.55).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(bar, "scale", Vector2.One, 0.4).SetTrans(Tween.TransitionType.Back);
    }

    // THE EFFECTS, PHOTOGRAPHED MID-RISE: the first fight, a card at an enemy, the next card, the enemies' turn —
    // a still a moment after each, while the numbers are in the air. Needs a window; headless it only walks.
    private async System.Threading.Tasks.Task SmokeEffects()
    {
        var session = Session;
        for (var i = 0; i < 8 && Play?.CombatDriver?.Current is null && session is not null; i++)
        {
            if (session.IsAwaitingNodeChoice) session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude) session.Continue();
            else break;
        }
        if (Play?.CombatDriver is not { Current: not null } driver)
        {
            GD.Print("smoke-effects: no fight reached");
            GetTree().Quit(1);
            return;
        }
        await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
        var shots = 0;
        for (var play = 0; play < 3; play++)
        {
            var combat = driver.Current!;
            var hero = combat.State.GetCombatant(combat.HeroId);
            // A card that needs no target first: in a starting hand that is the block.
            if (!combat.IsHeroTurn || combat.Hand
                    .Where(c => combat.CanPlay(c.Id) && CanPay(hero, c.DefinitionId.value))
                    .OrderBy(c => NeedsTarget(c.DefinitionId.value)).FirstOrDefault() is not { } card)
                break;
            var target = combat.State.Combatants
                .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)?.Id;
            GD.Print($"smoke-effects: play {card.DefinitionId.value}");
            driver.PlayCard(card.Id, target);
            await Snap(++shots);
        }
        if (driver.Current is { IsHeroTurn: true })
        {
            GD.Print("smoke-effects: end turn");
            EndTurnNow();
            // The enemies' turn is shown a beat per step: a still on each of the first three.
            for (var still = 0; still < 3; still++)
            {
                await ToSignal(GetTree().CreateTimer(still == 0 ? 0.3 : Beat), SceneTreeTimer.SignalName.Timeout);
                if (!DisplayServer.GetName().Contains("headless"))
                    GetViewport().GetTexture().GetImage().SavePng($"user://smoke-effects-{++shots}.png");
            }
        }
        GD.Print($"smoke-effects: {shots} shots");
        GetTree().Quit();

        async System.Threading.Tasks.Task Snap(int n)
        {
            await ToSignal(GetTree().CreateTimer(0.38), SceneTreeTimer.SignalName.Timeout);
            if (!DisplayServer.GetName().Contains("headless"))
                GetViewport().GetTexture().GetImage().SavePng($"user://smoke-effects-{n}.png");
            await ToSignal(GetTree().CreateTimer(1.4), SceneTreeTimer.SignalName.Timeout);
        }
    }
}
