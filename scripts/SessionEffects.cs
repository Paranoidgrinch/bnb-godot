using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// ── WHAT JUST HAPPENED, SHOWN WHERE IT HAPPENED ──────────────────────────────────
// A fight is redrawn whole on every change, so a blow used to be a number on a bar that was simply different
// the next time the player looked. Each drawing now remembers every body's health, block and statuses, and the
// next drawing shows the difference ON the body: a number that rises off it (red for a wound, green for a
// mend, steel for block, the status's own colour for a status put on it), a red flash and a shudder for a
// wound, a pulse of the bar for block.
//
// ⚠ IT IS A DIFFERENCE, NOT AN EVENT. The engine does not tell the screen "8 damage to the Clerk"; the screen
// sees that the Clerk had 30 and has 22. So a whole enemy turn, drawn once, shows as ONE number per body — the
// sum of it — and a status that comes and goes inside one action never shows at all. For "what did that just
// do to me", that is the right grain.
//
// ⚠ ONLY GAINS ARE SHOWN for block and statuses. Block falling away at the turn's start and a debuff ticking
// down every turn are the fight's weather; a number for each would bury the blows the player needs to see.
public partial class SessionScreen
{
    private readonly record struct Vitals(int Health, int Block, Dictionary<string, (string Name, int Amount, Color Colour)> Statuses);

    // Each body as drawn: its frame (flashed on a wound) and its health bar (pulsed on block).
    private readonly Dictionary<string, (Control Panel, Control Body, Control Bar)> _bodies = new(StringComparer.Ordinal);
    private Dictionary<string, Vitals> _vitalsSeen = new(StringComparer.Ordinal);
    private string _vitalsFight = "";
    private Control? _effectLayer;

    private void RegisterBody(CombatantState combatant, Control panel, Control body, Control bar) =>
        _bodies[combatant.Id.value] = (panel, body, bar);

    // Called after a fight is drawn: compares it with the last drawing of the SAME fight and shows the change.
    private void ShowCombatChanges(InteractiveRunSession session, InteractiveCombat combat)
    {
        var fight = session.Run.CurrentNodeId?.Value ?? "";
        var worn = session.Run.Relics.Select(r => r.Id.Value).ToHashSet(StringComparer.Ordinal);
        var now = new Dictionary<string, Vitals>(StringComparer.Ordinal);
        foreach (var combatant in combat.State.Combatants)
            now[combatant.Id.value] = VitalsOf(combat, combatant, worn);

        // A new fight (or the first drawing of a loaded one) has nothing to compare against.
        var before = fight == _vitalsFight ? _vitalsSeen : null;
        _vitalsSeen = now;
        _vitalsFight = fight;
        if (before is null || _fastForward)
            return;

        foreach (var (id, after) in now)
        {
            if (!before.TryGetValue(id, out var was))
                continue;
            var said = new List<(string Text, Color Colour, int Size)>();
            if (after.Health < was.Health)
                said.Add(($"−{was.Health - after.Health}", MoonvineTheme.Harm, 34));
            else if (after.Health > was.Health)
                said.Add(($"+{after.Health - was.Health}", Heal, 30));
            if (after.Block > was.Block)
                said.Add(($"+{after.Block - was.Block} 🛡", MoonvineTheme.Steel, 26));
            foreach (var (key, status) in after.Statuses)
            {
                var had = was.Statuses.TryGetValue(key, out var old) ? old.Amount : 0;
                if (!was.Statuses.ContainsKey(key))
                    said.Add((status.Amount > 1 ? $"{status.Name} {status.Amount}" : status.Name, status.Colour, 20));
                else if (status.Amount > had)
                    said.Add(($"{status.Name} +{status.Amount - had}", status.Colour, 20));
            }
            if (said.Count == 0)
                continue;

            if (after.Health < was.Health)
                Wound(id);
            if (after.Block > was.Block)
                Shield(id);
            Float(id, said);
        }
    }

    private static readonly Color Heal = new("6fbf73");

    private Vitals VitalsOf(InteractiveCombat combat, CombatantState combatant, HashSet<string> worn)
    {
        var registry = combat.State.DefinitionRegistry;
        var statuses = new Dictionary<string, (string, int, Color)>(StringComparer.Ordinal);
        foreach (var status in combatant.Statuses)
        {
            // The statuses the chips under the body show, and of those only the ones that are FOR or AGAINST
            // someone: a neutral marker ("Struck Last Round") comes back every round and is bookkeeping. Its
            // chip still flares when it changes; it does not get a number of its own.
            if (status.Visibility != StatusVisibility.Visible || IsPhase(status) || status.Polarity is not
                (StatusPolarity.Buff or StatusPolarity.Debuff) || OfAWornRelic(status.DefinitionId.value, worn)
                || OnAPlate(combatant, status))
                continue;
            StatusDefinition? definition = null;
            registry?.TryGetStatus(status.DefinitionId, out definition);
            var colour = status.Polarity == StatusPolarity.Buff ? MoonvineTheme.Accent : MoonvineTheme.Harm;
            var name = definition is not null && !string.IsNullOrWhiteSpace(definition.DisplayNameKey)
                ? definition.DisplayNameKey
                : Humanized(status.DefinitionId.value);
            var amount = status.Stacks > 0 ? status.Stacks : status.DurationTurns;
            // Two instances of one status are one fact to the player: their amounts add.
            var key = status.DefinitionId.value;
            statuses[key] = statuses.TryGetValue(key, out var held)
                ? (name, held.Item2 + amount, colour)
                : (name, amount, colour);
        }
        return new Vitals(combatant.Health.Current, Block(combatant), statuses);
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
    private async void Float(string id, List<(string Text, Color Colour, int Size)> said)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!_bodies.TryGetValue(id, out var body) || !IsInstanceValid(body.Body))
            return;
        var rect = body.Body.GetGlobalRect();
        var layer = EffectLayer();
        var origin = layer.GetGlobalTransform().AffineInverse() * (rect.Position + new Vector2(rect.Size.X / 2, rect.Size.Y * 0.35f));
        for (var i = 0; i < said.Count; i++)
        {
            var (text, colour, size) = said[i];
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
            // Centred on the body, each line a little lower than the one before, with a little sideways
            // scatter so two numbers in a row do not print over each other.
            var width = label.GetMinimumSize().X;
            var start = origin + new Vector2(-width / 2 + (i % 2 == 0 ? -1 : 1) * 10 * i, i * (size + 6));
            label.Position = start;
            var delay = i * 0.12;
            var tween = label.CreateTween();
            tween.TweenInterval(delay);
            tween.TweenProperty(label, "modulate:a", 1.0f, 0.12);
            tween.Parallel().TweenProperty(label, "position:y", start.Y - 70, 1.1)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(label, "modulate:a", 0.0f, 0.45);
            tween.TweenCallback(Callable.From(label.QueueFree));
        }
    }

    // A wound: the body flashes red and shudders.
    private async void Wound(string id)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!_bodies.TryGetValue(id, out var body) || !IsInstanceValid(body.Body))
            return;
        var figure = body.Body;
        figure.Modulate = new Color(2.2f, 0.55f, 0.55f);
        var home = figure.Position;
        var tween = figure.CreateTween();
        tween.TweenProperty(figure, "modulate", Colors.White, 0.4).SetTrans(Tween.TransitionType.Sine);
        var shake = figure.CreateTween();
        foreach (var dx in new[] { -9f, 7f, -5f, 3f, 0f })
            shake.TweenProperty(figure, "position:x", home.X + dx, 0.045);
    }

    // Block gained: the health bar (which carries the block number) pulses steel.
    private async void Shield(string id)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!_bodies.TryGetValue(id, out var body) || !IsInstanceValid(body.Bar))
            return;
        var bar = body.Bar;
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
            await Snap(++shots);
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
