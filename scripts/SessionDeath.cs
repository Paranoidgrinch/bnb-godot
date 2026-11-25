using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// ── A FALLEN ENEMY GETS ITS MOMENT (playtest, 2026-11-25) ─────────────────────────
// An enemy put down mid-fight was simply drawn dimmed on the next redraw, and the LAST one was never drawn
// dead at all: the blow that ended the fight ended the combat, the session moved on to the reward, and the
// reward screen replaced the fight in the same frame — no number, no empty bar, no fall, no sound.
//
// Now a body that dies groans, sinks and fades where it stood, and its fall is heard. For the last one the
// fight's screen is HELD for a beat: the blow lands on it (the hit, the number, the bar draining to 0), the
// body falls, and only then is the reward drawn, with the victory sting.
//
// ⚠ THE HELD SCREEN IS THE OLD FIGHT. The run has already moved on underneath it — every redraw asked for in
// the meantime is simply postponed to the end of the beat, which draws whatever the run is at by then. Nothing
// on it may be played: a click or a key only ends the beat early (SessionKeys._Input), and the beat ends by
// itself after BeatLength whatever happens. A probe, the simulator and a headless run never hold at all.
public partial class SessionScreen
{
    private const double FallAfter = 0.3;    // from the killing blow to the body giving way
    private const double ThudAfter = 0.42;   // from giving way to hitting the floor
    private const double BeatLength = 1.25;  // the whole held moment, from the blow to the reward

    // The enemies standing at the last drawing of a fight, and what health each had: the ones the last blow
    // of the fight is about to fell, when the fight ends without being drawn again.
    private readonly List<(string Id, int Health)> _living = [];
    private bool _deathBeat;
    private int _beatToken;

    private void NoteTheLiving(ICombatView combat)
    {
        _living.Clear();
        foreach (var c in combat.State.Combatants)
            if (c.IsAlive && c.Id != combat.HeroId && c.TeamId == StandardCombatIds.EnemyTeam)
                _living.Add((c.Id.value, c.Health.Current));
    }

    // Asked by Rebuild before it draws: true while the beat holds the screen (the redraw waits for its end).
    private bool HoldForDeathBeat()
    {
        if (_deathBeat)
            return true;
        if (_living.Count == 0 || Play?.Seat?.Current is not null)
            return false;
        // The fight that was drawn last is over.
        var dying = _living.ToList();
        _living.Clear();
        var session = Session;
        if (_fastForward || IsSimulating || !IsInsideTree() || DisplayServer.GetName().Contains("headless")
            || session is null || session.Error is not null || Play?.Error is not null
            || (session.IsComplete && session.Run.Result != RunResult.Victory))
            return false;
        dying = dying.Where(d => _bodies.TryGetValue(d.Id, out var body) && IsInstanceValid(body.Body)).ToList();
        if (dying.Count == 0)
            return false;

        _deathBeat = true;
        var line = 0;
        foreach (var (id, health) in dying)
        {
            Wound(id, 0);
            if (health > 0)
                DamageTally(id, [new Blow(0, health, true)], line++);
            Drain(id);
            Fall(id, FallAfter, last: true);
        }
        Sound(Sfx.Kind.Hit, 0);
        var token = ++_beatToken;
        GetTree().CreateTimer(BeatLength).Timeout += () =>
        {
            if (token == _beatToken && IsInstanceValid(this))
                EndDeathBeat();
        };
        return true;
    }

    private void EndDeathBeat()
    {
        if (!_deathBeat)
            return;
        _deathBeat = false;
        _beatToken++;
        if (!IsInsideTree())
            return;
        // The fight's numbers still in the air go with it, not over the reward.
        if (_effectLayer is { } layer && IsInstanceValid(layer))
            foreach (var number in layer.GetChildren().OfType<CanvasItem>())
            {
                var fade = number.CreateTween();
                fade.TweenProperty(number, "modulate:a", 0.0f, 0.15);
                fade.TweenCallback(Callable.From(number.QueueFree));
            }
        Sfx.Play(Sfx.Kind.Victory);
        Rebuild();
    }

    // While the beat holds: a press skips it, and no input reaches the old fight underneath.
    private bool DeathBeatInput(InputEvent @event)
    {
        if (!_deathBeat || @event is InputEventMouseMotion)
            return false;
        GetViewport().SetInputAsHandled();
        if (@event is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true })
            EndDeathBeat();
        return true;
    }

    // The bar of a body the last blow felled runs out to nothing.
    private void Drain(string id)
    {
        if (!_bodies.TryGetValue(id, out var drawn) || !IsInstanceValid(drawn.Bar))
            return;
        var bar = drawn.Bar;
        foreach (var plate in bar.GetChildren().OfType<ColorRect>().Where(c => c.Name == "Plate"))
            plate.QueueFree();
        if (bar.GetChildren().OfType<Label>().FirstOrDefault() is { } label)
            label.Text = $"0/{MaxHealthOf(id)}";
        if (bar.GetChildren().OfType<ColorRect>().FirstOrDefault(c => c.Name == "Fill") is { } fill)
            fill.CreateTween().TweenProperty(fill, "anchor_right", 0.0f, 0.35)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
    }

    // A body giving way: a groan, a last lift, then it sinks, tips and fades — to the dimmed body a redraw
    // draws for the dead, or, for the last one of a fight, almost to nothing — and the floor takes it.
    private async void Fall(string id, double delay, bool last)
    {
        if (!_bodies.TryGetValue(id, out var drawn) || !IsInstanceValid(drawn.Body))
            return;
        var figure = drawn.Body;
        // Already drawn as the dead are drawn; it stands until the blow has landed.
        figure.Modulate = Colors.White;
        var body = await BodyAfter(id, delay);
        // Heard even when a redraw has already replaced the body with its dimmed dead self.
        Sfx.Play(Sfx.Kind.Death);
        Sound(Sfx.Kind.Fall, ThudAfter);
        if (body is null || body != figure)
            return;
        figure.PivotOffset = new Vector2(figure.Size.X / 2, figure.Size.Y);
        var home = figure.Position;
        var tween = figure.CreateTween();
        tween.TweenProperty(figure, "scale", new Vector2(0.96f, 1.05f), 0.08).SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(figure, "scale", new Vector2(1.06f, 0.82f), 0.5)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        tween.Parallel().TweenProperty(figure, "rotation_degrees", 7f, 0.5)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        tween.Parallel().TweenProperty(figure, "position:y", home.Y + 22, 0.5)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        tween.Parallel().TweenProperty(figure, "self_modulate", new Color(0.55f, 0.5f, 0.5f, last ? 0.08f : 0.35f), 0.55);
    }

    // THE LAST BLOW, PHOTOGRAPHED (`--smoke-effects --to-the-death`, needs a window): after the effects probe, the
    // first fight is played out card by card; the moment it ends, stills of the held beat and one of what follows.
    private async System.Threading.Tasks.Task SmokeDeath()
    {
        for (var act = 0; act < 300 && Play?.Seat is { Current: { } combat } seat; act++)
        {
            if (!combat.IsHeroTurn)
            {
                await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
                continue;
            }
            var card = combat.Hand.Where(c => combat.CanPlay(c.Id) && CanAfford(c))
                .OrderByDescending(c => NeedsTarget(c.DefinitionId.value)).FirstOrDefault();
            if (card is null)
                EndTurnNow();
            else
                seat.PlayCard(card.Id, combat.State.Combatants
                    .FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)?.Id);
            if (Play?.Seat?.Current is null)
                break;
            await ToSignal(GetTree().CreateTimer(card is null ? 1.0 : 0.3), SceneTreeTimer.SignalName.Timeout);
        }
        var won = Session is { } session && (!session.IsComplete || session.Run.Result == RunResult.Victory);
        GD.Print($"smoke-death: fight over, {(won ? "won" : "lost")}");
        var shot = 0;
        var at = 0.0;
        foreach (var still in new[] { 0.1, 0.35, 0.7, 1.05, 1.8 })
        {
            await ToSignal(GetTree().CreateTimer(still - at), SceneTreeTimer.SignalName.Timeout);
            at = still;
            GD.Print($"smoke-death: +{still:0.00}s beat holding={_deathBeat}");
            if (!DisplayServer.GetName().Contains("headless"))
                GetViewport().GetTexture().GetImage().SavePng($"user://smoke-death-{++shot}.png");
        }
        GD.Print("smoke-death: sounds "
            + string.Join(", ", Enum.GetValues<Sfx.Kind>().Select(k => $"{k}={Sfx.Played[(int)k]}")));
    }
}
