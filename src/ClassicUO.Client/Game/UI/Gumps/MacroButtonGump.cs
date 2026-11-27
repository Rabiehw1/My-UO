using System;
using System.Linq;
using System.Xml;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.MyraWindows;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Gumps
{
    /// <summary>
    ///     A macro's standalone on-screen button, drawn with one of two appearances depending on whether
    ///     the macro is running.
    /// </summary>
    /// <remarks>
    ///     The macro's appearance is cached when it is handed over rather than read per frame, so an edit
    ///     reaches this button only by assigning <see cref="TheMacro" /> again. Only the run state itself
    ///     is polled, since nothing signals a macro starting or stopping.
    /// </remarks>
    public sealed class MacroButtonGump : AnchorableGump
    {
        private Texture2D _backgroundTexture;
        private Vector3 _hueVector;
        private int? _graphic;

        /// <summary>
        ///     Graphic drawn while the macro is running, already resolved through
        ///     <see cref="Macro.GraphicFor" /> so it equals <see cref="_graphic" /> when the macro has no
        ///     separate active graphic.
        /// </summary>
        /// <remarks>
        ///     Kept out of the <see cref="Graphic" /> setter deliberately: that setter resizes the gump and
        ///     its anchor-group cells, which must not happen per frame. The button therefore keeps the
        ///     resting graphic's size and stretches the active one into it, so switching state never moves
        ///     the button or reflows the group it is anchored in.
        /// </remarks>
        private int? _activeGraphic;
        private bool? _editorPreviewActive;

        /// <summary>Draw tint for the running state, resolved through <see cref="Macro.HueFor" />.</summary>
        private Vector3 _activeHueVector;

        private Macro _macr;
        internal const int MinButtonScale = 10;
        internal const int MaxButtonScale = 200;
        private readonly ResizeGrip _resizeGrip;
        private const int DEFAULT_WIDTH = 88;
        private const int DEFAULT_HEIGHT = 44;
        private RenderedText _gText;

        /// <summary>The label drawn while the macro is running, which may differ in text from <see cref="_gText" />.</summary>
        private RenderedText _gTextActive;

        /// <summary>The backing plate at rest. Hued at draw time.</summary>
        public static readonly Color PlateColor = new(30, 30, 30);

        /// <summary>The backing plate under the pointer, which is the button's whole hover response.</summary>
        public static readonly Color PlateHoverColor = Color.DimGray;

        /// <summary>Label hue and opacity per run state, resolved from the macro when it is handed over.</summary>
        private ushort _labelHue = Macro.DEFAULT_LABEL_HUE;

        private ushort _activeLabelHue = Macro.DEFAULT_LABEL_HUE;
        private float _labelOpacity = 1f;
        private float _activeLabelOpacity = 1f;

        public MacroButtonGump(World world, Macro macro, int x, int y) : this(world)
        {
            X = x;
            Y = y;
            Width = DEFAULT_WIDTH;
            Height = DEFAULT_HEIGHT;
            TheMacro = macro;
        }

        public MacroButtonGump(World world) : base(world,0, 0)
        {
            _backgroundTexture = SolidColorTextureCache.GetTexture(PlateColor);
            CanMove = true;
            AcceptMouseInput = true;
            CanCloseWithRightClick = true;
            WantUpdateSize = false;
            WidthMultiplier = 2;
            HeightMultiplier = 1;
            GroupMatrixWidth = 44;
            GroupMatrixHeight = 44;
            AnchorType = ANCHOR_TYPE.SPELL;
            Add(_resizeGrip = new ResizeGrip(this));
        }

        public override GumpType GumpType => GumpType.MacroButton;

        internal void RefreshAppearanceFromEditor(bool? previewActive)
        {
            _editorPreviewActive = previewActive;
            TheMacro = _macr;
        }

        public Macro TheMacro
        {
            get => _macr;
            set
            {
                _macr = value;

                // A button whose macro was deleted keeps its size and plate and simply draws no label.
                // Save() already declines to persist one, so it lasts the session and no longer.
                if (value == null)
                {
                    DestroyLabels();
                    return;
                }

                Scale = value.Scale;
                Graphic = value.Graphic;
                Hue = value.Hue;
                _labelHue = value.LabelHueFor(false);
                _activeLabelHue = value.LabelHueFor(true);
                _labelOpacity = value.LabelOpacityFor(false) / (float)Macro.FULL_OPACITY;
                _activeLabelOpacity = value.LabelOpacityFor(true) / (float)Macro.FULL_OPACITY;
                _activeGraphic = value.GraphicFor(true);
                _activeHueVector = ShaderHueTranslator.GetHueVector(value.HueFor(true));

                // Last, because the renderings bake the label text and the width it wraps at, both of
                // which the assignments above settle. Re-handing the macro is how an edit reaches a
                // button already on screen, so the labels have to be rebuilt here, not only at build.
                RebuildLabels();
                _resizeGrip.PositionInCorner();
            }
        }

        /// <summary>
        ///     Head of the macro's action chain, which is what <see cref="MacroManager.IsActive" />
        ///     identifies a run by.
        /// </summary>
        /// <remarks>Read live rather than cached: editing the macro's actions can replace the head node.</remarks>
        private MacroObject ActionHead => _macr?.Items as MacroObject;
        public bool IsPartialHue { get; set; }

        public ushort Hue
        {
            get;
            set
            {
                field = value;
                _hueVector = ShaderHueTranslator.GetHueVector(value);
            }
        }

        public new float Scale
        {
            get;
            set
            {
                field = value;

                float factor = value / 100F;

                Width = (int)(Width * factor);
                Height = (int)(Height * factor);
                GroupMatrixHeight = Height;
                GroupMatrixWidth = Width;
                WidthMultiplier = 1;
            }
        }

        public int? Graphic
        {
            get => _graphic;
            set
            {
                _graphic = value;
                float factor = Scale / 100F;
                var bounds = new Rectangle(0, 0, DEFAULT_WIDTH, DEFAULT_HEIGHT);
                IsPartialHue = false;

                if (value.HasValue)
                {
                    SpriteInfo texture = MacroButtonArt.GetSprite(value.Value);
                    if (texture.Texture != null)
                    {
                        Point size = MacroButtonArt.DisplaySize(value.Value, texture);
                        bounds = new Rectangle(0, 0, size.X, size.Y);
                        IsPartialHue = MacroButtonArt.IsPartialHue(value.Value);
                    }
                }

                Width = (int)(bounds.Width * factor);
                Height = (int)(bounds.Height * factor);

                GroupMatrixHeight = Height;
                GroupMatrixWidth = Width;
                WidthMultiplier = 1;
            }
        }

        /// <summary>
        ///     Renders both labels, one per run state.
        /// </summary>
        /// <remarks>
        ///     Reached on every macro assignment, so the previous renderings are returned to the pool
        ///     first; they are pooled objects, and dropping them leaks one per rebuild.
        /// </remarks>
        private void RebuildLabels()
        {
            DestroyLabels();

            if (!MacroButtonArt.IncludesCaption(Graphic))
                _gText = CreateLabel(TheMacro.LabelFor(false), _labelHue);
            if (!MacroButtonArt.IncludesCaption(_activeGraphic))
                _gTextActive = CreateLabel(TheMacro.LabelFor(true), _activeLabelHue);
        }

        /// <summary>Renders one of the button's labels.</summary>
        /// <param name="text">The text to render. Empty renders nothing, which is how a hidden label is drawn.</param>
        /// <param name="hue">
        ///     The label's hue, baked into the rendering. A unicode <see cref="RenderedText" /> colours
        ///     its glyphs as it generates them and ignores its <c>Hue</c> afterwards, so changing the
        ///     hue means rebuilding, not assigning.
        /// </param>
        /// <returns>The rendering.</returns>
        private RenderedText CreateLabel(string text, ushort hue) => RenderedText.Create
        (
            text ?? string.Empty,
            hue,
            255,
            true,
            FontStyle.BlackBorder,
            TEXT_ALIGN_TYPE.TS_CENTER,
            Width
        );

        /// <inheritdoc />
        /// <remarks>Hands the pooled label renderings back; nothing else reclaims them.</remarks>
        public override void Dispose()
        {
            DestroyLabels();
            base.Dispose();
        }

        /// <summary>Returns both label renderings to the pool. Idempotent.</summary>
        private void DestroyLabels()
        {
            _gText?.Destroy();
            _gTextActive?.Destroy();

            _gText = null;
            _gTextActive = null;
        }

        protected override void OnMouseEnter(int x, int y)
        {
            _backgroundTexture = SolidColorTextureCache.GetTexture(PlateHoverColor);
            base.OnMouseEnter(x, y);
        }

        protected override void OnMouseExit(int x, int y)
        {
            _backgroundTexture = SolidColorTextureCache.GetTexture(PlateColor);
            base.OnMouseExit(x, y);
        }


        public override void OnMouseUp(int x, int y, MouseButtonType button)
        {
            base.OnMouseUp(x, y, MouseButtonType.Left);

            Point offset = Mouse.LDragOffset;

            if (ProfileManager.GlobalSettings.SingleClickIconUse && button == MouseButtonType.Left && !Keyboard.Alt && Math.Abs(offset.X) < 5 && Math.Abs(offset.Y) < 5)
            {
                RunMacro();
            }
        }

        public override bool OnMouseDoubleClick(int x, int y, MouseButtonType button)
        {
            if (ProfileManager.GlobalSettings.SingleClickIconUse || button != MouseButtonType.Left)
            {
                return false;
            }

            RunMacro();

            return true;
        }

        private sealed class ResizeGrip : Control
        {
            private readonly MacroButtonGump _owner;
            private Point _start;
            private int _startWidth, _startHeight;
            private float _startScale;
            private bool _dragging;

            internal ResizeGrip(MacroButtonGump owner)
            {
                _owner = owner;
                CanMove = false;
                SetTooltip(TazLang.Get("macrobutton_resize_grip"));
            }

            private bool CanResize => _owner.TheMacro != null &&
                UIManager.AnchorManager[_owner] == null &&
                UIManager.GetGump<MacroButtonEditorWindow>() == null;

            public override bool AcceptMouseInput { get => CanResize; set { } }

            internal void PositionInCorner()
            {
                Width = Math.Max(1, Math.Min(12, _owner.Width / 3));
                Height = Math.Max(1, Math.Min(12, _owner.Height));
                X = _owner.Width - Width;
                Y = _owner.Height - Height;
            }

            public override void OnMouseDown(int x, int y, MouseButtonType button)
            {
                if (button != MouseButtonType.Left || !CanResize)
                    return;

                _dragging = true;
                _start = Mouse.Position;
                _startWidth = Math.Max(1, _owner.Width);
                _startHeight = Math.Max(1, _owner.Height);
                _startScale = _owner.Scale;
            }

            public override void Update()
            {
                base.Update();
                if (_dragging)
                {
                    if (Mouse.LButtonPressed)
                        Resize();
                    else
                        Finish();
                }
            }

            public override void OnMouseUp(int x, int y, MouseButtonType button)
            {
                if (button != MouseButtonType.Left || !_dragging)
                    return;

                if (CanResize)
                    Resize();
                Finish();
            }

            private void Resize()
            {
                var delta = Mouse.Position - _start;
                float change = ((float)delta.X * _startWidth + (float)delta.Y * _startHeight) /
                    ((float)_startWidth * _startWidth + (float)_startHeight * _startHeight);
                float scale = Math.Clamp(MathF.Round(_startScale * (1 + change)), MinButtonScale, MaxButtonScale);
                if (_owner.Scale == scale)
                    return;

                _owner.Scale = scale;
                _owner.Graphic = _owner.Graphic;
                _owner.RebuildLabels();
                PositionInCorner();
            }

            private void Finish()
            {
                _dragging = false;
                if (!CanResize)
                {
                    _owner.RefreshAppearanceFromEditor(null);
                    return;
                }

                if (_owner.Scale == _startScale)
                    return;

                Macro macro = _owner.TheMacro;
                macro.Scale = (byte)_owner.Scale;
                foreach (var button in UIManager.Gumps.OfType<MacroButtonGump>())
                    if (!button.IsDisposed && !ReferenceEquals(button, _owner) && ReferenceEquals(button.TheMacro, macro))
                        button.RefreshAppearanceFromEditor(null);

                _owner.World.Macros.Save();
            }

            public override bool Draw(UltimaBatcher2D batcher, int x, int y)
            {
                if (CanResize && (_dragging || UIManager.MouseOverControl?.RootParent == _owner))
                {
                    var texture = SolidColorTextureCache.GetTexture(Color.White);
                    var hue = ShaderHueTranslator.GetHueVector(0, false, 0.65f);
                    int dots = Math.Min(Width, Height) / 3;
                    for (int i = 0; i < dots; i++)
                        batcher.Draw(texture, new Rectangle(x + Width - 3 - i * 3, y + Height - 3 - i * 3, 2, 2), hue);
                }

                return true;
            }
        }

        private void RunMacro()
        {
            if (TheMacro != null)
            {
                World.Macros.SetMacroToExecute(TheMacro.Items as MacroObject);
                World.Macros.WaitForTargetTimer = 0;
                World.Macros.Update();
            }
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (!IsVisible) return false;

            // Resolved per frame rather than cached on a state change: nothing signals the macro
            // starting or stopping, and the test is two reference compares.
            bool isActive = _editorPreviewActive ?? World.Macros.IsActive(ActionHead);
            int? graphic = isActive ? _activeGraphic : Graphic;
            Vector3 stateHueVector = isActive ? _activeHueVector : _hueVector;

            batcher.Draw
            (
                _backgroundTexture,
                new Rectangle
                (
                    x,
                    y,
                    Width,
                    Height
                ),
                stateHueVector
            );

            if (graphic.HasValue)
            {
                SpriteInfo texture = MacroButtonArt.GetSprite(graphic.Value);
                if (texture.Texture != null)
                {
                    var rect = new Rectangle(x, y, Width, Height);
                    batcher.Draw
                    (
                        texture.Texture,
                        rect,
                        texture.UV,
                        MacroButtonArt.IncludesCaption(graphic) ? ShaderHueTranslator.GetHueVector(0) : stateHueVector
                    );
                }
            }
            else
            {
                batcher.DrawRectangle
                    (
                        SolidColorTextureCache.GetTexture(Color.Gray),
                        x,
                        y,
                        Width,
                        Height,
                        stateHueVector
                    );
            }

            // An empty label renders nothing, so "hidden" needs no branch of its own here.
            RenderedText label = isActive ? _gTextActive : _gText;

            if (label != null)
            {
                // Hue is already in the rendering; see CreateLabel. Hover is answered by the plate
                // alone, since recolouring the label under the pointer would hide the hue that is set.

                // Multiplied, not replaced: the gump's own alpha is the whole button fading.
                float labelAlpha = Alpha * (isActive ? _activeLabelOpacity : _labelOpacity);

                label.Draw(batcher, x, y + ((Height >> 1) - (label.Height >> 1)), labelAlpha);
            }


            base.Draw(batcher, x, y);

            return true;
        }

        public override void Save(XmlTextWriter writer)
        {
            if (TheMacro == null)
                return;

            // hack to give macro buttons a unique id for use in anchor groups
            int macroId = World.Macros.GetAllMacros().IndexOf(TheMacro);
            LocalSerial = (uint)macroId + 1000;
            base.Save(writer);
            writer.WriteAttributeString("name", TheMacro.Name);
        }

        public override void Restore(XmlElement xml)
        {
            base.Restore(xml);

            Macro macro = World.Macros.FindMacro(xml.GetAttribute("name"));

            if (macro != null)
            {
                TheMacro = macro;
            }
        }
    }
}
