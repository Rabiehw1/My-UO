#nullable enable
using System;
using System.Collections.Generic;
using ClassicUO.Game.UI.MyraWindows.Theme;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets;

public class MyraTabControl : TabControl
{
    private readonly List<Func<Widget>> _builders = new();
    private readonly List<bool> _rebuildOnSelect = new();
    private readonly bool _desktop = DesktopAssistantTheme.Current != null;

    public MyraTabControl()
    {
        SelectedIndexChanged += OnTabSelected;
    }

    /// <param name="rebuildOnSelect">
    ///     Rebuild the page every time the tab is selected instead of caching it. Use for pages that
    ///     mirror state changed elsewhere (e.g. agent lists the game can edit), so revisiting the tab
    ///     shows what is current. Avoid where the page holds unsaved input state.
    /// </param>
    public void AddTab(string name, Func<Widget> builder, string? tooltip = null, bool rebuildOnSelect = false)
    {
        int index = _builders.Count;
        _builders.Add(builder);
        _rebuildOnSelect.Add(rebuildOnSelect);

        var item = new TabItem(name) { Tag = index };
        Items.Add(item);
        if (_desktop && FindHeader(item) is { } header)
            header.AcceptsKeyboardFocus = true;

        if (!string.IsNullOrEmpty(tooltip) && FindHeader(item) is { } tooltipHeader)
            tooltipHeader.Tooltip = tooltip;
    }

    /// <summary>
    /// Finds the header in Myra's visual tree because <see cref="TabItem"/> doesn't expose it.
    /// The first grid under <c>InternalChild</c> tags each header with its owning tab.
    /// </summary>
    private ButtonBase2? FindHeader(TabItem item)
    {
        Grid? buttonsGrid = null;
        foreach (Widget w in InternalChild.Widgets)
        {
            if (w is Grid grid)
            {
                buttonsGrid = grid;
                break;
            }
        }

        if (buttonsGrid == null)
            return null;

        foreach (Widget button in buttonsGrid.Widgets)
        {
            if (ReferenceEquals(button.Tag, item))
                return button as ButtonBase2 ?? button.FindChild<ButtonBase2>();
        }

        return null;
    }

    /// <summary>
    /// Selects the tab whose header matches <paramref name="label"/>, so a caller holding a path of
    /// labels can drive the control without knowing tab indices.
    /// </summary>
    /// <param name="label">The tab header text to look for</param>
    /// <returns>Whether a matching tab was found and selected</returns>
    public bool TrySelectByLabel(string? label)
    {
        if (string.IsNullOrEmpty(label))
            return false;

        for (int i = 0; i < Items.Count; i++)
        {
            if (!string.Equals(Items[i].Text, label, StringComparison.Ordinal))
                continue;

            // Assigning an already-current index raises no event, but the page was built when it
            // was first selected, so the content is present either way.
            SelectedIndex = i;

            return true;
        }

        return false;
    }

    /// <summary>The selected tab's built content, or <see langword="null"/> when nothing is selected</summary>
    public Widget? SelectedContent => SelectedItem?.Content;

    public void SelectFirst()
    {
        if (Items.Count > 0)
            SelectedIndex = 0;
    }

    /// <summary>Rebuilds the selected page while preserving its parent tabs and their selections.</summary>
    internal void RefreshSelectedContent()
    {
        if (SelectedItem?.Content is MyraTabControl nestedTabs)
        {
            nestedTabs.RefreshSelectedContent();
        }
        else if (SelectedItem?.Tag is int index && index >= 0 && index < _builders.Count)
        {
            SelectedItem.Content = _builders[index]();
            UpdateDesktopCaptions();
        }
    }

    private void OnTabSelected(object? sender, EventArgs e)
    {
        if (SelectedItem?.Tag is int idx && idx < _builders.Count &&
            (SelectedItem.Content == null || _rebuildOnSelect[idx]))
            SelectedItem.Content = _builders[idx]();

        UpdateDesktopCaptions();
    }

    private void UpdateDesktopCaptions()
    {
        if (!_desktop)
            return;

        foreach (TabItem item in Items)
        {
            if (FindHeader(item)?.FindChild<Label>() is not { } caption)
                continue;

            bool selected = ReferenceEquals(item, SelectedItem);
            caption.TextColor = selected ? DesktopAssistantTheme.Accent : DesktopAssistantTheme.Text;
            caption.Font = DesktopAssistantTheme.Font(bold: selected);
        }
    }
}
