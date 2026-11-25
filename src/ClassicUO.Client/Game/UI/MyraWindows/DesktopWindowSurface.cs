using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static SDL3.SDL;

namespace ClassicUO.Game.UI.MyraWindows;

internal static class DesktopWindowSurface
{
    internal static unsafe void Present(nint window, RenderTarget2D frame, Color[] buffer)
    {
        nint surface = GetWindowSurface(window);
        if (surface == 0)
            throw new InvalidOperationException(SDL_GetError());

        frame.GetData(buffer);
        fixed (Color* pixels = buffer)
        {
            nint source = SDL_CreateSurfaceFrom(frame.Width, frame.Height, SDL_PixelFormat.SDL_PIXELFORMAT_RGBA32,
                (nint)pixels, frame.Width * sizeof(Color));
            if (source == 0)
                throw new InvalidOperationException(SDL_GetError());

            try
            {
                // These pixels already contain the final opaque desktop.
                SDL_SetSurfaceBlendMode(source, 0);
                if (!SDL_BlitSurfaceScaled(source, 0, surface, 0, SDL_ScaleMode.SDL_SCALEMODE_LINEAR)
                    || !SDL_UpdateWindowSurface(window))
                    throw new InvalidOperationException(SDL_GetError());
            }
            finally
            {
                SDL_DestroySurface(source);
            }
        }
    }

    private static nint GetWindowSurface(nint window)
    {
        // Avoid creating an SDL GL renderer that could take over the game's context.
        string? originalHint = SDL_GetHint(SDL_HINT_FRAMEBUFFER_ACCELERATION);
        SDL_SetHint(SDL_HINT_FRAMEBUFFER_ACCELERATION, "0");

        try
        {
            return SDL_GetWindowSurface(window);
        }
        finally
        {
            if (originalHint == null)
                SDL_ResetHint(SDL_HINT_FRAMEBUFFER_ACCELERATION);
            else
                SDL_SetHint(SDL_HINT_FRAMEBUFFER_ACCELERATION, originalHint);
        }
    }
}
