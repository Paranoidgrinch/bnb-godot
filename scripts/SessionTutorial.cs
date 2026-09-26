using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;

namespace BnbGodot;

// THE COACH (TUTORIAL_PLAN.md). While a tutorial is being played, every screen is looked at after it is drawn and
// the first MOMENT it offers that the player has not been told about yet is explained: a panel with a title, a few
// sentences and "Next", and a gold frame around the thing it is talking about. The frontend decides WHEN a moment
// has come — it is the one that knows what is on screen; the words are the game's (Presentation.Game.Extra,
// "tutorial:<moment>" and "tutorial.text:<moment>", bnb-content's Tutorial.cs).
//
// The coach never blocks the game: the frame ignores the mouse and the panel only covers itself, so a player who
// would rather play than read simply plays. "Skip tutorial" silences it for the rest of the run.
public partial class SessionScreen
{
    private const string CoachName = "Coach";
    private const int CoachWidth = 520;
    private readonly HashSet<string> _coachSeen = new(StringComparer.Ordinal);
    private bool _coachSilenced;

    // The moments this screen offers, in the order they should be explained.
    private List<string> MomentsHere(InteractiveRunSession session)
    {
        var moments = new List<string>();
        // A branching map knows its current node by id; a linear one (the tutorial's) only by its position.
        var run = session.Run;
        var here = run.CurrentNodeId is { } id
            ? run.Map.Nodes.FirstOrDefault(n => n.Id.Value == id.Value)
            : run.Position >= 0 && run.Position < run.Map.Nodes.Count ? run.Map.Nodes[run.Position] : null;

        if (session.IsComplete)
            return ["compendium", "complete"];
        if (session.IsAwaitingNodeChoice)
            return ["welcome", "map"];
        if (session.IsAwaitingEntities && session.PendingEntities is { } pick
            && pick.Purpose.StartsWith("reward", StringComparison.Ordinal))
            return ["reward"];
        if (session.IsAwaitingChoice)
        {
            if (session.PendingShopShelf is not null)
                return ["shop"];
            if (here?.HasTag(MapNodeTags.Rest) == true)
                return ["rest"];
            return ["event"];
        }
        if (Play?.CombatDriver?.Current is { IsHeroTurn: true } combat)
        {
            // The tutorial's walk is fixed — there is no fork to stand at — so the welcome and the map are told
            // at the first fight's first turn, with the map opened for the purpose.
            moments.AddRange(["welcome", "map"]);
            var enemies = combat.State.Combatants.Count(c => c.Id != combat.HeroId
                && c.TeamId == RogueDeck.Core.Combat.StandardCombatIds.EnemyTeam && c.IsAlive);
            if (_ruled.Count > 0)
                moments.Add("elite.rules");
            if (enemies > 1)
                moments.Add("combat.multiple");
            moments.AddRange(["combat.hand", "combat.energy", "combat.intent", "combat.block", "combat.endturn"]);
            // Statuses and relics are explained the first fight they are actually there to look at.
            if (combat.State.Combatants.Any(c => c.Statuses.Any(s => s.Visibility == RogueDeck.Core.Combat.StatusVisibility.Visible)))
                moments.Add("combat.statuses");
            if (session.Run.Relics.Count > 0)
                moments.Add("combat.relics");
        }
        return moments;
    }

    private void CoachAfterDraw(InteractiveRunSession session)
    {
        DropCoach();
        if (!GameHost.Instance.IsTutorial || _coachSilenced)
            return;
        var extra = GameHost.Instance.Blueprint.Presentation.Game?.Extra ?? new Dictionary<string, string>();
        var moment = MomentsHere(session).FirstOrDefault(m => !_coachSeen.Contains(m) && extra.ContainsKey($"tutorial:{m}"));
        if (moment is null)
            return;
        // The frame is laid over what was just drawn, so wait for the layout to settle before measuring it.
        CallDeferred(nameof(ShowCoach), moment, extra[$"tutorial:{moment}"], extra.GetValueOrDefault($"tutorial.text:{moment}") ?? "");
    }

    // The moment on screen now, for the probe that walks the tutorial.
    private string? _coachMoment;

    private async void ShowCoach(string moment, string title, string text)
    {
        if (moment == "map" && GetNodeOrNull(MapOverlayName) is null)
            ToggleMapOverlay();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        DropCoach();
        _coachMoment = moment;
        var layer = new CanvasLayer { Name = CoachName, Layer = 20 };
        AddChild(layer);

        var screen = GetViewportRect().Size;
        Rect2? target = CoachTarget(moment) is { } control && IsInstanceValid(control) && control.IsVisibleInTree()
            ? control.GetGlobalRect()
            : null;
        if (target is { } rect)
        {
            var frame = new Panel { MouseFilter = MouseFilterEnum.Ignore, Position = rect.Position - new Vector2(6, 6),
                Size = rect.Size + new Vector2(12, 12) };
            var box = MoonvineTheme.Panel(new Color(0, 0, 0, 0), MoonvineTheme.Signal, radius: 8);
            box.BorderWidthTop = box.BorderWidthBottom = box.BorderWidthLeft = box.BorderWidthRight = 3;
            // A frame, not a fill: with a shadow the whole framed area came out tinted gold and the cards under it
            // could hardly be read.
            box.DrawCenter = false;
            frame.AddThemeStyleboxOverride("panel", box);
            layer.AddChild(frame);
            var pulse = frame.CreateTween().SetLoops();
            pulse.TweenProperty(frame, "modulate:a", 0.45f, 0.7).SetTrans(Tween.TransitionType.Sine);
            pulse.TweenProperty(frame, "modulate:a", 1f, 0.7).SetTrans(Tween.TransitionType.Sine);
        }

        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
        var style = MoonvineTheme.Panel(MoonvineTheme.BgRaised, MoonvineTheme.Signal, radius: 8);
        style.BorderWidthTop = style.BorderWidthBottom = style.BorderWidthLeft = style.BorderWidthRight = 2;
        style.ContentMarginLeft = style.ContentMarginRight = 18;
        style.ContentMarginTop = style.ContentMarginBottom = 12;
        panel.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(CoachWidth, 0) };
        column.AddThemeConstantOverride("separation", 8);
        var head = new Label { Text = title };
        head.AddThemeFontSizeOverride("font_size", 20);
        head.AddThemeColorOverride("font_color", MoonvineTheme.Signal);
        column.AddChild(head);
        // ⚠ A WRAPPING LABEL NEEDS ITS WIDTH BEFORE IT IS MEASURED. Without one its first minimum height is one
        // word per line — hundreds of points — and the panel standing on that measure put its words off-screen.
        var body = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(CoachWidth, 0),
        };
        body.AddThemeFontSizeOverride("font_size", 15);
        column.AddChild(body);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        var skip = new Button { Text = "Skip tutorial", Flat = true };
        skip.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        skip.Pressed += () =>
        {
            _coachSilenced = true;
            DropCoach();
        };
        var next = new Button { Text = "Next ▸", CustomMinimumSize = new Vector2(110, 36) };
        next.Pressed += () =>
        {
            _coachSeen.Add(moment);
            if (moment == "map")
                GetNodeOrNull(MapOverlayName)?.QueueFree();
            if (Session is { } session)
                CoachAfterDraw(session);
        };
        buttons.AddChild(skip);
        buttons.AddChild(next);
        column.AddChild(buttons);
        panel.AddChild(column);
        layer.AddChild(panel);

        // The panel stands where the frame is NOT: above a thing in the lower half, below one in the upper half,
        // and in the middle when there is nothing to frame. Measured after a frame of layout, for the reason above.
        panel.Visible = false;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!IsInstanceValid(panel))
            return;
        panel.ResetSize();
        var size = panel.GetCombinedMinimumSize();
        panel.Size = size;
        panel.Visible = true;
        var x = (screen.X - size.X) / 2;
        var y = target is not { } at ? (screen.Y - size.Y) / 2
            : at.GetCenter().Y > screen.Y / 2 ? Math.Max(52, at.Position.Y - size.Y - 18)
            : Math.Min(screen.Y - size.Y - 12, at.End.Y + 18);
        panel.Position = new Vector2(x, y);
        GD.Print($"coach: {moment}{(target is null ? "" : " (framed)")}");
    }

    // What each moment frames.
    private Control? CoachTarget(string moment) => moment switch
    {
        "map" => Find<MapView>(GetNodeOrNull(MapOverlayName) ?? this),
        "combat.hand" => _regionHand,
        "combat.energy" => FindNamed(this, "EnergyLine"),
        "combat.intent" => FindNamed(this, "IntentPlate"),
        "combat.block" => FindNamed(this, "Incoming"),
        "combat.endturn" => FindNamed(this, "EndTurnButton"),
        "combat.statuses" => FindNamed(this, "StatusChips"),
        "combat.relics" => FindNamed(_combatRoot, RelicGridName),
        "combat.multiple" => _enemyRow,
        "elite.rules" => FindNamed(this, "FightRules"),
        "reward" or "event" or "shop" or "rest" => _main,
        _ => null,
    };

    private static T? Find<T>(Godot.Node node) where T : Godot.Node
    {
        if (node is T found)
            return found;
        foreach (var child in node.GetChildren())
            if (Find<T>(child) is { } deeper)
                return deeper;
        return null;
    }

    private static Control? FindNamed(Godot.Node? node, string prefix)
    {
        if (node is null)
            return null;
        if (node is Control { Visible: true } control && control.Name.ToString().StartsWith(prefix, StringComparison.Ordinal))
            return control;
        foreach (var child in node.GetChildren())
            if (FindNamed(child, prefix) is { } deeper)
                return deeper;
        return null;
    }

    // `--smoke-tutorial`: walk the whole tutorial the way a player does — read every note the coach puts up
    // (pressing Next), then take the simplest answer the screen offers — and photograph each note (windowed).
    // It fails unless the run reaches its end and every one of the coach's moments was shown.
    private async System.Threading.Tasks.Task SmokeTutorial()
    {
        var shown = new List<string>();
        var saveStamp = Godot.FileAccess.GetModifiedTime("user://run-save.json");
        var windowed = !DisplayServer.GetName().Contains("headless");
        for (var step = 0; step < 900 && Session is { } session && Play is { } play; step++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (GetNodeOrNull(CoachName) is not null && _coachMoment is { } moment && !_coachSeen.Contains(moment))
            {
                shown.Add(moment);
                if (windowed)
                {
                    await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
                    GetViewport().GetTexture().GetImage().SavePng($"user://tutorial-{shown.Count:00}-{moment}.png");
                }
                _coachSeen.Add(moment);
                if (moment == "map")
                    GetNodeOrNull(MapOverlayName)?.QueueFree();
                CoachAfterDraw(session);
                continue;
            }
            if (session.IsComplete)
            {
                if (_coachSeen.Contains("complete"))
                    break;
                CoachAfterDraw(session);
                continue;
            }
            if (play.CombatDriver is { Current: not null } driver)
            {
                if (driver.PendingOptionChoice is { } options)
                    driver.SupplyOptionChoice([.. Enumerable.Range(0, Math.Min(driver.PendingOptionChoiceCount, options.Count))]);
                else if (driver.PendingCardChoice is { } cards)
                    driver.SupplyCardChoice([.. cards.Take(driver.PendingCardChoiceCount).Select(c => c.Id)]);
                else if (driver.Current!.IsHeroTurn)
                {
                    var combat = driver.Current;
                    var hero = combat.State.GetCombatant(combat.HeroId);
                    var card = combat.Hand.FirstOrDefault(c => CanPay(hero, c.DefinitionId.value));
                    var target = combat.State.Combatants.FirstOrDefault(c => c.Id != combat.HeroId && c.IsAlive
                        && c.TeamId == RogueDeck.Core.Combat.StandardCombatIds.EnemyTeam)?.Id;
                    if (card is not null)
                        driver.PlayCard(card.Id, target);
                    else
                        driver.EndTurn();
                }
            }
            else if (session.IsAwaitingNodeChoice)
                session.PickNode(session.PendingNodeChoices[0].Id.Value);
            else if (session.IsAwaitingInterlude)
                session.Continue();
            else if (session.IsAwaitingEntities)
                session.PickEntities([0]);
            else if (session.IsAwaitingChoice)
                session.Pick(session.PendingChoices[^1].Id);
            Rebuild();
        }
        var extra = GameHost.Instance.Blueprint.Presentation.Game?.Extra ?? new Dictionary<string, string>();
        var all = extra.Keys.Where(k => k.StartsWith("tutorial:", StringComparison.Ordinal))
            .Select(k => k["tutorial:".Length..]).ToList();
        var missing = all.Except(shown).ToList();
        var done = Session is { IsComplete: true } end;
        GD.Print($"smoke-tutorial: result={Session?.Run.Result} shown {shown.Count}/{all.Count}: {string.Join(" ", shown)}"
            + (missing.Count > 0 ? $" · NEVER SHOWN: {string.Join(" ", missing)}" : "")
            + $" · the player's save {(Godot.FileAccess.GetModifiedTime("user://run-save.json") == saveStamp ? "untouched" : "WRITTEN")}");
        GetTree().Quit(done && missing.Count == 0 ? 0 : 1);
    }

    // Out of the tree at once, not at the end of the frame: a coach queued for freeing still holds its name, and
    // the next one would be renamed past anything that looks for it.
    private void DropCoach()
    {
        if (GetNodeOrNull(CoachName) is { } old)
        {
            RemoveChild(old);
            old.QueueFree();
        }
    }
}
