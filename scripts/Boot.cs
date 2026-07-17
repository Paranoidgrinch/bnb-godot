using Godot;
using RogueDeck.Run;

namespace BnbGodot;

// The boot proof: load the shipped game document through the engine's real loader and show its title.
// Run with `--headless -- --smoke` it prints the load line and quits — the CI-able TFM/reference check.
public partial class Boot : Control
{
    public override void _Ready()
    {
        var json = Godot.FileAccess.GetFileAsString("res://content/game.roguedeck.json");
        var options = RunJson.CreateOptions();
        var blueprint = RunJson.BlueprintFromJson(json, options);
        var title = blueprint.Presentation.Game?.FlavorText ?? "RogueDeck game";

        GetNode<Label>("Title").Text = title;
        GetNode<Label>("Stats").Text =
            $"{blueprint.Cards.Count} cards · {blueprint.Encounters.Count} encounters · "
            + $"{blueprint.Relics.Count} relics · {blueprint.Map.Nodes.Count} map nodes";
        GD.Print($"loaded: {title} ({blueprint.Cards.Count} cards, {blueprint.Map.Nodes.Count} map nodes)");

        if (OS.GetCmdlineUserArgs().Contains("--smoke"))
            GetTree().Quit();
    }
}
