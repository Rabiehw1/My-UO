using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using System;

namespace ClassicUO.Game.UI.Gumps
{
    public class CoolDownBar : Gump
    {
        public const int COOL_DOWN_WIDTH = 180, COOL_DOWN_HEIGHT = 30;
        public static int DEFAULT_X => ProfileManager.CurrentProfile.CoolDownX;
        public static int DEFAULT_Y => ProfileManager.CurrentProfile.CoolDownY;

        private AlphaBlendControl background, foreground;
        private readonly Label nameText, timerText;
        private DateTime expire;
        private TimeSpan duration;
        private int startX, startY;
        public string Name { get; }
        private readonly bool isBuffBar;
        private readonly BraidedGoldProgress _braided;
        private readonly bool _untimed;
        private readonly BraidedGoldResizeGrip _resizeGrip;
        internal bool IsEstimated { get; init; }

        private GumpPic gumpPic;

        public BuffIconType buffIconType;

        public CoolDownBar(World world, TimeSpan duration, string name, ushort hue, int x, int y,
            ushort graphic = ushort.MaxValue, BuffIconType type = BuffIconType.Unknown2, bool isBuffBar = false)
            : this(world, duration, name, hue, x, y, graphic, type, isBuffBar, null, false) { }

        internal CoolDownBar(World world, TimeSpan _duration, string _name, ushort _hue, int x, int y,
            ushort graphic, BuffIconType type, bool isBuffBar, BraidedGoldIcon? icon, bool untimed) : base(world, 0, 0)
        {
            #region VARS
            Width = COOL_DOWN_WIDTH;
            Height = COOL_DOWN_HEIGHT;
            X = x;
            startX = x;
            Y = y;
            startY = y;
            expire = DateTime.Now + _duration;
            duration = _duration;
            CanCloseWithRightClick = true;
            CanMove = true;
            AcceptMouseInput = true;
            buffIconType = type;
            this.isBuffBar = isBuffBar;
            _untimed = untimed;
            #endregion

            if (isBuffBar || icon.HasValue)
            {
                Width = isBuffBar ? BraidedGoldProgress.BuffWidth : BraidedGoldProgress.CardWidth;
                Height = isBuffBar ? BraidedGoldProgress.BuffHeight : BraidedGoldProgress.CardHeight;
                WantUpdateSize = false;
                Add(_braided = new BraidedGoldProgress(Width, Height, _name,
                    icon, graphic,
                    accentHue: isBuffBar && _hue != Profile.DefaultImprovedBuffBarHue ? _hue : (ushort)0));
                Profile profile = ProfileManager.CurrentProfile;
                if (isBuffBar)
                {
                    ResizeBraidedCard(profile.BraidedGoldBuffScale);
                }
                else
                {
                    bool fish = icon == BraidedGoldIcon.Fish;
                    Add(_resizeGrip = new BraidedGoldResizeGrip(this,
                        () => fish ? profile.BraidedGoldFishScale : profile.BraidedGoldPotionScale,
                        scale =>
                        {
                            if (fish)
                                profile.BraidedGoldFishScale = scale;
                            else
                                profile.BraidedGoldPotionScale = scale;
                            ResizeBraidedCard(scale);
                        }));
                    ResizeBraidedCard(fish ? profile.BraidedGoldFishScale : profile.BraidedGoldPotionScale);
                }
                return;
            }

            #region BACK/FORE GROUND
            background = new AlphaBlendControl(0.3f);
            background.Width = COOL_DOWN_WIDTH;
            background.Height = COOL_DOWN_HEIGHT;
            background.Hue = _hue;

            foreground = new AlphaBlendControl(0.8f);
            foreground.Width = COOL_DOWN_WIDTH;
            foreground.Height = COOL_DOWN_HEIGHT;
            foreground.Hue = _hue;
            #endregion

            if (graphic != ushort.MaxValue)
            {
                gumpPic = new GumpPic(0, 2, graphic, 0);
                background.X = gumpPic.Width;
                background.Width = COOL_DOWN_WIDTH - gumpPic.Width;

                foreground.X = gumpPic.Width;
                foreground.Width = COOL_DOWN_WIDTH - gumpPic.Width;
            }

            #region LABELS
            Name = _name;
            if (_name.Length > 17)
            {
                _name = _name.Substring(0, 16) + "..";
            }

            nameText = new Label(_name, true, _hue, background.Width, style: FontStyle.BlackBorder, align: Assets.TEXT_ALIGN_TYPE.TS_CENTER);
            timerText = new Label("", true, _hue, background.Width, style: FontStyle.BlackBorder, align: Assets.TEXT_ALIGN_TYPE.TS_CENTER);

            nameText.X = background.X;
            timerText.X = background.X;
            timerText.Y = COOL_DOWN_HEIGHT - timerText.Height - 2;
            #endregion

            #region ADD CONTROLS
            if (graphic != ushort.MaxValue)
                Add(gumpPic);
            Add(background);
            Add(foreground);
            Add(nameText);
            Add(timerText);
            #endregion
        }

        internal void ResizeBraidedCard(int scale)
        {
            Width = BraidedGoldResizeGrip.ScalePixels(isBuffBar ? BraidedGoldProgress.BuffWidth : BraidedGoldProgress.CardWidth, scale);
            Height = BraidedGoldResizeGrip.ScalePixels(isBuffBar ? BraidedGoldProgress.BuffHeight : BraidedGoldProgress.CardHeight, scale);
            _braided.Resize(Width, Height);
            if (_resizeGrip != null)
            {
                _resizeGrip.X = Width - _resizeGrip.Width;
                _resizeGrip.Y = Height - _resizeGrip.Height;
            }
        }

        private static string GetBuffBarFont()
        {
            GlobalSettingsSave settings = ProfileManager.GlobalSettings;
            return string.IsNullOrWhiteSpace(settings?.BuffBarFont) ? EmbeddedFontNames.AVADONIAN : settings.BuffBarFont;
        }

        private static float GetBuffBarFontSize()
        {
            GlobalSettingsSave settings = ProfileManager.GlobalSettings;
            return settings == null || settings.BuffBarFontSize <= 0 ? 14 : settings.BuffBarFontSize;
        }

        public TimeSpan Remaining => expire - DateTime.Now;

        public void Update(TimeSpan? maxValue = null, TimeSpan? currentValue = null)
        {
            if (maxValue.HasValue)
                duration = maxValue.Value;

            if (currentValue.HasValue)
                expire = DateTime.Now + currentValue.Value;
        }

        public void Restart()
        {
            expire = DateTime.Now + duration;
        }

        public override void Update()
        {
            base.Update();

            if (isBuffBar)
                _braided.SetFont(GetBuffBarFont(), GetBuffBarFontSize());

            if (
                !isBuffBar &&
                (ProfileManager.CurrentProfile?.UseLastMovedCooldownPosition ?? false) &&
                (X != startX || Y != startY)
                )
            {
                ProfileManager.CurrentProfile.CoolDownX = X;
                ProfileManager.CurrentProfile.CoolDownY = Y;
                startX = X;
                startY = Y;
            }
        }

        public override bool Draw(UltimaBatcher2D batcher, int x, int y)
        {
            if (IsDisposed)
                return false;

            if (!_untimed && DateTime.Now >= expire)
            {
                Dispose();
                return false;
            }

            TimeSpan remaing = expire - DateTime.Now;
            if (_braided != null)
            {
                _braided.Fraction = _untimed ? 1 : remaing.TotalMilliseconds / Math.Max(1, duration.TotalMilliseconds);
                _braided.Value = _untimed ? "" : isBuffBar
                    ? $"{(IsEstimated ? "~" : "")}{Math.Ceiling(remaing.TotalMinutes)}m"
                    : $"{remaing.TotalSeconds:0.0}s";
                return base.Draw(batcher, x, y);
            }

            if (remaing < TimeSpan.FromMinutes(60))
            {
                int offset = 0;
                if (gumpPic != null)
                    offset = gumpPic.Width;
                foreground.Width = Math.Max(0, Math.Min(COOL_DOWN_WIDTH - offset, (int)((remaing.TotalSeconds / duration.TotalSeconds) * (COOL_DOWN_WIDTH - offset))));
                timerText.Text = ((int)remaing.TotalSeconds).ToString();
            }

            base.Draw(batcher, x, y);

            batcher.DrawRectangle(
                    SolidColorTextureCache.GetTexture(Color.Black),
                    x, y,
                    COOL_DOWN_WIDTH,
                    COOL_DOWN_HEIGHT,
                    ShaderHueTranslator.GetHueVector(background.Hue, false, 1f)
                );
            batcher.DrawRectangle(
                SolidColorTextureCache.GetTexture(Color.Black),
                x + 1, y + 1,
                COOL_DOWN_WIDTH - 2,
                COOL_DOWN_HEIGHT - 2,
                ShaderHueTranslator.GetHueVector(background.Hue, false, 1f)
            );

            return true;
        }
    }
}
