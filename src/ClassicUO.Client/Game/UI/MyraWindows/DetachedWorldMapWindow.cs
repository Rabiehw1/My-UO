#nullable enable
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Renderer;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static SDL3.SDL;

namespace ClassicUO.Game.UI.MyraWindows;

internal sealed class DetachedWorldMapWindow
{
    private static volatile DetachedWorldMapWindow? _instance;

    private readonly nint _window;
    private readonly WorldMapGump _map;
    private readonly bool _ownsMap;
    private readonly bool _wasVisible;
    private readonly int _originalFacet;
    private readonly ConcurrentQueue<(SDL_Event Event, string? Text, bool Control)> _events = new();
    private readonly DetachedMapMarkers _markers;
    private RenderTarget2D? _frame;
    private Color[] _pixels = [];
    private bool _dragging;
    private bool? _cursorWasVisible;
    private Vector2? _center;
    private int? _browseFacet;
    private uint? _followSerial;
    private Point? _pointer;
    private bool _dragMoved;
    private bool _controlClick;
    private int _zoom;
    private float _displayScale = 1;
    private Vector2 _frameCenter;
    private Vector2 _renderedMapCenter;
    private int _renderedMapZoom;
    private int _renderedMapFacet;
    private ulong _nextFrame;

    private DetachedWorldMapWindow(nint window, WorldMapGump map, bool ownsMap)
    {
        _window = window;
        _map = map;
        _ownsMap = ownsMap;
        _wasVisible = map.IsVisible;
        _originalFacet = map.DesktopFacet;
        _zoom = map.ChangeDesktopZoom(map.DesktopZoomIndex, 0);
        _markers = new DetachedMapMarkers(map, this);
        map.IsDetached = true;
        map.IsVisible = false;
    }

    internal int SelectedFacet => _browseFacet ?? _map.MapWorld.MapIndex;
    internal uint? FollowSerial => _followSerial;

    internal void Follow(uint? serial)
    {
        _browseFacet = null;
        _followSerial = serial;
        _center = null;
        _nextFrame = 0;
    }

    internal void BrowseFacet(int facet)
    {
        if (facet == _map.MapWorld.MapIndex)
        {
            Follow(null);
            return;
        }

        _browseFacet = facet;
        _followSerial = null;
        Point center = _map.DesktopFacetCenter(facet);
        _center = new Vector2(center.X, center.Y);
        _nextFrame = 0;
    }

    internal void GoTo(Point tile)
    {
        _followSerial = null;
        _center = new Vector2(tile.X, tile.Y);
        _map.SetDesktopDestination(SelectedFacet, tile);
        _nextFrame = 0;
    }

    internal bool TryWalkTo(Point tile)
    {
        int facet = _map.MapWorld.MapIndex;
        if (SelectedFacet != facet)
            return false;

        Follow(null);
        _map.BeginFreshNavTo(facet, tile.X, tile.Y);
        return true;
    }

    private Vector2 ViewCenter()
    {
        if (_followSerial is uint serial && _map.DesktopPartyPosition(serial) is Point party)
            _center = new Vector2(party.X, party.Y);

        return _center ?? new Vector2(_map.MapWorld.Player.X, _map.MapWorld.Player.Y);
    }

    internal static bool IsOpenFor(World world) => _instance is { } mapWindow && mapWindow._map.MapWorld == world;

    internal static void Show() => MainThreadQueue.EnqueueAction(Open);

    internal static void ShowAddMarker() => MainThreadQueue.EnqueueAction(() =>
    {
        Open();
        if (_instance is { } mapWindow)
        {
            mapWindow._markers.AddAtPlayer();
            mapWindow._nextFrame = 0;
        }
    });

    internal static bool TryRaise()
    {
        if (_instance is not { } mapWindow)
            return false;

        SDL_RestoreWindow(mapWindow._window);
        SDL_RaiseWindow(mapWindow._window);
        return true;
    }

    private static void Open()
    {
        if (TryRaise() || World.Instance is not { InGame: true } world)
            return;

        nint window = SDL_CreateWindow("World Map · Ctrl+double-click to walk · Right-click to mark · Drag to pan · Wheel to zoom · Home to follow", 900, 650,
            SDL_WindowFlags.SDL_WINDOW_RESIZABLE | SDL_WindowFlags.SDL_WINDOW_HIDDEN);

        if (window == 0)
        {
            ReportFailure(world, SDL_GetError());
            return;
        }

        try
        {
            WorldMapGump? existing = UIManager.GetGump<WorldMapGump>();
            var map = existing ?? new WorldMapGump(world, desktopOnly: true);
            _instance = new DetachedWorldMapWindow(window, map, existing == null);
            SDL_SetWindowMinimumSize(window, 320, 240);
            if (!SDL_ShowWindow(window) || !SDL_StartTextInput(window))
                throw new InvalidOperationException(SDL_GetError());
        }
        catch (Exception ex)
        {
            if (!Close())
                SDL_DestroyWindow(window);

            ReportFailure(world, ex.ToString());
        }
    }

    internal static unsafe bool TryHandleEvent(ref SDL_Event e)
    {
        if (_instance is not { } mapWindow || SDL_GetWindowFromEvent(ref e) != mapWindow._window)
            return false;

        switch ((SDL_EventType)e.type)
        {
            case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
            case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
            case SDL_EventType.SDL_EVENT_WINDOW_EXPOSED:
            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
            case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_ENTER:
            case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE:
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
            case SDL_EventType.SDL_EVENT_KEY_DOWN:
            case SDL_EventType.SDL_EVENT_KEY_UP:
            case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                string? text = e.type == (uint)SDL_EventType.SDL_EVENT_TEXT_INPUT
                    ? Marshal.PtrToStringUTF8((nint)e.text.text) : null;
                bool control = (SDL_EventType)e.type is SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN
                    or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP
                    && (SDL_GetModState() & SDL_Keymod.SDL_KMOD_CTRL) != 0;
                mapWindow._events.Enqueue((e, text, control));
                break;
        }
        return true;
    }

    internal static void Update(GraphicsDevice device, UltimaBatcher2D batcher)
    {
        if (_instance is not { } mapWindow)
            return;

        if (World.Instance is not { InGame: true } world || mapWindow._map.MapWorld != world || mapWindow._map.IsDisposed)
        {
            Close(restoreMap: false);
            return;
        }

        try
        {
            mapWindow.RenderFrame(device, batcher);
        }
        catch (Exception ex)
        {
            Close();
            ReportFailure(world, ex.ToString());
        }
    }

    internal static bool Close(bool restoreMap = true)
    {
        if (_instance is not { } mapWindow)
            return false;

        _instance = null;
        mapWindow.RestoreCursor();
        mapWindow._frame?.Dispose();
        SDL_StopTextInput(mapWindow._window);
        SDL_DestroyWindow(mapWindow._window);
        if (mapWindow._ownsMap)
            mapWindow._map.Dispose();
        else if (!mapWindow._map.IsDisposed)
        {
            mapWindow._map.IsDetached = false;
            mapWindow._map.IsVisible = restoreMap && mapWindow._wasVisible;
            if (mapWindow._map.IsVisible && mapWindow._map.MapWorld.InGame)
            {
                int facet = mapWindow._map.FreeView ? mapWindow._originalFacet : mapWindow._map.MapWorld.MapIndex;
                mapWindow._map.UpdateDetachedMap(facet);
            }
        }

        return true;
    }

    private void RenderFrame(GraphicsDevice device, UltimaBatcher2D batcher)
    {
        for (int i = 0; i < 128 && _events.TryDequeue(out var item); i++)
        {
            if ((SDL_EventType)item.Event.type == SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED)
            {
                Close();
                return;
            }

            ApplyEvent(item.Event, item.Text, item.Control);
        }

        ulong now = SDL_GetTicks();
        if ((SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0 || now < _nextFrame
            || !SDL_GetWindowSize(_window, out int width, out int height) || width <= 0 || height <= 0)
            return;

        _nextFrame = now + 100;
        if (_map.DesktopFacet != SelectedFacet)
        {
            if (_browseFacet == null)
                _center = null;
            _dragging = false;
        }
        _map.UpdateDetachedMap(SelectedFacet);

        // Bound the readback size on large or high-DPI displays.
        _displayScale = Math.Min(1, Math.Min(1280f / width, 960f / height));
        int frameWidth = Math.Max(16, (int)(width * _displayScale));
        int frameHeight = Math.Max(16, (int)(height * _displayScale));
        if (_frame == null || _frame.Width != frameWidth || _frame.Height != frameHeight)
        {
            _frame?.Dispose();
            _frame = new RenderTarget2D(device, frameWidth, frameHeight, false, SurfaceFormat.Color,
                DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            _pixels = new Color[frameWidth * frameHeight];
        }

        _markers.Resize(frameWidth, frameHeight);
        _frameCenter = new Vector2(frameWidth / 2, frameHeight / 2);
        var center = ViewCenter();
        _renderedMapCenter = new Vector2((int)center.X, (int)center.Y);
        _renderedMapZoom = _zoom;
        _renderedMapFacet = _map.DesktopFacet;

        var targets = device.GetRenderTargets();
        var viewport = device.Viewport;
        var scissor = device.ScissorRectangle;
        try
        {
            device.SetRenderTarget(_frame);
            device.Clear(Color.Black);
            _map.SetDesktopPointer(_pointer is Point pointer && !_markers.Contains(pointer.X * _displayScale, pointer.Y * _displayScale)
                ? new Point((int)(pointer.X * _displayScale), (int)(pointer.Y * _displayScale)) : null);
            batcher.Begin();
            try
            {
                _map.DrawDetached(batcher, frameWidth, frameHeight, _renderedMapZoom, _renderedMapCenter);
            }
            finally
            {
                batcher.End();
            }

            _markers.Draw();
        }
        finally
        {
            device.SetRenderTargets(targets);
            device.Viewport = viewport;
            device.ScissorRectangle = scissor;
        }

        DesktopWindowSurface.Present(_window, _frame, _pixels);
    }

    private void ApplyEvent(SDL_Event e, string? text, bool control)
    {
        var type = (SDL_EventType)e.type;
        bool markerInput = type switch
        {
            SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN or SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP =>
                !_dragging && _markers.Contains(e.button.x * _displayScale, e.button.y * _displayScale),
            SDL_EventType.SDL_EVENT_MOUSE_WHEEL => _markers.Contains(e.wheel.mouse_x * _displayScale, e.wheel.mouse_y * _displayScale),
            SDL_EventType.SDL_EVENT_KEY_DOWN => _markers.IsOpen,
            _ => false
        };

        if (markerInput || type is SDL_EventType.SDL_EVENT_MOUSE_MOTION or SDL_EventType.SDL_EVENT_KEY_UP
            or SDL_EventType.SDL_EVENT_TEXT_INPUT or SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST)
            _markers.QueueInput(e, text, _displayScale);
        if (markerInput)
        {
            _nextFrame = 0;
            return;
        }

        switch (type)
        {
            case SDL_EventType.SDL_EVENT_WINDOW_RESIZED:
            case SDL_EventType.SDL_EVENT_WINDOW_EXPOSED:
                _nextFrame = 0;
                break;
            case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
            case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE:
                _dragging = false;
                _pointer = null;
                RestoreCursor();
                break;
            case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_ENTER:
                _cursorWasVisible ??= SDL_CursorVisible();
                SDL_ShowCursor();
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP when e.button.button == (byte)MouseButtonType.Right && _frame != null && !_dragging:
                if (TryGetTile(e.button.x, e.button.y, out Point location))
                {
                    _markers.AddAt(location);
                    _nextFrame = 0;
                }
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN when e.button.button == (byte)MouseButtonType.Left:
                _dragging = true;
                _dragMoved = false;
                _controlClick = control;
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP when e.button.button == (byte)MouseButtonType.Left:
                if (_dragging && !_dragMoved && _renderedMapFacet == _map.MapWorld.MapIndex
                    && TryGetTile(e.button.x, e.button.y, out Point tile))
                {
                    if (_controlClick && control && e.button.clicks == 2)
                        TryWalkTo(tile);
                    else if (!_controlClick && !control)
                        _map.TryTargetDesktopTile(tile);
                }
                _dragging = false;
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                _pointer = new Point((int)e.motion.x, (int)e.motion.y);
                if (_dragging && (e.motion.xrel != 0 || e.motion.yrel != 0))
                {
                    _dragMoved = true;
                    _followSerial = null;
                    _center = _map.PanDesktop(_center, _zoom, new Vector2(e.motion.xrel, e.motion.yrel) * _displayScale);
                }
                break;
            case SDL_EventType.SDL_EVENT_MOUSE_WHEEL:
                if (e.wheel.y != 0)
                {
                    int direction = e.wheel.direction == SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED ? -1 : 1;
                    _zoom = _map.ChangeDesktopZoom(_zoom, direction * Math.Sign(e.wheel.y));
                }
                break;
            case SDL_EventType.SDL_EVENT_KEY_DOWN when e.key.key == (uint)SDL_Keycode.SDLK_HOME:
                Follow(null);
                break;
        }
    }

    private bool TryGetTile(float x, float y, out Point tile)
    {
        tile = default;
        if (_frame == null || _renderedMapFacet != SelectedFacet || _map.DesktopFacet != _renderedMapFacet)
            return false;

        var offset = new Vector2(x, y) * _displayScale - _frameCenter;
        return _map.TryGetDesktopPosition(_renderedMapCenter, _renderedMapZoom, offset, out tile);
    }

    private void RestoreCursor()
    {
        if (_cursorWasVisible == false)
            SDL_HideCursor();
        _cursorWasVisible = null;
    }

    private static void ReportFailure(World world, string message)
    {
        Log.Error($"Detached World Map: {message}");
        GameActions.Print(world, "The detached World Map could not be displayed.", Constants.HUE_WARN);
    }
}
