#nullable enable

using System;
using ClassicUO.Game.Managers.Launcher;
using ClassicUO.Game.UI.MyraWindows.Theme;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Setup;
using ClassicUO.Utility;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Launcher;

/// <summary>
/// Launcher settings: the profile chosen in the Play bar, launcher options and updates.
/// </summary>
/// <remarks>Fields save when they lose focus. Adding, duplicating, deleting or browsing rebuilds the page.</remarks>
internal static class LauncherTabContent
{
    #region Private members

    private const int TEXT_INPUT_WIDTH = 260;
    private const int SHORT_INPUT_WIDTH = 90;

    #endregion

    internal static string Title => TazLang.Get("launcher_title");

    /// <summary>Builds the page for the profile currently selected in the Play bar.</summary>
    /// <param name="interaction">Page services (dialogs).</param>
    /// <returns>The page.</returns>
    internal static Widget Build(AgentPageInteraction interaction)
    {
        LauncherSave save = LauncherSave.Current;
        LauncherUpdates.CheckOnce();

        var root = new VerticalStackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        root.Widgets.Add(new MyraLabel(TazLang.Get("launcher_intro"), MyraLabel.TextStyle.P) { Wrap = true });

        LauncherProfile? profile = save.GetSelected();
        root.Widgets.Add(BuildProfileActions(save, profile, interaction));
        if (profile != null)
            root.Widgets.Add(BuildProfileFields(save, profile));

        root.Widgets.Add(new DesktopAssistantForm.SectionPair(BuildOptions(save), BuildUpdates(save)));
        return root;
    }

    #region Private methods

    private static Widget BuildProfileActions(LauncherSave save, LauncherProfile? profile, AgentPageInteraction interaction)
    {
        MyraButton add = DesktopAssistantTheme.ActionButton(TazLang.Get("launcher_new_profile"), () =>
        {
            // Start from the selected profile's game and shard: helpers usually share them.
            LauncherProfile created = profile?.Duplicate(TazLang.Get("launcher_new_profile_name")) ?? new LauncherProfile
            {
                Name = TazLang.Get("launcher_new_profile_name")
            };
            created.Account = "";
            created.EncryptedPassword = "";
            created.Character = "";
            save.Profiles.Add(created);
            save.Select(created);
        }, primary: true);

        MyraButton duplicate = DesktopAssistantTheme.ActionButton(TazLang.Get("launcher_duplicate"), () =>
        {
            if (profile == null)
                return;

            LauncherProfile copy = profile.Duplicate(TazLang.Get("launcher_copy_name", [profile.Name]));
            save.Profiles.Add(copy);
            save.Select(copy);
        });

        MyraButton delete = DesktopAssistantTheme.ActionButton(TazLang.Get("launcher_delete"));
        delete.Enabled = profile != null && save.Profiles.Count > 1;
        delete.Tooltip = TazLang.Get("launcher_delete_tooltip");
        delete.Click += (_, _) =>
        {
            if (profile == null || save.Profiles.Count <= 1)
                return;

            interaction.Confirm(delete, TazLang.Get("launcher_delete"), TazLang.Get("launcher_delete_confirm", [profile.Name]), () =>
            {
                save.Profiles.Remove(profile);
                save.Select(save.Profiles[0]);
            }, destructive: true);
        };

        return DesktopAssistantTheme.Group(TazLang.Get("launcher_profiles"), DesktopAssistantForm.Actions(add, duplicate, delete));
    }

    private static Widget BuildProfileFields(LauncherSave save, LauncherProfile profile)
    {
        var folderWarning = new MyraLabel("", MyraLabel.TextStyle.P) { Wrap = true, TextColor = DesktopAssistantTheme.SecondaryText };
        ShowFolderWarning(folderWarning, profile.UltimaOnlineDirectory);

        MyraInputBox folder = Input(profile.UltimaOnlineDirectory, value =>
        {
            profile.UltimaOnlineDirectory = value.Trim();
            ShowFolderWarning(folderWarning, profile.UltimaOnlineDirectory);
        }, save);
        MyraButton browse = DesktopAssistantTheme.ActionButton(TazLang.Get("launcher_browse"), () =>
        {
            if (NativeDialogs.PickFolder(profile.UltimaOnlineDirectory, out string? picked) != FolderPickResult.Picked || picked == null)
                return;

            profile.UltimaOnlineDirectory = picked;
            folder.Text = picked;
            ShowFolderWarning(folderWarning, picked);
            save.Save();
        });

        MyraInputBox port = Input(profile.Port.ToString(), value =>
        {
            if (ushort.TryParse(value, out ushort parsed))
                profile.Port = parsed;
        }, save, SHORT_INPUT_WIDTH);
        port.InputFilter = MyraInputBox.DigitInputFilter;

        MyraInputBox password = Input(Crypter.Decrypt(profile.EncryptedPassword), value =>
            profile.EncryptedPassword = Crypter.Encrypt(value), save);
        password.PasswordField = true;

        MyraInputBox name = Input(profile.Name, value =>
        {
            if (!string.IsNullOrWhiteSpace(value))
                profile.Name = value.Trim();
        }, save, notifyList: true);

        Widget fields = DesktopAssistantForm.Fields(
            new(TazLang.Get("launcher_field_name"), name),
            new(TazLang.Get("launcher_field_folder"), folder, browse),
            new(TazLang.Get("launcher_field_clientversion"), Input(profile.ClientVersion, value => profile.ClientVersion = value.Trim(), save, SHORT_INPUT_WIDTH)),
            new(TazLang.Get("launcher_field_shard"), Input(profile.ShardName, value => profile.ShardName = value.Trim(), save)),
            new(TazLang.Get("launcher_field_address"), Input(profile.Address, value => profile.Address = value.Trim(), save)),
            new(TazLang.Get("launcher_field_port"), port),
            new(TazLang.Get("launcher_field_account"), Input(profile.Account, value => profile.Account = value.Trim(), save)),
            new(TazLang.Get("launcher_field_password"), password),
            new(TazLang.Get("launcher_field_character"), Input(profile.Character, value => profile.Character = value.Trim(), save))
        );

        Widget playAll = MyraCheckButton.CreateWithCallback(profile.IncludeInPlayAll, value =>
        {
            profile.IncludeInPlayAll = value;
            save.Save();
        }, TazLang.Get("launcher_field_playall"));
        Widget reconnect = MyraCheckButton.CreateWithCallback(profile.Reconnect, value =>
        {
            profile.Reconnect = value;
            save.Save();
        }, TazLang.Get("assistant_reconnect"), TazLang.Get("autoreconnecttooltip"));

        return DesktopAssistantTheme.Group(
            TazLang.Get("launcher_profile_group", [profile.Name]),
            fields,
            folderWarning,
            new MyraLabel(TazLang.Get("launcher_character_hint"), MyraLabel.TextStyle.P)
            {
                Wrap = true, TextColor = DesktopAssistantTheme.SecondaryText
            },
            playAll,
            reconnect
        );
    }

    private static Widget BuildOptions(LauncherSave save) =>
        DesktopAssistantTheme.Group(
            TazLang.Get("launcher_options"),
            MyraCheckButton.CreateWithCallback(save.OpenOnStart, value =>
            {
                save.OpenOnStart = value;
                save.Save();
            }, TazLang.Get("launcher_openonstart"), TazLang.Get("launcher_openonstart_tooltip")),
            MyraCheckButton.CreateWithCallback(save.KeepOpenAfterPlay, value =>
            {
                save.KeepOpenAfterPlay = value;
                save.Save();
            }, TazLang.Get("launcher_keepopen"))
        );

    private static Widget BuildUpdates(LauncherSave save)
    {
        var status = new MyraLabel(LauncherUpdates.Status, MyraLabel.TextStyle.P) { Wrap = true };
        bool canInstall = LauncherUpdates.Available != null;
        MyraButton action = DesktopAssistantTheme.ActionButton(
            TazLang.Get(canInstall ? "launcher_update_install" : "launcher_update_check"),
            canInstall ? LauncherUpdates.Install : LauncherUpdates.Check,
            primary: canInstall
        );
        action.Enabled = !LauncherUpdates.IsBusy;

        var group = new VerticalStackPanel { Spacing = 6 };
        group.Widgets.Add(status);

        // Open clients keep running the old files until they're closed and played again.
        if (canInstall && save.Profiles.Exists(ClientLauncher.IsRunning))
            group.Widgets.Add(new MyraLabel(TazLang.Get("launcher_update_running"), MyraLabel.TextStyle.P)
            {
                Wrap = true, TextColor = DesktopAssistantTheme.SecondaryText
            });

        group.Widgets.Add(action);
        return DesktopAssistantTheme.Group(TazLang.Get("launcher_updates"), group);
    }

    /// <summary>A text box that commits on losing focus and saves; only name changes refresh the Play bar.</summary>
    private static MyraInputBox Input(
        string value,
        Action<string> commit,
        LauncherSave save,
        int width = TEXT_INPUT_WIDTH,
        bool notifyList = false
    )
    {
        var box = new MyraInputBox { Text = value, MinWidth = width };
        box.LostFocus = () =>
        {
            commit(box.Text ?? "");
            if (notifyList)
                save.SaveAndNotify();
            else
                save.Save();
        };
        return box;
    }

    private static void ShowFolderWarning(MyraLabel label, string folder)
    {
        bool valid = FirstRunSetup.IsUltimaOnlineFolder(folder);
        label.Text = valid ? "" : TazLang.Get("launcher_folder_invalid");
        label.Visible = !valid;
    }

    #endregion
}
