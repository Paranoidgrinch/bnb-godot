using Godot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Run;

namespace BnbGodot;

// ── THE END OF A RUN, FILED ──────────────────────────────────────────────────────
// A run ends on a form, because in this game everything does. A win is a FINAL REPORT stamped APPROVED; a death
// is a CERTIFICATE OF DEATH, filed, with the cause entered in the proper box. What is on it is only what the
// game actually knows: who, how far, what was felled (RunTally), what was left, what was carried — so the
// picture a player sends a friend is a true one.
public partial class SessionScreen
{
    private void RenderComplete(InteractiveRunSession session)
    {
        if (GameHost.Instance.IsTutorial)
        {
            RenderTutorialEnd(session);
            return;
        }
        var run = session.Run;
        var victory = run.Result == RunResult.Victory;
        var tally = RunTally.Counts;
        var cause = victory ? null : CauseOfDeath();
        var rooms = RunLog.Latest?.Rooms.Count;

        var sheet = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(760, 0) };
        sheet.AddThemeStyleboxOverride("panel", MoonvineTheme.WoodPanel(rim: true));
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 26);
        sheet.AddChild(margin);
        var form = new VBoxContainer();
        form.AddThemeConstantOverride("separation", 8);
        margin.AddChild(form);

        var office = new Label
        {
            Text = "OFFICE OF RECORDS · DEPARTMENT OF CONCLUDED MATTERS",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        office.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        office.AddThemeFontSizeOverride("font_size", 12);
        form.AddChild(office);
        var heading = new Label
        {
            Text = victory ? "FINAL REPORT" : "CERTIFICATE OF DEATH",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        heading.AddThemeFontSizeOverride("font_size", 28);
        heading.AddThemeColorOverride("font_color", victory ? MoonvineTheme.Accent : MoonvineTheme.Text);
        form.AddChild(heading);
        var number = new Label
        {
            Text = $"Case no. {run.RandomSeed} · filed {DateTime.Now:yyyy-MM-dd HH:mm}",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        number.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        form.AddChild(number);
        form.AddChild(new HSeparator());

        var fields = new GridContainer { Columns = 2 };
        fields.AddThemeConstantOverride("h_separation", 24);
        fields.AddThemeConstantOverride("v_separation", 6);
        void Field(string label, string value, Color? colour = null)
        {
            var name = new Label { Text = label, CustomMinimumSize = new Vector2(220, 0) };
            name.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
            fields.AddChild(name);
            var entry = new Label { Text = value, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0) };
            entry.AddThemeColorOverride("font_color", colour ?? MoonvineTheme.Text);
            fields.AddChild(entry);
        }
        Field("Applicant", PlayerIdentity.Name is { } player ? $"{Play?.HeroName} ({player})" : Play?.HeroName ?? "—");
        Field("Outcome", victory ? "Every act concluded. The Ledger is closed." : $"Deceased in Act {run.ActNumber}",
            victory ? MoonvineTheme.Accent : MoonvineTheme.Harm);
        if (cause is not null)
            Field("Cause of death", cause, MoonvineTheme.Harm);
        Field("Rooms entered", rooms is > 0 ? rooms.Value.ToString() : "—");
        Field("Enemies felled", tally.GetValueOrDefault(RunTally.Enemies).ToString());
        Field("Elites · bosses defeated", $"{tally.GetValueOrDefault(RunTally.Elites)} · {tally.GetValueOrDefault(RunTally.Bosses)}");
        Field("Health on closing", $"{Math.Max(run.Health.Current, 0)} / {run.Health.Max}");
        Field("Gold on account", run.Resources.GetValueOrDefault(StandardRunIds.Gold).ToString());
        Field("Deck on file", $"{run.Deck.Count} cards · {run.Deck.Count(c => c.UpgradeLevel > 0)} improved");
        Field("Relics in custody", run.Relics.Count == 0 ? "none"
            : string.Join(", ", run.Relics.Select(r => r.Definition.DisplayName)));
        form.AddChild(fields);
        form.AddChild(new HSeparator());

        var remark = new Label
        {
            Text = "Remarks: " + (victory
                ? "The applicant's file is closed. No further action is required. Congratulations are not a service this office provides."
                : "The applicant is advised not to reapply in person. Next of kin may collect the deck at counter 3."),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        remark.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        remark.AddThemeFontSizeOverride("font_size", 13);
        form.AddChild(remark);

        // THE STAMP, laid across the corner of the sheet the way a clerk brings it down: at an angle, not quite
        // square, over whatever was written there. Only on a win: a death certificate is left unstamped.
        var holder = new Control { CustomMinimumSize = new Vector2(0, 0), MouseFilter = MouseFilterEnum.Ignore };
        var stamp = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, RotationDegrees = -14 };
        var ink = MoonvineTheme.Accent;
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0),
            BorderColor = ink,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        box.SetBorderWidthAll(4);
        box.SetCornerRadiusAll(6);
        stamp.AddThemeStyleboxOverride("panel", box);
        var word = new Label { Text = "APPROVED" };
        word.AddThemeFontSizeOverride("font_size", 30);
        word.AddThemeColorOverride("font_color", ink);
        stamp.AddChild(word);
        stamp.Position = new Vector2(430, -300);
        stamp.Modulate = new Color(1, 1, 1, 0.85f);
        holder.AddChild(stamp);
        if (victory)
            form.AddChild(holder);

        var centre = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        centre.AddChild(sheet);
        _main.AddChild(centre);

        _main.AddChild(ComplaintForm(victory));

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        var back = new Button { Text = "Back to title", CustomMinimumSize = new Vector2(180, 44) };
        back.Pressed += () =>
        {
            // Leaving the report is what sends the run home — with the player's word in it, if they gave one.
            RunLog.Release(GameHost.Instance);
            GetTree().ChangeSceneToFile("res://scenes/Boot.tscn");
        };
        buttons.AddChild(back);
        var copy = new Button { Text = $"Copy seed {run.RandomSeed}", CustomMinimumSize = new Vector2(180, 44) };
        copy.Pressed += () =>
        {
            DisplayServer.ClipboardSet(run.RandomSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Toast("Seed copied.");
        };
        buttons.AddChild(copy);
        _main.AddChild(buttons);
    }

    // ── FORM B-7: COMPLAINTS AND COMMENDATIONS ───────────────────────────────────
    // One question, the same after a win and a death so the answers can be compared: how fair did it feel? A
    // rating of 1–5 and an optional line, written into the run's recording before it is sent (RunLog.Feedback),
    // so every rating in bnb-runs sits beside the run it is about. Filing is optional and filed once.
    private int _rating;
    private string _remark = "";
    private bool _filed;

    private Control ComplaintForm(bool victory)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(760, 0) };
        panel.AddThemeStyleboxOverride("panel", MoonvineTheme.Panel(MoonvineTheme.BgPanel, MoonvineTheme.Hairline));
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 14);
        panel.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 8);
        margin.AddChild(column);

        var head = new Label { Text = "FORM B-7 · COMPLAINTS AND COMMENDATIONS" };
        head.AddThemeFontSizeOverride("font_size", 12);
        head.AddThemeColorOverride("font_color", MoonvineTheme.TextMuted);
        column.AddChild(head);

        if (_filed)
        {
            var thanks = new Label
            {
                Text = "Filed. Your submission has been received and will be read, eventually, by someone.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            thanks.AddThemeColorOverride("font_color", MoonvineTheme.Accent);
            column.AddChild(thanks);
            return Centred(panel);
        }

        var question = new Label { Text = victory ? "How fair did this run feel?" : "Was this death fair?" };
        column.AddChild(question);
        var scale = new HBoxContainer();
        scale.AddThemeConstantOverride("separation", 6);
        string[] words = ["1 · rigged", "2", "3", "4", "5 · entirely fair"];
        var group = new ButtonGroup();
        for (var i = 0; i < words.Length; i++)
        {
            var value = i + 1;
            var button = new Button
            {
                Text = words[i],
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = _rating == value,
                CustomMinimumSize = new Vector2(i is 0 or 4 ? 150 : 56, 36),
                Name = $"Rating{value}",
            };
            button.Toggled += on =>
            {
                if (on)
                    _rating = value;
            };
            scale.AddChild(button);
        }
        column.AddChild(scale);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var comment = new LineEdit
        {
            PlaceholderText = "Remarks (optional) — what made it so?",
            MaxLength = 500,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Name = "Remarks",
            // ⚠ The screen can be redrawn under the player's typing; what they wrote is kept here, not in the box.
            Text = _remark,
        };
        comment.TextChanged += text => _remark = text;
        row.AddChild(comment);
        var file = new Button { Text = "File it", CustomMinimumSize = new Vector2(110, 0), Name = "FileIt" };
        file.Pressed += () =>
        {
            if (_rating == 0)
            {
                Toast("Please tick a box. The form cannot be filed without one.");
                return;
            }
            RunLog.Feedback(_rating, _remark);
            _filed = true;
            Rebuild();
        };
        row.AddChild(file);
        column.AddChild(row);
        return Centred(panel);
    }

    private static Control Centred(Control control)
    {
        var centre = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        centre.AddChild(control);
        return centre;
    }

    // What ended the run: the enemies still standing in the last fight, by name (RunTally.LastFoes).
    private static string? CauseOfDeath() =>
        RunTally.LastFoes.Count == 0 ? null : string.Join(" & ", RunTally.LastFoes);

    // `--smoke-report`: a run started on 3 health walks into its first fight and ends turns until it is over,
    // and the certificate it ends on is photographed.
    private async System.Threading.Tasks.Task SmokeReport()
    {
        var combat = WalkToFirstFight();
        for (var guard = 0; guard < 60 && Play?.CombatDriver?.Current is { IsOver: false } fight; guard++)
            if (fight.IsHeroTurn)
                EndTurnNow();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Rebuild();
        var over = Session is { IsComplete: true };

        // Fill Form B-7 the way a player does: tick 2, write a line, and photograph it before filing.
        // The LAST of each name: a redraw queues the old form for freeing, and it is still in the tree this frame.
        T? Newest<T>(string name) where T : Godot.Node =>
            FindChildren(name, "", recursive: true, owned: false).OfType<T>().LastOrDefault();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (Newest<Button>("Rating2") is { } two)
            two.ButtonPressed = true;
        if (Newest<LineEdit>("Remarks") is { } remarks)
        {
            remarks.Text = "the ward hit harder than it said";
            remarks.EmitSignal(LineEdit.SignalName.TextChanged, remarks.Text);
        }
        var ticked = _rating == 2;
        if (!DisplayServer.GetName().Contains("headless"))
        {
            await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
            GetViewport().GetTexture().GetImage().SavePng("user://smoke-report.png");
        }
        Newest<Button>("FileIt")?.EmitSignal(BaseButton.SignalName.Pressed);
        var ok = over && ticked && _filed && _remark.Length > 0;
        GD.Print($"smoke-report: fight={(combat is not null)} run over={over} result={Session?.Run.Result} "
            + $"cause={CauseOfDeath() ?? "—"} ticked={ticked} filed={_filed} {(ok ? "PASS" : "FAIL")}");
        await CaptureThenQuit("smoke-report-filed.png", ok ? 0 : 1);
    }

    // The end of a lesson is not a report: no form, no upload, no ranking — a word, and the two ways on.
    private void RenderTutorialEnd(InteractiveRunSession session)
    {
        var won = session.Run.Result == RunResult.Victory;
        Title(won ? "Tutorial complete" : "The tutorial ended early");
        Muted(won
            ? "You have seen every kind of room. A real run is five acts of this, with a new map every time."
            : "Even a lesson can go wrong. You have still seen how it all works — try it again, or start for real.");
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        var again = new Button { Text = "Play the tutorial again", CustomMinimumSize = new Vector2(220, 44) };
        again.Pressed += () =>
        {
            _coachSeen.Clear();
            _coachSilenced = false;
            GameHost.Instance.StartTutorial();
        };
        var title = new Button { Text = "Back to the title", CustomMinimumSize = new Vector2(220, 44) };
        title.Pressed += () =>
        {
            GameHost.Instance.AbandonRunInMemory();
            GetTree().ChangeSceneToFile("res://scenes/Boot.tscn");
        };
        row.AddChild(again);
        row.AddChild(title);
        _main.AddChild(row);
    }
}
