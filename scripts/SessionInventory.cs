using Godot;

namespace BnbGodot;

// ── THE INVENTORY (user, 2026-09-28) ─────────────────────────────────────────────────────────────────────────
// Everything the player carries, in one place, on I or the HUD's button: the relics, the cards and the
// consumables, a tab each. It replaced the top bar's relic shelf (outside a fight — in one, the relics stand over
// the hero, where the ones that fire are seen firing) and the "Deck" button under the hand.
//
// It hangs off the SCREEN on its own canvas layer like the pile viewer, and for the same reason: an enemy acting
// underneath redraws the fight and must leave the inventory standing.
public partial class SessionScreen
{
    private const string InventoryOverlayName = "InventoryOverlay";

    internal enum InventoryTab { Relics, Cards, Consumables }

    // The tab the inventory opens on is the one it was last left on.
    private InventoryTab _inventoryTab = InventoryTab.Cards;

    // A pile viewer or the inventory, whichever is up — both cover the table the same way.
    private Godot.Node? CardViewerOpen => GetNodeOrNull(PileOverlayName) ?? GetNodeOrNull(InventoryOverlayName);

    private void ToggleInventory()
    {
        if (GetNodeOrNull(InventoryOverlayName) is { } open)
        {
            open.QueueFree();
            RemoveChild(open);
            return;
        }
        OpenInventory(_inventoryTab);
    }

    private void CloseInventory() => GetNodeOrNull(InventoryOverlayName)?.QueueFree();

    private void OpenInventory(InventoryTab tab)
    {
        if (GetNodeOrNull(InventoryOverlayName) is { } previous)
        {
            previous.QueueFree();
            RemoveChild(previous);
        }
        if (AnyMenuOpen || Session is null)
            return;
        ClosePile();
        _inventoryTab = tab;
        var run = Session.Run;

        var layer = new CanvasLayer { Name = InventoryOverlayName, Layer = 50 };
        var veil = new Control { MouseFilter = MouseFilterEnum.Stop, Theme = Theme };
        veil.SetAnchorsPreset(LayoutPreset.FullRect);
        layer.AddChild(veil);
        var dim = new ColorRect { Color = new Color(MoonvineTheme.Bg, 0.94f) };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        dim.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                CloseInventory();
        };
        veil.AddChild(dim);

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.SetAnchorsPreset(LayoutPreset.FullRect);
        column.OffsetLeft = 24;
        column.OffsetRight = -24;
        column.OffsetTop = 16;
        column.OffsetBottom = -16;
        column.AddThemeConstantOverride("separation", 10);
        veil.AddChild(column);

        var head = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        head.AddThemeConstantOverride("separation", 10);
        var heading = new Label { Text = "Inventory" };
        heading.AddThemeFontSizeOverride("font_size", 20);
        head.AddChild(heading);
        foreach (var (each, label) in new[]
        {
            (InventoryTab.Relics, $"Relics ({run.Relics.Count})"),
            (InventoryTab.Cards, $"Cards ({run.Deck.Count})"),
            (InventoryTab.Consumables, $"Consumables ({run.Consumables.Count})"),
        })
        {
            var button = new Button { Text = label, ToggleMode = true, ButtonPressed = each == tab };
            var which = each;
            button.Pressed += () => OpenInventory(which);
            head.AddChild(button);
        }
        var close = new Button { Text = "Close (Esc)" };
        close.Pressed += CloseInventory;
        head.AddChild(close);
        column.AddChild(head);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(body);
        column.AddChild(scroll);

        switch (tab)
        {
            case InventoryTab.Cards:
                foreach (var section in DeckSections().Where(section => section.Cards.Count > 0))
                {
                    var gallery = Gallery();
                    foreach (var (definition, upgrade, caption) in section.Cards)
                        gallery.AddChild(CardPick(definition, upgrade, selected: false, caption: caption, onClick: null));
                    body.AddChild(gallery);
                }
                break;

            case InventoryTab.Relics:
                if (run.Relics.Count == 0)
                    body.AddChild(Centred(MutedLabel("No relics yet.")));
                foreach (var relic in run.Relics)
                {
                    var look = GameHost.Instance.Blueprint.Presentation.Relics.GetValueOrDefault(relic.Id.Value);
                    body.AddChild(Entry(RelicTile(relic, 56), relic.Definition.DisplayName + (relic.Enabled ? "" : " (off)"),
                        Glossary.Explain(look?.FlavorText)));
                }
                break;

            default:
                if (run.Consumables.Count == 0)
                    body.AddChild(Centred(MutedLabel("No consumables.")));
                foreach (var consumable in run.Consumables)
                {
                    var id = consumable.DefinitionId.Value;
                    var look = GameHost.Instance.Blueprint.Presentation.Consumables.GetValueOrDefault(id);
                    body.AddChild(Entry(ConsumableIcon(id, 56), ConsumableName(id), Glossary.Explain(look?.FlavorText)));
                }
                break;
        }

        AddChild(layer);
    }

    // One carried thing as a row: its tile, its name, what it does.
    private static Control Entry(Control tile, string name, string text)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(640, 0) };
        row.AddThemeConstantOverride("separation", 14);
        row.AddChild(tile);
        var words = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var title = new Label { Text = name };
        title.AddThemeFontSizeOverride("font_size", 17);
        words.AddChild(title);
        var about = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        about.AddThemeColorOverride("font_color", MoonvineTheme.TextSoft);
        words.AddChild(about);
        row.AddChild(words);
        return row;
    }
}
