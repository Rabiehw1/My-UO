using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.UI.Controls;
using System;
using System.Collections.Generic;
using System.Xml;

namespace ClassicUO.Game.UI.Gumps
{
    public class ImprovedBuffGump : Gump
    {
        private const int PADDING_HANDLE = 13;
        private const int BAR_GAP = 2;

        private GumpPic _background;
        private Button _button;
        private bool _horizontal;
        private int _lastViewportWidth;
        private BraidedGoldResizeGrip _resizeGrip;
        private readonly List<CoolDownBar> _bars = new();
        private ushort _graphic = 2091;
        private DataBox _box;

        public ImprovedBuffGump(World world) : base(world, 0, 0)
        {
            X = 100;
            Y = 100;
            Width = BraidedGoldProgress.BuffWidth;
            Height = PADDING_HANDLE;
            CanMove = true;
            CanCloseWithRightClick = true;
            AcceptMouseInput = false;
            // This gump manages its own Width/Height in UpdateSize(). Leaving the base
            // auto-size on would let the child graphics (toggle button/background) grow the
            // empty-bar height past PADDING_HANDLE, which shifts the saved bottom anchor and
            // makes the bar creep up/down a few pixels each logout.
            WantUpdateSize = false;

            BuildGump();
        }

        public void AddBuff(BuffIcon icon)
        {
            if (icon != null && IsPositiveBuff(icon.Type))
            {
                var coolDownBar = new CoolDownBar(World, TimeSpan.FromMilliseconds(icon.Timer - Time.Ticks), (icon.Title ?? string.Empty).Replace("<br>", " "), ProfileManager.CurrentProfile.ImprovedBuffBarHue, 0, 0, icon.Graphic, icon.Type, true, IconArt(icon.Type), untimed: icon.Timer == uint.MaxValue);
                coolDownBar.SetTooltip(icon.Text);
                _bars.RemoveAll(bar => bar.IsDisposed);
                int index = _bars.FindIndex(bar => bar.buffIconType == icon.Type);
                if (index >= 0)
                {
                    _bars[index].Dispose();
                    _bars[index] = coolDownBar;
                }
                else
                {
                    _bars.Add(coolDownBar);
                }
                _box.Add(coolDownBar);
                UpdateSize();
            }
        }

        private static BraidedGoldIcon? IconArt(BuffIconType type) => type switch
        {
            BuffIconType.Bless => BraidedGoldIcon.Bless,
            BuffIconType.Protection or BuffIconType.ArchProtection => BraidedGoldIcon.Protection,
            BuffIconType.Agility => BraidedGoldIcon.Agility,
            BuffIconType.Strength => BraidedGoldIcon.Strength,
            BuffIconType.Cunning => BraidedGoldIcon.Cunning,
            _ => null
        };

        private static bool IsPositiveBuff(BuffIconType type) => type is
            BuffIconType.NightSight or BuffIconType.AchievePerfection or BuffIconType.DivineFury or
            BuffIconType.EnemyOfOne or BuffIconType.HidingAndOrStealth or BuffIconType.ActiveMeditation or
            BuffIconType.BloodOathCaster or BuffIconType.GiftOfRenewal or BuffIconType.AttuneWeapon or
            BuffIconType.EtherealVoyage or BuffIconType.GiftOfLife or BuffIconType.ArcaneEmpowerment or
            BuffIconType.ReactiveArmor or BuffIconType.Protection or BuffIconType.ArchProtection or
            BuffIconType.MagicReflection or BuffIconType.Incognito or BuffIconType.Disguised or
            BuffIconType.AnimalForm or BuffIconType.Polymorph or BuffIconType.Invisibility or
            BuffIconType.Agility or BuffIconType.Cunning or BuffIconType.Strength or
            BuffIconType.Bless or BuffIconType.StoneForm or BuffIconType.Berserk or
            BuffIconType.Fly or BuffIconType.Inspire or BuffIconType.Invigorate or
            BuffIconType.Resilience or BuffIconType.Perseverance or BuffIconType.FishPie or
            BuffIconType.DualWield or BuffIconType.Block or BuffIconType.DefenseMastery or
            BuffIconType.SpellFocusingBuff or BuffIconType.RageFocusingBuff or BuffIconType.Warding or
            BuffIconType.Surge or BuffIconType.Feint or BuffIconType.ConsecrateWeapon or
            BuffIconType.GrapesOfWrath or BuffIconType.HorrificBeast or BuffIconType.LichForm or
            BuffIconType.VampiricEmbrace or BuffIconType.CurseWeapon or BuffIconType.ReaperForm or
            BuffIconType.ImmolatingWeapon or BuffIconType.Enchant or BuffIconType.HonorableExecution or
            BuffIconType.Confidence or BuffIconType.Evasion or BuffIconType.CounterAttack or
            BuffIconType.LightningStrike or BuffIconType.MomentumStrike or BuffIconType.OrangePetals or
            BuffIconType.RoseOfTrinsic or BuffIconType.PoisonImmunity or BuffIconType.Perfection or
            BuffIconType.Honored or BuffIconType.ManaPhase or BuffIconType.Rage or
            BuffIconType.HiryuPhysicalResistance or BuffIconType.WraithForm or BuffIconType.CityTradeDeal or
            BuffIconType.Spirituality or BuffIconType.Humility or BuffIconType.Rampage or
            BuffIconType.Toughness or BuffIconType.Thrust or BuffIconType.PlayingTheOdds or
            BuffIconType.FocusedEye or BuffIconType.ElementalFury or BuffIconType.CalledShot or
            BuffIconType.SavingThrow or BuffIconType.Conduit or BuffIconType.EtherealBurst or
            BuffIconType.MysticWeapon or BuffIconType.ManaShield or BuffIconType.AnticipateHit or
            BuffIconType.Warcry or BuffIconType.Shadow or BuffIconType.WhiteTigerForm or
            BuffIconType.Bodyguard or BuffIconType.HeightenedSenses or BuffIconType.Tolerance or
            BuffIconType.Intuition or BuffIconType.EnchantedSummoning or BuffIconType.Whispering or
            BuffIconType.CombatTraining or BuffIconType.InjectedStrike or BuffIconType.Boarding or
            BuffIconType.Potency or BuffIconType.FistsOfFury or BuffIconType.BarrabHemolymphConcentrate or
            BuffIconType.JukariBurnPoiltice or BuffIconType.KurakAmbushersEssence or BuffIconType.BarakoDraftOfMight or
            BuffIconType.UraliTranceTonic or BuffIconType.SakkhraProphylaxis or BuffIconType.SwarmImmune or
            BuffIconType.BoneBreakerImmune or BuffIconType.CaddelliteInfused or BuffIconType.PotionGloriousFortune or
            BuffIconType.MysticalPolymorphTotem;

        public void RemoveBuff(BuffIconType type)
        {
            foreach (CoolDownBar bar in _bars)
                if (bar.buffIconType == type)
                    bar.Dispose();

            _bars.RemoveAll(bar => bar.IsDisposed);
            UpdateSize();
        }

        protected override void UpdateContents()
        {
            base.UpdateContents();
            UpdateSize();
        }

        public override void PreDraw()
        {
            base.PreDraw();
            if (_bars.RemoveAll(bar => bar.IsDisposed) != 0 || _lastViewportWidth != ScaleHelper.LogicalWindowWidth)
                UpdateSize();
        }

        private void UpdateSize()
        {
            int scale = ProfileManager.CurrentProfile.BraidedGoldBuffScale;
            int cardWidth = BraidedGoldResizeGrip.ScalePixels(BraidedGoldProgress.BuffWidth, scale);
            int cardHeight = BraidedGoldResizeGrip.ScalePixels(BraidedGoldProgress.BuffHeight, scale);
            _lastViewportWidth = ScaleHelper.LogicalWindowWidth;
            int columns = _horizontal
                ? Math.Min(Math.Max(1, _bars.Count), Math.Max(1, (_lastViewportWidth + BAR_GAP) / (cardWidth + BAR_GAP)))
                : 1;
            int rows = (_bars.Count + columns - 1) / columns;
            int handleY = Y + Height - PADDING_HANDLE;
            Width = columns * (cardWidth + BAR_GAP) - BAR_GAP;
            Height = PADDING_HANDLE + rows * (cardHeight + BAR_GAP);
            Y = handleY - Height + PADDING_HANDLE;
            if (_horizontal && _lastViewportWidth >= Width)
                X = Math.Clamp(X, 0, _lastViewportWidth - Width);

            _background.Width = cardWidth;
            _background.Y = Height - PADDING_HANDLE + 2;
            _button.Y = Height - PADDING_HANDLE;
            _resizeGrip.X = Width - _resizeGrip.Width;
            _resizeGrip.Y = Height - _resizeGrip.Height;
            _box.Width = Width;
            _box.Height = Height - PADDING_HANDLE;

            for (int i = 0; i < _bars.Count; i++)
            {
                CoolDownBar bar = _bars[i];
                if (bar.Width != cardWidth || bar.Height != cardHeight)
                    bar.ResizeBraidedCard(scale);
                bar.X = i % columns * (cardWidth + BAR_GAP);
                int row = _horizontal ? i / columns : rows - i - 1;
                bar.Y = row * (cardHeight + BAR_GAP);
            }
        }

        public override void OnButtonClick(int buttonID)
        {
            if (buttonID == 0)
            {
                _horizontal = !_horizontal;
                UpdateSize();
            }
        }

        private void BuildGump()
        {
            _background = new GumpPic(0, 0, _graphic, 0);

            _button = new Button(0, 0x7585, 0x7589, 0x7589)
            {
                ButtonAction = ButtonAction.Activate
            };

            _button.SetTooltip(TazLang.Get("assistant_braided_toggle_buff_layout"));
            _box = new DataBox(0, 0, Width, PADDING_HANDLE);
            Profile profile = ProfileManager.CurrentProfile;
            _resizeGrip = new BraidedGoldResizeGrip(this, () => profile.BraidedGoldBuffScale, scale =>
            {
                profile.BraidedGoldBuffScale = scale;
                UpdateSize();
            });

            Add(_background);
            Add(_box);
            Add(_button);
            Add(_resizeGrip);

            if (World.Player != null)
            {
                foreach (KeyValuePair<BuffIconType, BuffIcon> k in World.Player.BuffIcons)
                {
                    AddBuff(k.Value);
                }
            }
            UpdateContents();
        }

        public override void Save(XmlTextWriter writer)
        {
            //Need to override base here, don't call it.

            writer.WriteAttributeString("type", ((int)GumpType).ToString());
            writer.WriteAttributeString("x", X.ToString());
            writer.WriteAttributeString("y", (Y + Height - PADDING_HANDLE).ToString());
            writer.WriteAttributeString("serial", LocalSerial.ToString());
            writer.WriteAttributeString("serverSerial", ServerSerial.ToString());
            writer.WriteAttributeString("isLocked", IsLocked.ToString());
            writer.WriteAttributeString("alphaOffset", AlphaOffset.ToString());

            writer.WriteAttributeString("graphic", _graphic.ToString());
            writer.WriteAttributeString("horizontal", _horizontal.ToString());
        }

        public override void Restore(XmlElement xml)
        {
            base.Restore(xml);

            _graphic = ushort.Parse(xml.GetAttribute("graphic"));
            _horizontal = bool.TryParse(xml.GetAttribute("horizontal"), out bool horizontal) && horizontal;
            // The persisted Y coordinate is the handle anchor; the profile loader sets
            // it after Restore returns. Lay out the rows relative to it on the next update.
            Height = PADDING_HANDLE;

            RequestUpdateContents();
        }

        public override GumpType GumpType => GumpType.Buff;

    }
}
