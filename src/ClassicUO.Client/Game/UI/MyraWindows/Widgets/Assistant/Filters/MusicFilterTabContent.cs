using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Filters;

public static class MusicFilterTabContent
{
    public static Widget Build() => SoundFilterTabContent.Build(AgentPageInteraction.InGame, isMusic: true);
}
