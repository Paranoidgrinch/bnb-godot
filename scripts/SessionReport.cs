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
        var run = session.Run;
        var victory = run.Result == RunResult.Victory;
        var tally = RunTally.Counts;
        var cause = victory ? null : CauseOfDeath();
        var rooms = RunLog.Current?.Rooms.Count;

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
        // square, over whatever was written there.
        var holder = new Control { CustomMinimumSize = new Vector2(0, 0), MouseFilter = MouseFilterEnum.Ignore };
        var stamp = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, RotationDegrees = -14 };
        var ink = victory ? MoonvineTheme.Accent : MoonvineTheme.Harm;
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0),
            BorderColor = ink,
            ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 4, ContentMarginBottom = 4,
        };
        box.SetBorderWidthAll(4);
        box.SetCornerRadiusAll(6);
        stamp.AddThemeStyleboxOverride("panel", box);
        var word = new Label { Text = victory ? "APPROVED" : "FILED · DECEASED" };
        word.AddThemeFontSizeOverride("font_size", 30);
        word.AddThemeColorOverride("font_color", ink);
        stamp.AddChild(word);
        stamp.Position = new Vector2(430, -300);
        stamp.Modulate = new Color(1, 1, 1, 0.85f);
        holder.AddChild(stamp);
        form.AddChild(holder);

        var centre = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        centre.AddChild(sheet);
        _main.AddChild(centre);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        var back = new Button { Text = "Back to title", CustomMinimumSize = new Vector2(180, 44) };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Boot.tscn");
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
        GD.Print($"smoke-report: fight={(combat is not null)} run over={over} result={Session?.Run.Result} "
            + $"cause={CauseOfDeath() ?? "—"} {(over ? "PASS" : "FAIL")}");
        await CaptureThenQuit("smoke-report.png", over ? 0 : 1);
    }
}
