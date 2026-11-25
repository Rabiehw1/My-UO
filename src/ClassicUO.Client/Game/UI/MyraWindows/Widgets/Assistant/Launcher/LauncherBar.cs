#nullable enable

using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.Launcher;
using ClassicUO.Game.UI.MyraWindows.Theme;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Launcher;

/// <summary>
/// The Play bar across the top of the Assistant: pick a profile, Play it, or Play all.
/// </summary>
/// <remarks>Owned by the Assistant window, which calls <see cref="Rebuild" /> and <see cref="RefreshRunning" />.</remarks>
internal sealed class LauncherBar : MyraGrid
{
    #region Private members

    private const string RUNNING_MARK = "● ";
    private const string IDLE_MARK = "○ ";
    private const int PROFILE_COMBO_WIDTH = 200;

    private static readonly Color _runningColor = new(37, 111, 69);

    private readonly List<(LauncherProfile Profile, Label Item)> _items = [];
    private readonly MyraLabel _running = new("", MyraLabel.TextStyle.P) { VerticalAlignment = VerticalAlignment.Center };
    private readonly MyraLabel _message = new("", MyraLabel.TextStyle.P)
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextColor = DesktopAssistantTheme.SecondaryText
    };

    #endregion

    public LauncherBar()
    {
        Padding = new Thickness(8, 5);
        ColumnSpacing = 8;
        Background = DesktopAssistantTheme.Sidebar;
        Border = DesktopAssistantTheme.Divider;
        BorderThickness = new Thickness(0, 0, 0, 1);
        Rebuild();
    }

    #region Public methods

    /// <summary>Rebuilds the profile list and buttons from <see cref="LauncherSave.Current" />.</summary>
    public void Rebuild()
    {
        Widgets.Clear();
        ColumnsProportions.Clear();
        _items.Clear();

        LauncherSave save = LauncherSave.Current;
        LauncherProfile? selected = save.GetSelected();

        AddColumn();
        AddColumn();
        AddColumn(new Proportion(ProportionType.Fill));
        AddColumn();
        AddColumn();
        AddColumn();

        AddWidget(new MyraLabel(TazLang.Get("launcher_profile"), MyraLabel.TextStyle.P) { VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        AddWidget(BuildProfileCombo(save, selected), 0, 1);
        AddWidget(_message, 0, 2);
        AddWidget(_running, 0, 3);

        MyraButton play = DesktopAssistantTheme.ActionButton(TazLang.Get("launcher_play"), () => Play(selected), primary: true);
        play.Tooltip = TazLang.Get("launcher_play_tooltip");
        AddWidget(play, 0, 4);

        MyraButton playAll = DesktopAssistantTheme.ActionButton(TazLang.Get("launcher_playall"), PlayAll);
        playAll.Tooltip = TazLang.Get("launcher_playall_tooltip");
        AddWidget(playAll, 0, 5);

        RefreshRunning();
    }

    /// <summary>Updates which profiles show as running; cheap enough to call every couple of seconds.</summary>
    public void RefreshRunning()
    {
        int running = 0;

        foreach ((LauncherProfile profile, Label item) in _items)
        {
            bool isRunning = ClientLauncher.IsRunning(profile);
            item.Text = (isRunning ? RUNNING_MARK : IDLE_MARK) + profile.Name;
            if (isRunning)
                running++;
        }

        _running.Text = TazLang.Get("launcher_running_count", [running.ToString(), _items.Count.ToString()]);
        _running.TextColor = running > 0 ? _runningColor : DesktopAssistantTheme.SecondaryText;
    }

    #endregion

    #region Private methods

    private ComboView BuildProfileCombo(LauncherSave save, LauncherProfile? selected)
    {
        var combo = new ComboView { MinWidth = PROFILE_COMBO_WIDTH };

        foreach (LauncherProfile profile in save.Profiles)
        {
            // ComboView clones its selected item, so use Myra's cloneable Label.
            var item = new Label { Text = profile.Name };
            _items.Add((profile, item));
            combo.ListView.Widgets.Add(item);
        }

        combo.ListView.SelectedIndex = selected == null ? null : save.Profiles.IndexOf(selected);
        combo.ListView.SelectedIndexChanged += (_, _) =>
        {
            if (combo.ListView.SelectedIndex is int index && index < save.Profiles.Count)
                save.Select(save.Profiles[index]);
        };

        return combo;
    }

    private void Play(LauncherProfile? profile)
    {
        if (profile == null)
            return;

        if (ClientLauncher.IsRunning(profile))
        {
            _message.Text = TazLang.Get("launcher_already_running", [profile.Name]);
            return;
        }

        string? error = ClientLauncher.Play(profile);
        _message.Text = error ?? TazLang.Get("launcher_started", [profile.Name]);

        if (error == null)
            CloseLauncherIfDone();
    }

    private void PlayAll()
    {
        int started = 0;
        string? lastError = null;

        foreach (LauncherProfile profile in LauncherSave.Current.Profiles)
        {
            if (!profile.IncludeInPlayAll || ClientLauncher.IsRunning(profile))
                continue;

            string? error = ClientLauncher.Play(profile);
            if (error == null)
                started++;
            else
                lastError = error;
        }

        _message.Text = lastError ?? TazLang.Get("launcher_started_count", [started.ToString()]);

        if (started > 0)
            CloseLauncherIfDone();
    }

    private static void CloseLauncherIfDone()
    {
        if (ClientLauncher.IsLauncherOnly && !LauncherSave.Current.KeepOpenAfterPlay)
            MainThreadQueue.EnqueueAction(() => Client.Game.Exit());
    }

    #endregion
}
