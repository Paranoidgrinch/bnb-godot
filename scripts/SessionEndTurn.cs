using Godot;
using RogueDeck.Scenario.Scripting;

namespace BnbGodot;

// ── "END THE TURN WITH CARDS STILL PLAYABLE?" ────────────────────────────────────
// Asked only when it is worth asking: there is a card in hand the hero could play right now — affordable and
// allowed by every rule in force. A hand of nothing but unplayable cards ends the turn without a word.
// Pressing End turn again (the button or its key) answers yes, so a player who meant it loses one key press;
// Esc or "Keep playing" answers no. "Don't ask again" turns it off, and Settings turns it back on.
public partial class SessionScreen
{
    private const string EndTurnAskName = "EndTurnAsk";

    private void RequestEndTurn()
    {
        if (Play?.CombatDriver?.Current is not { IsHeroTurn: true } combat)
            return;
        if (GetNodeOrNull(EndTurnAskName) is not null)
        {
            ConfirmEndTurn();
            return;
        }
        var playable = PlayableCount(combat);
        if (!GameplaySettings.ConfirmEndTurn || playable == 0)
        {
            EndTurnNow();
            return;
        }
        AddChild(EndTurnAsk(combat, playable));
    }

    private void ConfirmEndTurn()
    {
        CloseEndTurnAsk();
        EndTurnNow();
    }

    private bool CloseEndTurnAsk()
    {
        if (GetNodeOrNull(EndTurnAskName) is not { } ask)
            return false;
        RemoveChild(ask);
        ask.QueueFree();
        return true;
    }

    private int PlayableCount(InteractiveCombat combat)
    {
        var hero = combat.State.GetCombatant(combat.HeroId);
        return combat.Hand.Count(card => combat.CanPlay(card.Id) && CanPay(hero, card.DefinitionId.value));
    }

    private CanvasLayer EndTurnAsk(InteractiveCombat combat, int playable)
    {
        var layer = new CanvasLayer { Name = EndTurnAskName, Layer = 55 };
        var veil = new Control { MouseFilter = MouseFilterEnum.Stop, Theme = Theme };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        layer.AddChild(veil);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.5f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        veil.AddChild(center);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));
        center.AddChild(panel);
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 20);
        panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        margin.AddChild(column);

        var question = new Label
        {
            Text = $"You still have ⚡{combat.HeroEnergy} and {playable} card{(playable == 1 ? "" : "s")} you could play.\n"
                + "End the turn anyway?",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        question.AddThemeFontSizeOverride("font_size", 18);
        column.AddChild(question);

        var never = new CheckBox { Text = "Don't ask again (Settings can turn it back on)" };
        never.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        never.Toggled += on => GameplaySettings.ConfirmEndTurn = !on;
        column.AddChild(never);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        var end = new Button
        {
            Text = $"End turn ({Controls.KeyName(Controls.EndTurn)})",
            CustomMinimumSize = new Vector2(160, 40),
        };
        end.Pressed += ConfirmEndTurn;
        buttons.AddChild(end);
        var keep = new Button { Text = "Keep playing (Esc)", CustomMinimumSize = new Vector2(160, 40) };
        keep.Pressed += () => CloseEndTurnAsk();
        buttons.AddChild(keep);
        column.AddChild(buttons);
        return layer;
    }
}
