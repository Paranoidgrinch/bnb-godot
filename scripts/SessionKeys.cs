using Godot;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// ── THE SHORTCUTS ────────────────────────────────────────────────────────────────
// Every key here is an action from Controls, so what the player rebinds is what this listens to. A key does
// exactly what the click it stands for does — it calls the same handler — so there is no second path through a
// fight that could drift from the first.
//
// ⚠ _Input, not _UnhandledInput. A button the player just clicked keeps the focus, and Godot's GUI hands Space
// to a focused button before anything unhandled sees it: Space after clicking "End turn" would have ended the
// NEXT turn too. So the shortcuts are read first — and, for the same reason, not at all while a text field has
// the focus or a menu (settings, report, archive) is open: a name being typed is not a string of commands.
public partial class SessionScreen
{
    // The enemy the keys are aiming at. Kept between cards, so a player hitting the same body card after card
    // does not re-aim every time; it falls back to the first living enemy when that body is gone.
    private CombatantId? _keyTarget;

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } && _armedCard is not null)
        {
            PutArmedCardBack();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is not InputEventKey { Pressed: true, Echo: false } || AnyMenuOpen
            || GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit)
            return;
        if (Shortcut(@event))
            GetViewport().SetInputAsHandled();
    }

    private bool Shortcut(InputEvent @event)
    {
        if (@event.IsActionPressed(Controls.Map))
        {
            ToggleMapOverlay();
            return true;
        }
        foreach (var (action, pile) in new[]
        {
            (Controls.ViewDeck, Pile.Deck), (Controls.ViewDraw, Pile.Draw),
            (Controls.ViewDiscard, Pile.Discard), (Controls.ViewExhaust, Pile.Exhaust),
        })
            if (@event.IsActionPressed(action))
            {
                GetNodeOrNull(MapOverlayName)?.QueueFree();
                TogglePile(pile);
                return true;
            }

        // Everything below plays the fight, and is only meaningful with nothing covering it.
        if (GetNodeOrNull(PileOverlayName) is not null || GetNodeOrNull(MapOverlayName) is not null
            || Play?.CombatDriver is not { } driver || driver.Current is not { IsHeroTurn: true } combat
            || driver.PendingCardChoice is not null || driver.PendingOptionChoice is not null)
            return false;

        if (@event.IsActionPressed(Controls.EndTurn))
        {
            EndTurnNow();
            return true;
        }
        for (var i = 0; i < Controls.CardKeys; i++)
            if (@event.IsActionPressed(Controls.Card(i)))
            {
                var hand = combat.Hand;
                if (i < hand.Count)
                    OnCardClicked(hand[i].Id);
                return true;
            }
        if (_armedCard is null)
            return false;
        if (@event.IsActionPressed(Controls.TargetNext) || @event.IsActionPressed(Controls.TargetPrevious))
        {
            var living = LivingEnemies(combat);
            if (living.Count > 0)
            {
                var at = living.FindIndex(id => id.value == AimedEnemy(combat)?.value);
                var step = @event.IsActionPressed(Controls.TargetNext) ? 1 : -1;
                _keyTarget = living[((at < 0 ? 0 : at) + step + living.Count) % living.Count];
                Rebuild();
            }
            return true;
        }
        if (@event.IsActionPressed(Controls.Confirm))
        {
            PlayArmedCardAt(AimedEnemy(combat));
            return true;
        }
        return false;
    }

    private static List<CombatantId> LivingEnemies(InteractiveCombat combat) =>
        combat.State.Combatants
            .Where(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
            .Select(c => c.Id)
            .ToList();

    private CombatantId? AimedEnemy(InteractiveCombat combat)
    {
        var living = LivingEnemies(combat);
        return _keyTarget is { } aimed && living.Any(id => id.value == aimed.value)
            ? aimed
            : living.Count > 0 ? living[0] : null;
    }

    private void PutArmedCardBack()
    {
        _armedCard = null;
        Rebuild();
    }

    // The small key name over each card in hand, so the shortcut is learnt by looking rather than by the menu.
    private static Control CardKeyCap(int index, Vector2 cardAt)
    {
        var cap = new Label
        {
            Text = Controls.KeyName(Controls.Card(index)),
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Position = cardAt + new Vector2(0, -17),
            Size = new Vector2(CardVisuals.CardW, 16),
        };
        cap.AddThemeFontSizeOverride("font_size", 11);
        cap.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        return cap;
    }

    // `--smoke-keys`: the shortcuts, pressed as keys through the same _Input a player's keyboard reaches.
    private async System.Threading.Tasks.Task SmokeKeys()
    {
        var combat = WalkToFirstFight();
        if (combat is null)
        {
            GD.Print("smoke-keys: no fight reached");
            GetTree().Quit(1);
            return;
        }
        void Press(string action) => _Input(new InputEventKey { Keycode = Controls.KeyOf(action), Pressed = true });
        var report = new List<string>();

        Press(Controls.ViewDeck);
        report.Add($"deck-open={GetNodeOrNull(PileOverlayName) is not null}");
        Press(Controls.ViewDeck);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        report.Add($"deck-closed={GetNodeOrNull(PileOverlayName) is null}");

        var fight = Play!.CombatDriver!.Current!;
        var hero = fight.State.GetCombatant(fight.HeroId);
        var index = fight.Hand.ToList().FindIndex(c => NeedsTarget(c.DefinitionId.value)
            && CanPay(hero, c.DefinitionId.value) && fight.CanPlay(c.Id));
        if (index is >= 0 and < Controls.CardKeys)
        {
            var before = fight.Hand.Count;
            Press(Controls.Card(index));
            report.Add($"armed={_armedCard is not null}");
            Press(Controls.TargetNext);
            Press(Controls.Confirm);
            report.Add($"played={Play.CombatDriver.Current!.Hand.Count < before}");
        }
        else
            report.Add("no-targeted-card");

        var round = Play.CombatDriver.Current!.Round;
        Press(Controls.EndTurn);
        report.Add($"turn-ended={Play.CombatDriver.Current is not { } now || now.Round > round}");

        // And the dialog the keys are chosen in, photographed from the Esc menu.
        _UnhandledInput(new InputEventAction { Action = "ui_cancel", Pressed = true });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var controls = FindButton(GetNodeOrNull("SettingsOverlay"), "🎮  Controls");
        controls?.EmitSignal(BaseButton.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        report.Add($"controls-menu={controls is not null}");

        var ok = report.All(r => !r.Contains("False"));
        GD.Print($"smoke-keys: {string.Join(" ", report)} {(ok ? "PASS" : "FAIL")}");
        await CaptureThenQuit("smoke-controls.png", ok ? 0 : 1);
    }
}
