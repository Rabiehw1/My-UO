#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ClassicUO.Utility.Logging;
using SDL3;

namespace ClassicUO.Setup;

/// <summary>Outcome of <see cref="NativeDialogs.PickFolder" />.</summary>
internal enum FolderPickResult
{
    Picked,
    Cancelled,

    /// <summary>The operating system could not show a folder picker (e.g. no desktop portal on Linux).</summary>
    Unavailable
}

/// <summary>
/// Operating-system message boxes and folder picker, usable before the game window exists. Every method blocks
/// until the player answers, so call them from the main thread before the game loop starts.
/// </summary>
internal static class NativeDialogs
{
    private const int FOLDER_POLL_MS = 15;

    /// <summary>
    /// Kept in a field while a folder dialog is open: SDL calls it from native code, and a delegate that only a
    /// local references could be garbage collected first.
    /// </summary>
    private static SDL.SDL_DialogFileCallback? _folderCallback;

    /// <summary>Shows a message box and returns the index of the button pressed.</summary>
    /// <param name="title">Window title.</param>
    /// <param name="message">Body text; keep it short, native boxes cannot scroll.</param>
    /// <param name="kind">Icon to show.</param>
    /// <param name="buttons">Button labels, left to right. The first is chosen by Enter, the last by Escape.</param>
    /// <returns>Index into <paramref name="buttons" />, or the last index if the box could not be shown.</returns>
    public static unsafe int ShowChoice(string title, string message, SDL.SDL_MessageBoxFlags kind, params string[] buttons)
    {
        if (buttons.Length == 0)
            throw new ArgumentException("At least one button is required.", nameof(buttons));

        int escapeIndex = buttons.Length - 1;
        var encodedTexts = new GCHandle[buttons.Length];
        var buttonData = new SDL.SDL_MessageBoxButtonData[buttons.Length];
        byte[] titleUtf8 = Utf8(title);
        byte[] messageUtf8 = Utf8(message);

        try
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                encodedTexts[i] = GCHandle.Alloc(Utf8(buttons[i]), GCHandleType.Pinned);

                SDL.SDL_MessageBoxButtonFlags flags = 0;

                if (i == 0)
                    flags |= SDL.SDL_MessageBoxButtonFlags.SDL_MESSAGEBOX_BUTTON_RETURNKEY_DEFAULT;

                if (i == escapeIndex)
                    flags |= SDL.SDL_MessageBoxButtonFlags.SDL_MESSAGEBOX_BUTTON_ESCAPEKEY_DEFAULT;

                buttonData[i] = new SDL.SDL_MessageBoxButtonData
                {
                    flags = flags,
                    buttonID = i,
                    text = (byte*)encodedTexts[i].AddrOfPinnedObject()
                };
            }

            fixed (byte* titlePtr = titleUtf8)
            fixed (byte* messagePtr = messageUtf8)
            fixed (SDL.SDL_MessageBoxButtonData* buttonsPtr = buttonData)
            {
                var data = new SDL.SDL_MessageBoxData
                {
                    flags = kind | SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_BUTTONS_LEFT_TO_RIGHT,
                    window = IntPtr.Zero,
                    title = titlePtr,
                    message = messagePtr,
                    numbuttons = buttons.Length,
                    buttons = buttonsPtr,
                    colorScheme = null
                };

                if (SDL.SDL_ShowMessageBox(ref data, out int pressed) && pressed >= 0 && pressed < buttons.Length)
                    return pressed;

                Log.Warn($"Setup: message box could not be shown - {SDL.SDL_GetError()}");
                return escapeIndex;
            }
        }
        finally
        {
            foreach (GCHandle handle in encodedTexts)
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
        }
    }

    /// <summary>Opens the operating system's folder picker and waits for the player to choose or cancel.</summary>
    /// <param name="startFolder">Folder the picker opens in, or null for the system default.</param>
    /// <param name="folder">The chosen folder when the result is <see cref="FolderPickResult.Picked" />.</param>
    /// <returns>Whether a folder was picked, the picker was cancelled, or no picker could be shown.</returns>
    public static FolderPickResult PickFolder(string? startFolder, out string? folder)
    {
        folder = null;

        // The dialog delivers its answer through SDL's event loop, which needs the video subsystem. SDL reference
        // counts subsystems, so this balanced init/quit leaves the game's own SDL setup untouched.
        bool videoStarted = SDL.SDL_InitSubSystem(SDL.SDL_InitFlags.SDL_INIT_VIDEO);

        if (!videoStarted)
            Log.Warn($"Setup: could not start SDL video for the folder picker - {SDL.SDL_GetError()}");

        using var answered = new ManualResetEventSlim();
        string? picked = null;
        bool failed = false;

        _folderCallback = (_, fileList, _) =>
        {
            if (fileList == IntPtr.Zero)
            {
                failed = true;
            }
            else
            {
                // A null-terminated array of UTF-8 paths; an empty array means the player cancelled.
                IntPtr first = Marshal.ReadIntPtr(fileList);

                if (first != IntPtr.Zero)
                    picked = Marshal.PtrToStringUTF8(first);
            }

            answered.Set();
        };

        try
        {
            SDL.SDL_ShowOpenFolderDialog(_folderCallback, IntPtr.Zero, IntPtr.Zero, startFolder, false);

            while (!answered.Wait(FOLDER_POLL_MS))
                SDL.SDL_PumpEvents();
        }
        finally
        {
            _folderCallback = null;

            if (videoStarted)
                SDL.SDL_QuitSubSystem(SDL.SDL_InitFlags.SDL_INIT_VIDEO);
        }

        if (failed)
        {
            Log.Warn($"Setup: folder picker unavailable - {SDL.SDL_GetError()}");
            return FolderPickResult.Unavailable;
        }

        folder = picked;
        return picked == null ? FolderPickResult.Cancelled : FolderPickResult.Picked;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text + '\0');
}
