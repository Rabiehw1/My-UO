#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.Updates;
using ClassicUO.Game.Scenes;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Myra.Graphics2D;
using Myra.Graphics2D.Brushes;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows;

/// <summary>
/// The update screen: presents a new release with its notes, then downloads, installs and restarts into it when the
/// player agrees. Open it through <see cref="ShowIfUpdateAvailable" /> (stays hidden unless there is an update) or
/// <see cref="CheckNow" /> (always opens and reports every outcome). At most one instance; game thread only.
/// </summary>
public sealed class UpdateWindow : MyraControl
{
    #region Private members

    private const int CONTENT_MAX_WIDTH = 520;
    private const int NOTES_MAX_HEIGHT = 300;
    private const int PROGRESS_BAR_HEIGHT = 16;
    private const int RESTART_DELAY_MS = 1200;

    private readonly VerticalStackPanel _content = new()
    {
        Spacing = 6,
        Padding = new Thickness(8),
        MaxWidth = CONTENT_MAX_WIDTH
    };

    private AvailableUpdate? _update;
    private CancellationTokenSource? _installCancellation;
    private MyraLabel? _progressLabel;
    private HorizontalProgressBar? _progressBar;
    private Widget? _cancelButtons;

    #endregion

    #region Ctor

    private UpdateWindow() : base(TazLang.Get("updatewindow_title", [ClientBranding.NAME]))
    {
        _rootWindow.Props.Minimizable = false;
        SetRootContent(_content);
    }

    #endregion

    #region Public methods

    /// <summary>
    /// Checks for an update in the background and opens the update screen only if one exists and the player is
    /// still on the login screen. Does nothing for local builds or when the player turned update checks off.
    /// </summary>
    public static void ShowIfUpdateAvailable()
    {
        if (!ClientUpdater.IsAutomaticCheckEnabled)
            return;

        Task.Run(async () =>
        {
            UpdateCheckResult result = await ClientUpdater.CheckAsync();

            if (result.Status != UpdateCheckStatus.Available || result.Update == null)
                return;

            MainThreadQueue.InvokeOnMainThread(() =>
            {
                // A fast or automatic login may have finished meanwhile; never interrupt play.
                if (Client.Game.Scene is LoginScene)
                    Open(window => window.ShowAvailable(result.Update));
            });
        });
    }

    /// <summary>Opens the update screen and checks for an update right away, reporting the result either way.</summary>
    public static void CheckNow() => Open(window => window.Check());

    #endregion

    #region Private methods

    /// <summary>Applies <paramref name="setState" /> to the open update screen, creating and centering it if needed.</summary>
    private static void Open(Action<UpdateWindow> setState)
    {
        foreach (IGui gump in UIManager.Gumps)
        {
            if (gump is UpdateWindow existing && !existing.IsDisposed && !existing.IsClosing)
            {
                // Never interrupt a download or install in progress.
                if (existing._installCancellation == null)
                    setState(existing);

                existing.BringOnTop();
                return;
            }
        }

        var window = new UpdateWindow();
        setState(window);
        UIManager.Add(window);
        window.CenterInViewPort();
    }

    private void Check()
    {
        ShowMessage(TazLang.Get("updatewindow_checking"));

        Task.Run(async () =>
        {
            UpdateCheckResult result = await ClientUpdater.CheckAsync();

            MainThreadQueue.InvokeOnMainThread(() =>
            {
                if (!IsDisposed)
                    ShowResult(result);
            });
        });
    }

    private void ShowResult(UpdateCheckResult result)
    {
        switch (result.Status)
        {
            case UpdateCheckStatus.Available when result.Update != null:
                ShowAvailable(result.Update);
                break;
            case UpdateCheckStatus.UpToDate:
                ShowMessage(TazLang.Get("updatewindow_uptodate", [ClientUpdater.CurrentVersion.ToString()]), showClose: true);
                break;
            case UpdateCheckStatus.Unsupported:
                ShowMessage(TazLang.Get("updatewindow_unsupported"), showClose: true);
                break;
            default:
                ShowMessage(TazLang.Get("updatewindow_checkfailed"), Check);
                break;
        }
    }

    private void ShowAvailable(AvailableUpdate update)
    {
        _update = update;
        Reset();

        AddLabel(TazLang.Get("updatewindow_available"), MyraLabel.TextStyle.H3);
        AddLabel(
            TazLang.Get("updatewindow_versions", [ClientUpdater.CurrentVersion.ToString(), update.Version.ToString()]),
            MyraLabel.TextStyle.P
        );

        if (!string.IsNullOrWhiteSpace(update.ReleaseNotes))
        {
            AddLabel(TazLang.Get("updatewindow_whatsnew"), MyraLabel.TextStyle.H5);
            _content.Widgets.Add(new ScrollViewer
            {
                MaxHeight = NOTES_MAX_HEIGHT,
                Content = new MyraLabel(update.ReleaseNotes.Trim(), MyraLabel.TextStyle.P)
            });
        }

        AddButtons(
            (TazLang.Get("updatewindow_later"), Close),
            (TazLang.Get("updatewindow_updatenow"), StartInstall)
        );
    }

    /// <summary>Shows <paramref name="text" /> with a Retry button when <paramref name="retry" /> is set.</summary>
    private void ShowMessage(string text, Action? retry = null, bool showClose = false)
    {
        Reset();
        AddLabel(text, MyraLabel.TextStyle.P);

        if (retry != null)
            AddButtons((TazLang.Get("updatewindow_close"), Close), (TazLang.Get("updatewindow_retry"), retry));
        else if (showClose)
            AddButtons((TazLang.Get("updatewindow_close"), Close));
    }

    private void StartInstall()
    {
        if (_update == null || _installCancellation != null)
            return;

        AvailableUpdate update = _update;
        Reset();
        SetClosable(false);

        _progressLabel = AddLabel(TazLang.Get("updatewindow_downloading", ["0"]), MyraLabel.TextStyle.P);
        _progressBar = new HorizontalProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            Height = PROGRESS_BAR_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Stretch,

            // Plain brushes so the bar doesn't depend on skin images the client's theme may not define.
            Background = new SolidBrush(new Color(0, 0, 0, 90)),
            Border = new SolidBrush(MyraStyle.GridBorderColor),
            BorderThickness = new Thickness(1),
            Filler = new SolidBrush(new Color(70, 160, 90))
        };
        _content.Widgets.Add(_progressBar);
        _cancelButtons = AddButtons((TazLang.Get("updatewindow_cancel"), CancelInstall));

        _installCancellation = new CancellationTokenSource();
        CancellationToken token = _installCancellation.Token;
        var progress = new MainThreadProgress(this);

        Task.Run(async () =>
        {
            Exception? error = null;

            try
            {
                await ClientUpdater.InstallAsync(update, progress, token);
            }
            catch (Exception e)
            {
                error = e;
            }

            MainThreadQueue.InvokeOnMainThread(() => OnInstallFinished(error));
        });
    }

    private void CancelInstall() => _installCancellation?.Cancel();

    private void OnProgress(float value)
    {
        if (_progressBar == null || _progressLabel == null)
            return;

        _progressBar.Value = value;

        if (value >= 1f)
        {
            // Files are being swapped now; cancelling is no longer possible.
            _progressLabel.Text = TazLang.Get("updatewindow_installing");

            if (_cancelButtons != null)
                _cancelButtons.Visible = false;
        }
        else
        {
            _progressLabel.Text = TazLang.Get("updatewindow_downloading", [((int)(value * 100)).ToString()]);
        }
    }

    private void OnInstallFinished(Exception? error)
    {
        bool cancelled = _installCancellation?.IsCancellationRequested ?? false;
        _installCancellation?.Dispose();
        _installCancellation = null;

        if (error == null)
        {
            ShowMessage(TazLang.Get("updatewindow_restarting"));
            Task.Delay(RESTART_DELAY_MS).ContinueWith(_ => MainThreadQueue.InvokeOnMainThread(Restart));
            return;
        }

        SetClosable(true);

        if (cancelled && error is OperationCanceledException && _update != null)
        {
            ShowAvailable(_update);
            return;
        }

        Log.Error($"Updater: update failed - {error}");
        ShowMessage(TazLang.Get("updatewindow_installfailed", [error.Message]), StartInstall);
    }

    private void Restart()
    {
        try
        {
            ClientUpdater.RestartClient();
        }
        catch (Exception e)
        {
            Log.Error($"Updater: could not restart - {e}");
            SetClosable(true);
            ShowMessage(TazLang.Get("updatewindow_restartmanually"), showClose: true);
        }
    }

    private void Reset()
    {
        _content.Widgets.Clear();
        _progressLabel = null;
        _progressBar = null;
        _cancelButtons = null;
    }

    private MyraLabel AddLabel(string text, MyraLabel.TextStyle style)
    {
        var label = new MyraLabel(text, style);
        _content.Widgets.Add(label);
        return label;
    }

    private Widget AddButtons(params (string Text, Action OnClick)[] buttons)
    {
        var row = new HorizontalStackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };

        foreach ((string text, Action onClick) in buttons)
            row.Widgets.Add(new MyraButton(text, onClick));

        _content.Widgets.Add(row);
        return row;
    }

    /// <summary>Stops the player closing the window mid-install, which would hide the progress and any error.</summary>
    private void SetClosable(bool closable)
    {
        _rootWindow.CloseButton.Visible = closable;
        CanCloseWithRightClick = closable;
    }

    private void Close() => _rootWindow.Close();

    #endregion

    /// <summary>Forwards download progress to the game thread, only when the whole percentage changes.</summary>
    private sealed class MainThreadProgress(UpdateWindow owner) : IProgress<float>
    {
        private int _lastPercent = -1;

        public void Report(float value)
        {
            int percent = (int)(value * 100);

            if (Interlocked.Exchange(ref _lastPercent, percent) == percent)
                return;

            MainThreadQueue.InvokeOnMainThread(() =>
            {
                if (!owner.IsDisposed)
                    owner.OnProgress(value);
            });
        }
    }
}
