using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// ── WHAT A CARD WILL DO, BEFORE IT IS PLAYED ─────────────────────────────────────
// Point at a card and every health bar it would change shows the change: the part of the bar it would take
// lit up with the number beside it, block it would eat or give, a body it would finish marked as such. A card
// that is picked and waiting for a target shows it on the enemy it is aimed at, and on whichever enemy the
// pointer is over.
//
// ⚠⚠ THE NUMBER IS NOT CALCULATED, IT IS PLAYED OUT — the same rule the enemy telegraph follows
// (InteractiveCombat.Foresee). The fight is forked and the card is played on the copy; what comes back already
// has every strength, vulnerability, relic and decree in it, because it is the code that will run. A preview
// that did the arithmetic itself would part company with the fight on the first modifier nobody told it about.
//
// ⚠ A fork has nobody sitting at it: a card that asks the player something is answered on the copy by the
// headless default. Its preview shows that answer, which is the one case where it can differ from the play.
public partial class SessionScreen
{
    private const string PreviewName = "Preview";

    // Each combatant's health bar as last drawn, so a hover can decorate it without a redraw.
    private readonly Dictionary<string, (Control Bar, int Max)> _healthBars = new(StringComparer.Ordinal);

    private Control RegisterHealthBar(CombatantState combatant, Control bar)
    {
        _healthBars[combatant.Id.value] = (bar, combatant.Health.Max);
        return bar;
    }

    // What the screen should preview when nothing is being pointed at: the picked card, if there is one.
    private void RestorePreview()
    {
        if (Play?.CombatDriver?.Current is { } combat && _armedCard is { } armed)
            ShowPreview(combat, armed, AimedEnemy(combat));
        else
            ClearPreview();
    }

    private void PreviewHandCard(CardInstanceId card)
    {
        if (Play?.CombatDriver?.Current is not { } combat)
            return;
        var definition = combat.Hand.FirstOrDefault(c => c.Id.value == card.value)?.DefinitionId.value;
        // A card that is aimed is previewed at the enemy the keys are aiming at — the one it would hit.
        ShowPreview(combat, card, definition is not null && NeedsTarget(definition) ? AimedEnemy(combat) : null);
    }

    private void ShowPreview(InteractiveCombat combat, CardInstanceId card, CombatantId? target)
    {
        ClearPreview();
        if (!GameplaySettings.DamageCalculator || !combat.IsHeroTurn || !combat.CanPlay(card)
            || combat.Hand.FirstOrDefault(c => c.Id.value == card.value) is not { } inHand
            || !CanPay(combat.State.GetCombatant(combat.HeroId), inHand.DefinitionId.value))
            return;
        // The same default target PlayArmedCardAt falls back to, so the preview is of the play that would happen.
        target ??= LivingEnemies(combat).FirstOrDefault();

        var before = Standing(combat);
        Dictionary<string, Stand> after;
        try
        {
            var fork = combat.Fork();
            fork.PlayCard(card, target);
            after = Standing(fork);
        }
        catch (Exception ex)
        {
            // A preview is a courtesy; a card whose play cannot be foreseen is still a card that can be played.
            GD.PushWarning($"preview of {inHand.DefinitionId.value}: {ex.Message}");
            return;
        }

        foreach (var (id, then) in before)
            if (after.TryGetValue(id, out var now) && _healthBars.TryGetValue(id, out var bar)
                && IsInstanceValid(bar.Bar))
                Decorate(bar.Bar, bar.Max, then, now);
    }

    private void ClearPreview()
    {
        foreach (var (bar, _) in _healthBars.Values)
            if (IsInstanceValid(bar) && bar.GetNodeOrNull(PreviewName) is { } old)
            {
                bar.RemoveChild(old);
                old.QueueFree();
            }
    }

    // What a body is standing with: its health, its block, whether it is up, and what it is carrying — the
    // visible statuses by name and size, so a card that only applies Paperwork still has something to show.
    private sealed record Stand(int Health, int Block, bool Alive, IReadOnlyDictionary<string, int> Statuses);

    private static Dictionary<string, Stand> Standing(InteractiveCombat combat) =>
        combat.State.Combatants.ToDictionary(c => c.Id.value, c => new Stand(c.Health.Current, Block(c), c.IsAlive,
            c.Statuses
                .Where(status => status.Visibility == StatusVisibility.Visible && !IsPhase(status))
                .GroupBy(status => StatusName(combat, status))
                .ToDictionary(group => group.Key,
                    group => group.Sum(status => status.Stacks > 0 ? status.Stacks
                        : status.DurationTurns > 0 ? status.DurationTurns : Math.Max(status.Charges, 1)),
                    StringComparer.Ordinal)),
            StringComparer.Ordinal);

    private static string StatusName(InteractiveCombat combat, StatusInstance status)
    {
        StatusDefinition? definition = null;
        combat.State.DefinitionRegistry?.TryGetStatus(status.DefinitionId, out definition);
        return definition is not null && !string.IsNullOrWhiteSpace(definition.DisplayNameKey)
            ? definition.DisplayNameKey
            : Humanized(status.DefinitionId.value);
    }

    private static IEnumerable<string> StatusChanges(Stand then, Stand now)
    {
        foreach (var (name, size) in now.Statuses)
        {
            var was = then.Statuses.GetValueOrDefault(name);
            if (size != was)
                yield return $"{name} {(size > was ? "+" : "−")}{Math.Abs(size - was)}";
        }
        foreach (var (name, was) in then.Statuses)
            if (!now.Statuses.ContainsKey(name))
                yield return $"{name} −{was}";
    }

    private static void Decorate(Control bar, int max,
        Stand then, Stand now)
    {
        var lost = then.Health - Math.Max(now.Health, 0);
        var healed = now.Health - then.Health;
        var block = now.Block - then.Block;
        var dies = then.Alive && (!now.Alive || now.Health <= 0);
        var statuses = StatusChanges(then, now).ToList();
        if (lost <= 0 && healed <= 0 && block == 0 && !dies && statuses.Count == 0)
            return;

        var layer = new Control { Name = PreviewName, MouseFilter = MouseFilterEnum.Ignore };
        layer.SetAnchorsPreset(LayoutPreset.FullRect);
        if (lost > 0 && max > 0)
        {
            // The stretch of the bar the card would take, lit, from where the health would end to where it is.
            var cut = new ColorRect { Color = new Color(1f, 0.93f, 0.75f, 0.75f), MouseFilter = MouseFilterEnum.Ignore };
            cut.AnchorTop = 0;
            cut.AnchorBottom = 1;
            cut.AnchorLeft = Mathf.Clamp((float)Math.Max(now.Health, 0) / max, 0, 1);
            cut.AnchorRight = Mathf.Clamp((float)then.Health / max, 0, 1);
            layer.AddChild(cut);
        }

        var parts = new List<string>();
        if (dies)
            parts.Add("☠");
        if (lost > 0)
            parts.Add($"−{lost}");
        if (healed > 0)
            parts.Add($"+{healed}");
        if (block != 0)
            parts.Add($"🛡{(block > 0 ? "+" : "−")}{Math.Abs(block)}");
        parts.AddRange(statuses);
        var label = new Label
        {
            Text = string.Join("  ", parts),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0, AnchorBottom = 0,
            OffsetTop = -24, OffsetBottom = -2,
            GrowVertical = Control.GrowDirection.Begin,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        label.AddThemeFontSizeOverride("font_size", 17);
        label.AddThemeColorOverride("font_color", lost > 0 || dies ? MoonvineTheme.Signal : MoonvineTheme.AccentLight);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 6);
        layer.AddChild(label);
        bar.AddChild(layer);
    }

    // The hover that previews a hand card: the same Button the card's lift listens on.
    private void PreviewOnHover(Control face, CardInstanceId card)
    {
        var listener = face.GetChildren().OfType<Button>().LastOrDefault() as Control ?? face;
        listener.MouseEntered += () => PreviewHandCard(card);
        listener.MouseExited += RestorePreview;
    }

    // `--smoke-preview`: pick an aimed card, and the aimed enemy's bar must show what it would lose — while the
    // real fight stays exactly as it was (a preview that leaked into the fight would be a card played twice).
    private async System.Threading.Tasks.Task SmokePreview()
    {
        var combat = WalkToFirstFight();
        if (combat is null)
        {
            GD.Print("smoke-preview: no fight reached");
            GetTree().Quit(1);
            return;
        }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var hero = combat.State.GetCombatant(combat.HeroId);
        // The first aimed card whose words promise damage, so the probe measures a NUMBER.
        var card = combat.Hand.FirstOrDefault(c => NeedsTarget(c.DefinitionId.value)
            && CanPay(hero, c.DefinitionId.value) && combat.CanPlay(c.Id)
            && GameHost.Instance.Blueprint.Presentation.Cards.GetValueOrDefault(c.DefinitionId.value)?.FlavorText
                is { } text && text.Contains("damage", StringComparison.OrdinalIgnoreCase));
        if (card is null)
        {
            GD.Print("smoke-preview: no aimed card in the opening hand");
            GetTree().Quit(1);
            return;
        }
        var before = Standing(combat);
        OnCardClicked(card.Id); // arms it, and the redraw previews it at the aimed enemy
        var aimed = AimedEnemy(combat)!.Value.value;
        var shown = _healthBars.TryGetValue(aimed, out var bar) && bar.Bar.GetNodeOrNull(PreviewName) is { } layer
            ? layer.GetChildren().OfType<Label>().FirstOrDefault()?.Text ?? ""
            : "";
        var untouched = Standing(Play!.CombatDriver!.Current!).All(pair =>
            before[pair.Key] is var was && was.Health == pair.Value.Health && was.Block == pair.Value.Block
            && was.Alive == pair.Value.Alive && !StatusChanges(was, pair.Value).Any());
        var ok = shown.Contains('−') && untouched;
        GD.Print($"smoke-preview: card={card.DefinitionId.value} aimed={aimed} shows=\"{shown}\" "
            + $"fight-untouched={untouched} {(ok ? "PASS" : "FAIL")}");
        await CaptureThenQuit("smoke-preview.png", ok ? 0 : 1);
    }
}
