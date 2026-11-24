using System;
using ClassicUO.Configuration;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;

public static class AgentTab
{
    internal enum AgentGroup
    {
        HealingAndStats,
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
        new(TazLang.Get("assistant_agent_tab_autoloot", "Auto Loot"), AgentGroup.LootAndScavenge, AutoLootAgentTabContent.Build, true),
        new(TazLang.Get("assistant_agent_tab_scavenger", "Scavenger"), AgentGroup.LootAndScavenge, ScavengerAgentTabContent.Build, true),
        new(TazLang.Get("assistant_agent_tab_dress", "Dress Agent"), AgentGroup.GearAndInventory, DressAgentTabContent.Build),
        new(TazLang.Get("assistant_agent_tab_autobuy", "Auto Buy"), AgentGroup.Vendors, AutoBuyAgentTabContent.Build),
        new(TazLang.Get("assistant_agent_tab_autosell", "Auto Sell"), AgentGroup.Vendors, AutoSellAgentTabContent.Build),
        new(TazLang.Get("assistant_agent_tab_bandage", "Bandage"), AgentGroup.HealingAndStats, _ => BandageAgentTabContent.Build()),
        new(TazLang.Get("assistant_agent_tab_selfheal", "Self Heal"), AgentGroup.HealingAndStats, SelfHealTabContent.Build),
        new(TazLang.Get("assistant_agent_tab_organizer", "Organizer"), AgentGroup.GearAndInventory, OrganizerAgentTabContent.Build),
        new(TazLang.Get("assistant_agent_tab_statlock", "Stat Lock"), AgentGroup.HealingAndStats, _ => AutoStatLockAgentTabContent.Build())
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
