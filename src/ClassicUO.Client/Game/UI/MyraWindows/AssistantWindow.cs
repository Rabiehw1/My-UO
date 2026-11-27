using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemDatabase;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Skills;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows;

public class AssistantWindow : MyraControl
{
    public static void Show()
    {
        foreach (IGui g in UIManager.Gumps)
        {
            if (g is AssistantWindow w)
            {
                w.CenterInViewPort();
                w.BringOnTop();
                // The window is reused, so its cached pages may predate changes made elsewhere
                // (e.g. items shift-clicked into the auto loot list). Rebuild the visible page.
                w.RefreshSelectedContent();
                return;
            }
        }
        UIManager.Add(new AssistantWindow());
    }

    internal static void ShowItemLists()
    {
        foreach (IGui g in UIManager.Gumps)
        {
            if (g is AssistantWindow window && !window.IsClosing)
            {
                window._tabs.TrySelectByLabel(ItemListsTabContent.Title);
                window.BringOnTop();
                return;
            }
        }

        var assistant = new AssistantWindow();
        assistant._tabs.TrySelectByLabel(ItemListsTabContent.Title);
        UIManager.Add(assistant);
    }

    private SkillsTabContent _skillsTabContent;
    private MyraTabControl _tabs;

    public AssistantWindow() : base(TazLang.Get("assistantwindow_title", "Legion Assistant"))
    {
        CanBeSaved = true;
        Build();
        CenterInViewPort();

        EventSink.SkillValueChangedEvent += EventSkillUpdated;
        EventSink.SkillBaseChangedEvent += EventSkillUpdated;
        EventSink.SkillCapChangedEvent += EventSkillUpdated;
    }

    private void EventSkillUpdated(object sender, SkillChangeArgs e) => _skillsTabContent?.UpdateSkills();

    public override void Dispose()
    {
        base.Dispose();

        HotkeysTabContent.Cleanup();

        EventSink.SkillValueChangedEvent -= EventSkillUpdated;
        EventSink.SkillBaseChangedEvent -= EventSkillUpdated;
        EventSink.SkillCapChangedEvent -= EventSkillUpdated;
    }

    private void Build()
    {
        _tabs = new MyraTabControl();
        _tabs.AddTab(GeneralTab.Title, GeneralTab.Build, rebuildOnSelect: true);
        _tabs.AddTab(TazLang.Get("assistantwindow_tab_agents", "Agents"), AgentTab.Build, rebuildOnSelect: true);
        _tabs.AddTab(ItemListsTabContent.Title, () => ItemListsTabContent.Build(AgentPageInteraction.InGame), rebuildOnSelect: true);
        _tabs.AddTab(TazLang.Get("assistantwindow_tab_itemdatabase", "Item Database"), ItemDatabaseTabContent.Build);
        _tabs.AddTab(HotkeysMacrosTabContent.Title, () => HotkeysMacrosTabContent.Build(AgentPageInteraction.InGame), rebuildOnSelect: true);
        _tabs.AddTab(TazLang.Get("assistantwindow_tab_skills", "Skills"), () => _skillsTabContent = new SkillsTabContent());
        var openDesktop = new MyraButton(
            TazLang.Get("assistant_open_desktop", "Open Desktop Assistant"),
            () => DesktopAgentsWindow.Show(_tabs.SelectedItem?.Text)) { Visible = false, HorizontalAlignment = HorizontalAlignment.Left };
        _tabs.SelectedIndexChanged += (_, _) => openDesktop.Visible = _tabs.SelectedIndex == 1
            || _tabs.SelectedItem?.Text == HotkeysMacrosTabContent.Title
            || _tabs.SelectedItem?.Text == GeneralTab.Title
            || _tabs.SelectedItem?.Text == ItemListsTabContent.Title;

        var root = new VerticalStackPanel { Spacing = MyraStyle.STANDARD_SPACING };
        root.Widgets.Add(openDesktop);
        root.Widgets.Add(_tabs);
        _tabs.SelectFirst();
        SetRootContent(root);
    }

    private void RefreshSelectedContent() => _tabs?.RefreshSelectedContent();
}
