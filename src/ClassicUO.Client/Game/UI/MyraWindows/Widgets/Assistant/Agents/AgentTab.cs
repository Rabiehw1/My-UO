using System;
using ClassicUO.Configuration;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

public static class AgentTab
{
    internal enum AgentGroup
    {
        ItemLists,
        Settings,
        Controls,
        HealingAndStats,
        Support,
        LootAndScavenge,
        Vendors,
        GearAndInventory
    }

    internal sealed record AgentPage(
        string Name,
        AgentGroup Group,
        Func<AgentPageInteraction, Widget> Build,
        bool RebuildOnSelect = false);

    internal static AgentPage[] GetPages() =>
    [
        new(AutoLootAgentTabContent.Title, AgentGroup.LootAndScavenge, AutoLootAgentTabContent.Build, true),
        new(TazLang.Get("assistant_agent_tab_scavenger", "Scavenger"), AgentGroup.LootAndScavenge, ScavengerAgentTabContent.Build, true),
        new(DressAgentTabContent.Title, AgentGroup.GearAndInventory, DressAgentTabContent.Build),
        new(ShoppingAssistantTabContent.Title, AgentGroup.Vendors, ShoppingAssistantTabContent.Build, true),
        new(TazLang.Get("assistant_agent_tab_healing", "Healing Assistant"), AgentGroup.HealingAndStats, HealingAssistantTabContent.Build),
        new(TazLang.Get("assistant_agent_tab_selfheal", "Self Heal"), AgentGroup.HealingAndStats, SelfHealTabContent.Build),
        new(TazLang.Get("assistant_agent_tab_organizer", "Organizer"), AgentGroup.GearAndInventory, OrganizerAgentTabContent.Build, true),
        new(TazLang.Get("assistant_agent_tab_statlock", "Stat Lock"), AgentGroup.HealingAndStats, _ => AutoStatLockAgentTabContent.Build()),
        new(TazLang.Get("assistant_agent_tab_support"), AgentGroup.Support, SupportAgentTabContent.Build, true)
    ];

    public static Widget Build()
    {
        var tabs = new MyraTabControl();
        foreach (AgentPage page in GetPages())
            tabs.AddTab(page.Name, () => page.Build(AgentPageInteraction.InGame), rebuildOnSelect: page.RebuildOnSelect);

        tabs.SelectFirst();
        return tabs;
    }
}
