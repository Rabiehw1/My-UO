#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ClassicUO.LegionScripting;

/// <summary>Runs the character's queued scripts on the game thread, advancing only after clean completion.</summary>
internal static class ScriptSequence
{
    internal const int StepTimeoutSeconds = 60;

    private static Queue<string> _pending = new();
    private static ScriptFile? _active;
    private static Thread? _thread;
    private static long _deadline;
    private static bool _completed;
    private static string? _step;
    private static bool _running;

    public static bool IsRunning => _running;
    public static string Status { get; private set; } = "Ready.";

    public static void Start(IEnumerable<string> scripts)
    {
        if (_running)
            return;

        _pending = new Queue<string>(scripts);
        if (_pending.Count == 0)
            return;

        _running = true;
        Advance();
    }

    public static void Update()
    {
        if (!_running || _active == null)
            return;

        if (_active.ScriptThread != _thread)
        {
            if (_completed && !_active.IsZombie)
                Advance();
            else
                End($"Sequence halted at {_step}.");
        }
        else if (!_completed && Time.Ticks >= _deadline)
        {
            var script = _active;
            End($"Timed out waiting for {_step}.");
            LegionScripting.StopScript(script);
        }
    }

    public static void Stop()
    {
        if (!_running)
            return;

        var script = _active;
        End("Sequence stopped.");
        if (script != null && script.ScriptThread != null)
            LegionScripting.StopScript(script);
    }

    public static void Reset() => End("Ready.");

    public static void ThreadFinished(ScriptFile script, Thread thread, bool completed)
    {
        if (_active != script || _thread != thread)
            return;

        if (completed)
            _completed = true;
        else
            End($"Sequence halted at {_step}. See the script error in game.");
    }

    public static void StopRequested(ScriptFile script)
    {
        if (script == _active)
            End($"Sequence halted at {_step}.");
    }

    private static void Advance()
    {
        if (!_pending.TryDequeue(out string? path))
        {
            End("Sequence complete.");
            return;
        }

        _step = path;
        _active = LegionScripting.LoadedScripts.FirstOrDefault(script => script.RelativePath == path);
        if (_active == null || !_active.FileExists())
        {
            End($"Script not found: {path}");
            return;
        }

        if (_active.ScriptThread != null || _active.IsZombie)
        {
            End($"Already running or stopping: {path}");
            return;
        }

        _completed = false;
        _deadline = Time.Ticks + StepTimeoutSeconds * 1000;
        LegionScripting.PlayScript(_active);
        _thread = _active.ScriptThread;
        if (_thread == null)
        {
            End($"Could not start: {path}");
            return;
        }

        Status = $"Running {path}";
    }

    private static void End(string status)
    {
        _running = false;
        _pending.Clear();
        _active = null;
        _thread = null;
        _step = null;
        _completed = false;
        Status = status;
    }
}
