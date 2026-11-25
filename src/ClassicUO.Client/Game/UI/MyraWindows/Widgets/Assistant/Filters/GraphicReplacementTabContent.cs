#nullable enable
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Map;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.ArtTexture;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Utility;
using Myra.Graphics2D.UI;
using Myra.Graphics2D.UI.WrapPanel;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Filters;

public static class GraphicReplacementTabContent
{
    private static readonly string[] TypeNames = { "Mobile", "Land", "Static" };
    private static readonly byte[] TypeValues = { 1, 2, 3 };

    private static string GetTypeName(byte t) => t switch { 1 => "Mobile", 2 => "Land", _ => "Static" };

    public static Widget Build() => Build(AgentPageInteraction.InGame);

    internal static Widget Build(AgentPageInteraction interaction)
    {
        var root = new VerticalStackPanel { Spacing = 6 };
        var status = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false, Wrap = true };

        root.Widgets.Add(new MyraLabel(
            "Replace graphics with other graphics. Mobile = animations, Land = terrain tiles, Static = items/statics.",
            MyraLabel.TextStyle.P) { Wrap = true });

        var filtersPanel = new VerticalStackPanel { Spacing = 2 };

        void BuildFilterList()
        {
            filtersPanel.Widgets.Clear();
            Dictionary<(ushort, byte), GraphicChangeFilter> filters = GraphicsReplacement.GraphicFilters;

            if (filters.Count == 0)
            {
                filtersPanel.Widgets.Add(new MyraLabel("No replacements configured.", MyraLabel.TextStyle.H3));
                return;
            }

            var grid = new MyraGrid();
            grid.SetupWithHeaders(
                GridColumnInfo.Auto(TazLang.Get("agent_col_enabled")),
                GridColumnInfo.Numeric("Original"),
                GridColumnInfo.Auto("Type"),
                GridColumnInfo.Fill("Replacement"),
                GridColumnInfo.Fill("Preview"),
                GridColumnInfo.Fill("New Hue"),
                GridColumnInfo.Auto(TazLang.Get("agent_col_actions"))
            );

            var filterList = filters.Values.ToList();
            int dataRow = 1;
            for (int i = filterList.Count - 1; i >= 0; i--)
            {
                GraphicChangeFilter filter = filterList[i];
                ushort capturedOrigGraphic = filter.OriginalGraphic;
                byte capturedOrigType = filter.OriginalType;

                // Enabled — disable a filter without deleting it
                grid.AddWidget(MyraCheckButton.CreateWithCallback(
                    filter.Enabled,
                    isEnabled =>
                    {
                        GraphicsReplacement.SetEnabled(capturedOrigGraphic, capturedOrigType, isEnabled);
                        RefreshEntities(capturedOrigGraphic, capturedOrigType);
                    },
                    tooltip: "Enable or disable this replacement"
                ), dataRow, 0);

                // Original — show as label (changing original = key change, use delete+re-add)
                grid.AddWidget(MyraGrid.TableText($"0x{filter.OriginalGraphic:X4}", alignRight: true), dataRow, 1);

                // Type — cycle button using wrapper panel (key change requires rebuild)
                var typeWrapper = new HorizontalStackPanel();
                void BuildTypeBtn()
                {
                    typeWrapper.Widgets.Clear();
                    var btn = new MyraButton(GetTypeName(filter.OriginalType), () =>
                    {
                        int idx = System.Array.IndexOf(TypeValues, filter.OriginalType);
                        byte newType = TypeValues[(idx + 1) % TypeValues.Length];
                        GraphicChangeFilter? moved = GraphicsReplacement.NewFilter(
                            filter.OriginalGraphic, newType,
                            filter.ReplacementGraphic, newType,
                            filter.NewHue);
                        if (moved == null)
                        {
                            Report("A replacement already uses this graphic and type.", Constants.HUE_ERROR);
                            return;
                        }
                        GraphicsReplacement.SetEnabled(moved.OriginalGraphic, newType, filter.Enabled);
                        GraphicsReplacement.DeleteFilter(filter.OriginalGraphic, filter.OriginalType);
                        RefreshEntities(filter.OriginalGraphic, filter.OriginalType);
                        RefreshEntities(moved.OriginalGraphic, newType);
                        BuildFilterList();
                    }) { Tooltip = "Click to cycle: Mobile / Land / Static", MinWidth = 65 };
                    btn.Content.HorizontalAlignment = HorizontalAlignment.Center;
                    typeWrapper.Widgets.Add(btn);
                }
                BuildTypeBtn();
                grid.AddWidget(typeWrapper, dataRow, 2);

                // Preview wrapper — rebuilt in-place when replacement changes
                var previewWrapper = new HorizontalStackPanel { Spacing = 2 };
                void BuildPreview()
                {
                    previewWrapper.Widgets.Clear();
                    if (filter.OriginalType == 3)
                    {
                        previewWrapper.Widgets.Add(new MyraArtTexture(filter.OriginalGraphic));
                        previewWrapper.Widgets.Add(new MyraLabel("→", MyraLabel.TextStyle.P));
                        previewWrapper.Widgets.Add(new MyraArtTexture(filter.ReplacementGraphic));
                    }
                    else
                    {
                        previewWrapper.Widgets.Add(new MyraLabel(
                            $"0x{filter.OriginalGraphic:X4} → 0x{filter.ReplacementGraphic:X4}", MyraLabel.TextStyle.P));
                    }
                }
                BuildPreview();
                grid.AddWidget(previewWrapper, dataRow, 4);

                // Replacement Graphic — inline edit, immediate commit + preview update
                var replacementBox = new MyraInputBox { Text = $"0x{filter.ReplacementGraphic:X4}" };
                replacementBox.LostFocus = () =>
                {
                    string txt = replacementBox.Text ?? "";
                    if (StringHelper.TryParseInt(txt, out int newReplacement) && newReplacement is >= 0 and <= ushort.MaxValue
                        && newReplacement != filter.ReplacementGraphic)
                    {
                        filter.ReplacementGraphic = (ushort)newReplacement;
                        filter.ReplacementType = filter.OriginalType;
                        BuildPreview();
                        RefreshEntities(capturedOrigGraphic, capturedOrigType);
                    }
                    replacementBox.Text = $"0x{filter.ReplacementGraphic:X4}";
                };
                grid.AddWidget(replacementBox, dataRow, 3);

                // Hue — inline edit, immediate commit
                var hueBox = MyraInputBox.Hue(filter.NewHue);
                hueBox.LostFocus = () =>
                {
                    if (MyraInputBox.TryParseHue(hueBox.Text, out ushort hue) && hue != filter.NewHue)
                    {
                        filter.NewHue = hue;
                        RefreshEntities(capturedOrigGraphic, capturedOrigType);
                    }
                    hueBox.Text = filter.NewHue == ushort.MaxValue ? "-1" : $"0x{filter.NewHue:X4}";
                };
                grid.AddWidget(hueBox, dataRow, 5);

                // Delete
                grid.AddWidget(MyraStyle.ApplyButtonDangerStyle(new MyraButton("Delete", () =>
                {
                    GraphicsReplacement.DeleteFilter(capturedOrigGraphic, capturedOrigType);
                    RefreshEntities(capturedOrigGraphic, capturedOrigType);
                    BuildFilterList();
                }) { Tooltip = "Delete this replacement" }), dataRow, 6);

                dataRow++;
            }

            filtersPanel.Widgets.Add(grid);
        }

        // Add entry panel
        var addEntryPanel = new VerticalStackPanel { Visible = false, Spacing = 4 };
        var newOriginalBox = new MyraInputBox { HintText = "Original graphic (e.g. 0x0EED)", Width = 170 };
        var newReplacementBox = new MyraInputBox { HintText = "Replacement graphic", Width = 170 };
        var newHueBox = MyraInputBox.Hue(ushort.MaxValue, 120, "Hue (-1 = unchanged)");
        int[] newTypeIndex = { 2 }; // Default: Static

        var newTypeWrapper = new HorizontalStackPanel();
        var validationLabel = new MyraLabel("", MyraLabel.TextStyle.P) { Visible = false };

        void BuildNewTypeBtn()
        {
            newTypeWrapper.Widgets.Clear();
            newTypeWrapper.Widgets.Add(new MyraButton(TypeNames[newTypeIndex[0]], () =>
            {
                newTypeIndex[0] = (newTypeIndex[0] + 1) % TypeNames.Length;
                BuildNewTypeBtn();
            }) { Tooltip = "Click to cycle: Mobile / Land / Static" });
        }
        BuildNewTypeBtn();

        var addConfirmRow = new HorizontalStackPanel { Spacing = 4 };
        addConfirmRow.Widgets.Add(new MyraButton("Add", () =>
        {
            string origText = newOriginalBox.Text ?? "";
            string replText = newReplacementBox.Text ?? "";

            if (!StringHelper.TryParseInt(origText, out int origGraphic) || origGraphic is < 0 or > ushort.MaxValue ||
                !StringHelper.TryParseInt(replText, out int replGraphic) || replGraphic is < 0 or > ushort.MaxValue)
            {
                validationLabel.Text = "Enter original and replacement graphics between 0 and 65535.";
                validationLabel.Visible = true;
                return;
            }

            if (!MyraInputBox.TryParseHue(newHueBox.Text, out ushort hue))
            {
                if (!string.IsNullOrEmpty(newHueBox.Text))
                {
                    validationLabel.Text = $"Invalid hue: '{newHueBox.Text}'. Must be 0-65535, 0x hex, or -1";
                    validationLabel.Visible = true;
                    return;
                }

                hue = ushort.MaxValue;
            }

            validationLabel.Visible = false;
            byte type = TypeValues[newTypeIndex[0]];
            if (GraphicsReplacement.NewFilter((ushort)origGraphic, type, (ushort)replGraphic, type, hue) == null)
            {
                validationLabel.Text = "A replacement already uses this graphic and type.";
                validationLabel.Visible = true;
                return;
            }
            RefreshEntities((ushort)origGraphic, type);

            newOriginalBox.Text = "";
            newReplacementBox.Text = "";
            newHueBox.Text = "";
            newTypeIndex[0] = 2;
            BuildNewTypeBtn();
            addEntryPanel.Visible = false;
            BuildFilterList();
        }));
        addConfirmRow.Widgets.Add(new MyraButton("Cancel", () =>
        {
            addEntryPanel.Visible = false;
            newOriginalBox.Text = "";
            newReplacementBox.Text = "";
            newHueBox.Text = "";
            validationLabel.Visible = false;
        }));

        var addFieldsRow1 = new WrapPanel { HorizontalSpacing = 4, VerticalSpacing = 4 };
        addFieldsRow1.Widgets.Add(new MyraLabel("Original:", MyraLabel.TextStyle.P));
        addFieldsRow1.Widgets.Add(newOriginalBox);
        addFieldsRow1.Widgets.Add(new MyraLabel("Replacement:", MyraLabel.TextStyle.P));
        addFieldsRow1.Widgets.Add(newReplacementBox);

        var addFieldsRow2 = new HorizontalStackPanel { Spacing = 4 };
        addFieldsRow2.Widgets.Add(new MyraLabel("Type:", MyraLabel.TextStyle.P));
        addFieldsRow2.Widgets.Add(newTypeWrapper);
        addFieldsRow2.Widgets.Add(new MyraLabel("New Hue:", MyraLabel.TextStyle.P));
        addFieldsRow2.Widgets.Add(newHueBox);

        addEntryPanel.Widgets.Add(new MyraLabel("New Entry:", MyraLabel.TextStyle.H3));
        addEntryPanel.Widgets.Add(addFieldsRow1);
        addEntryPanel.Widgets.Add(addFieldsRow2);
        addEntryPanel.Widgets.Add(validationLabel);
        addEntryPanel.Widgets.Add(addConfirmRow);

        var actionRow = new WrapPanel { HorizontalSpacing = 4, VerticalSpacing = 4 };
        var addEntry = new MyraButton(TazLang.Get("assistant_filters_add_entry"), () => addEntryPanel.Visible = !addEntryPanel.Visible);
        if (interaction.IsDesktop)
            DesktopAssistantTheme.ApplyActionButtonStyle(addEntry, primary: true);
        actionRow.Widgets.Add(addEntry);
        actionRow.Widgets.Add(new MyraButton("Target Entity", () =>
        {
            if (World.Instance == null) return;
            interaction.SelectTarget(targeted =>
            {
                if (targeted == null) return;
                ushort graphic = 0;
                ushort hue = 0;
                byte entityType = 3;

                if (targeted is Mobile mob) { graphic = mob.Graphic; hue = mob.Hue; entityType = 1; }
                else if (targeted is Land land) { graphic = land.Graphic; hue = land.Hue; entityType = 2; }
                else if (targeted is Entity entity) { graphic = entity.Graphic; hue = entity.Hue; }
                else if (targeted is Static stat) { graphic = stat.Graphic; hue = stat.Hue; }
                else if (targeted is GameObject obj) { graphic = obj.Graphic; hue = obj.Hue; }
                else return;

                if (GraphicsReplacement.NewFilter(graphic, entityType, graphic, entityType, hue) != null)
                    RefreshEntities(graphic, entityType);
                BuildFilterList();
            });
        }) { Tooltip = "Target an entity to add it to the replacement list" });
        actionRow.Widgets.Add(new MyraButton("Import", () =>
        {
            string? json = Clipboard.GetClipboardText();
            if (json.NotNullNotEmpty() && GraphicsReplacement.ImportFromJson(json, message => Report(message, Constants.HUE_SUCCESS)))
            {
                RefreshEntities();
                BuildFilterList();
                return;
            }
            Report("The clipboard does not contain a filter list.", Constants.HUE_ERROR);
        }) { Tooltip = "Import from your clipboard, must have a valid export copied." });
        actionRow.Widgets.Add(new MyraButton("Export", () =>
        {
            GraphicsReplacement.GetJsonExport()?.CopyToClipboard();
            Report("Copied graphic filters to the clipboard.", Constants.HUE_SUCCESS);
        }) { Tooltip = "Export your filters to your clipboard." });
        actionRow.Widgets.Add(new MyraButton("Apply to All Entities", () =>
        {
            int count = RefreshEntities();
            Report($"Refreshed {count} objects.", Constants.HUE_SUCCESS);
        }) { Tooltip = "Reapply graphic replacements to all entities currently in the world" });

        root.Widgets.Add(actionRow);
        root.Widgets.Add(status);
        root.Widgets.Add(addEntryPanel);
        root.Widgets.Add(new MyraLabel("Current Graphic Replacements:", MyraLabel.TextStyle.H3));
        BuildFilterList();
        root.Widgets.Add(interaction.IsDesktop ? filtersPanel : new ScrollViewer { Height = 300, Content = filtersPanel });

        return root;

        void Report(string message, ushort hue)
        {
            if (interaction.IsDesktop)
            {
                status.Text = message;
                status.Visible = true;
            }
            else
                GameActions.Print(message, hue);
        }
    }

    private static int RefreshEntities(ushort? graphic = null, byte? type = null)
    {
        World? world = World.Instance;
        if (world == null)
            return 0;

        int count = 0;
        if (type is null or 1)
        {
            foreach (Mobile mobile in world.Mobiles.Values.ToList())
                Refresh(mobile);
        }
        if (type is null or 3)
        {
            foreach (Item item in world.Items.Values.ToList())
                Refresh(item);
        }
        if ((type is null or 2 or 3) && world.Map != null)
        {
            foreach (Chunk chunk in world.Map.GetUsedChunks())
                for (int x = 0; x < 8; x++)
                    for (int y = 0; y < 8; y++)
                        for (GameObject? obj = chunk?.GetHeadObject(x, y); obj != null; obj = obj.TNext)
                            if (((obj is Land && (type is null or 2)) || (obj is Static && (type is null or 3))) &&
                                !obj.IsDestroyed && (graphic == null || obj.OriginalGraphic == graphic))
                            {
                                obj.UpdateGraphicBySeason();
                                count++;
                            }
        }
        return count;

        void Refresh(GameObject obj)
        {
            if (obj.IsDestroyed || (graphic != null && obj.OriginalGraphic != graphic))
                return;

            obj.Graphic = obj.OriginalGraphic;
            count++;
        }
    }
}
