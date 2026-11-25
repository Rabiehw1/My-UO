#nullable enable

using System;
using System.Threading.Tasks;
using ClassicUO.Game.Managers.Updates;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers.Launcher;

/// <summary>
/// Update check and install for the launcher, which has no login screen to show the usual update window.
/// </summary>
/// <remarks>State is read and changed on the main thread; network work runs in the background.</remarks>
internal static class LauncherUpdates
{
    #region Public accessors

    /// <summary>Plain-language status for the launcher's Updates box.</summary>
    public static string Status { get; private set; } = "";

    /// <summary>The newer release found by the last check, if any.</summary>
    public static AvailableUpdate? Available { get; private set; }

    /// <summary>A check or install is running; buttons should wait.</summary>
    public static bool IsBusy { get; private set; }

    #endregion

    #region Private members

    private const int PERCENT = 100;

    private static bool _checkedOnce;

    #endregion

    #region Public methods

    /// <summary>Checks once per run, if automatic checks are on.</summary>
    public static void CheckOnce()
    {
        if (_checkedOnce)
            return;

        _checkedOnce = true;

        if (ClientUpdater.IsAutomaticCheckEnabled)
            Check();
        else
            SetStatus(TazLang.Get("launcher_update_current", [ClientUpdater.CurrentVersion.ToString()]));
    }

    /// <summary>Looks for a newer release in the background and reports it through <see cref="Status" />.</summary>
    public static void Check()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        SetStatus(TazLang.Get("launcher_update_checking"));

        Task.Run(async () =>
        {
            UpdateCheckResult result = await ClientUpdater.CheckAsync();

            MainThreadQueue.InvokeOnMainThread(() =>
            {
                IsBusy = false;
                Available = result.Status == UpdateCheckStatus.Available ? result.Update : null;
                SetStatus(result.Status switch
                {
                    UpdateCheckStatus.Available when result.Update != null =>
                        TazLang.Get("launcher_update_available", [result.Update.Version.ToString()]),
                    UpdateCheckStatus.UpToDate => TazLang.Get("launcher_update_current", [ClientUpdater.CurrentVersion.ToString()]),
                    UpdateCheckStatus.Unsupported => TazLang.Get("updatewindow_unsupported"),
                    _ => TazLang.Get("updatewindow_checkfailed")
                });
            });
        });
    }

    /// <summary>Downloads and installs <see cref="Available" />, then restarts as the launcher.</summary>
    public static void Install()
    {
        if (IsBusy || Available is not { } update)
            return;

        IsBusy = true;
        SetStatus(TazLang.Get("launcher_update_downloading", [0]));
        int shownPercent = 0;
        var progress = new Progress<float>(value =>
        {
            // Downloads report every few kilobytes; only redraw when the whole percent changes.
            int percent = (int)(value * PERCENT);
            if (percent == shownPercent)
                return;

            shownPercent = percent;
            MainThreadQueue.InvokeOnMainThread(() => SetStatus(TazLang.Get("launcher_update_downloading", [percent])));
        });

        Task.Run(async () =>
        {
            try
            {
                await ClientUpdater.InstallAsync(update, progress);
                MainThreadQueue.InvokeOnMainThread(ClientUpdater.RestartClient);
            }
            catch (Exception e)
            {
                Log.Error($"Launcher: update failed - {e}");
                MainThreadQueue.InvokeOnMainThread(() =>
                {
                    IsBusy = false;
                    SetStatus(TazLang.Get("launcher_update_failed"));
                });
            }
        });
    }

    #endregion

    #region Private methods

    private static void SetStatus(string text)
    {
        Status = text;
        LauncherSave.NotifyViewChanged();
    }

    #endregion
}
