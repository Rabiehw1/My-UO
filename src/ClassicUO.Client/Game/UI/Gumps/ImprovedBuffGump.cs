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
        private bool _direction = false;
        private ushort _graphic = 2091;
        private DataBox _box;
        private int _lastBarCount;

        public ImprovedBuffGump(World world) : base(world, 0, 0)
        {
            X = 100;
            Y = 100;
            Width = CoolDownBar.COOL_DOWN_WIDTH;
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
                var coolDownBar = new CoolDownBar(World, TimeSpan.FromMilliseconds(icon.Timer - Time.Ticks), (icon.Title ?? string.Empty).Replace("<br>", " "), ProfileManager.CurrentProfile.ImprovedBuffBarHue, 0, 0, icon.Graphic, icon.Type, true, untimed: icon.Timer == uint.MaxValue);
                coolDownBar.SetTooltip(icon.Text);
                BuffBarManager.AddCoolDownBar(coolDownBar, _direction, _box);
                _box.Add(coolDownBar);
                UpdateSize();
            }
        }

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

        public void RemoveBuff(BuffIconType graphic)
        {
            BuffBarManager.RemoveBuffType(graphic);
            UpdateSize();
        }

        private void SwitchDirections()
        {
            int dynamicHeight = CalculateDynamicHeight();
            if (!_direction)
            {
                Y -= dynamicHeight - 11;
                _background.Y = dynamicHeight - 11;
            }
            else
            {
                Y += dynamicHeight - 11;
                _background.Y = 0;
            }
            Height = dynamicHeight;
            _box.Height = dynamicHeight;
            _box.Y = 0;
            _button.Y = _background.Y - 5;
            _lastBarCount = BuffBarManager.GetActiveBarCount();
            BuffBarManager.UpdatePositions(_direction, _box);
        }

        protected override void UpdateContents()
        {
            base.UpdateContents();
            UpdateSize();
        }

        public override void PreDraw()
        {
            base.PreDraw();

            int currentCount = BuffBarManager.GetActiveBarCount();
            if (currentCount != _lastBarCount)
            {
                UpdateSize();
            }
        }

        private int CalculateDynamicHeight()
        {
            int barCount = BuffBarManager.GetActiveBarCount();
            if (barCount == 0)
                return PADDING_HANDLE;

            return PADDING_HANDLE + barCount * (CoolDownBar.COOL_DOWN_HEIGHT + BAR_GAP);
        }

        private void UpdateSize()
        {
            int newHeight = CalculateDynamicHeight();
            int oldHeight = Height;

            if (!_direction && newHeight != oldHeight)
            {
                Y -= newHeight - oldHeight;
            }

            Height = newHeight;
            _box.Height = newHeight;
            _box.Y = 0;

            if (!_direction)
            {
                _background.Y = newHeight - 11;
            }
            else
            {
                _background.Y = 0;
            }

            _button.Y = _background.Y - 5;
            _lastBarCount = BuffBarManager.GetActiveBarCount();
            BuffBarManager.UpdatePositions(_direction, _box);
        }

        public override void OnButtonClick(int buttonID)
        {
            if (buttonID == 0)
            {
                _direction = !_direction;
                SwitchDirections();
            }
        }

        private void BuildGump()
        {
            _background = new GumpPic(0, 0, _graphic, 0);
            _background.Width = CoolDownBar.COOL_DOWN_WIDTH;

            _button = new Button(0, 0x7585, 0x7589, 0x7589)
            {
                ButtonAction = ButtonAction.Activate
            };

            _box = new DataBox(0, 0, Width, PADDING_HANDLE);



            Add(_background);
            Add(_button);
            Add(_box);

            BuffBarManager.Clear();
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
            writer.WriteAttributeString("y", (_direction ? Y : Bounds.Bottom - PADDING_HANDLE).ToString());
            writer.WriteAttributeString("serial", LocalSerial.ToString());
            writer.WriteAttributeString("serverSerial", ServerSerial.ToString());
            writer.WriteAttributeString("isLocked", IsLocked.ToString());
            writer.WriteAttributeString("alphaOffset", AlphaOffset.ToString());

            writer.WriteAttributeString("graphic", _graphic.ToString());
            writer.WriteAttributeString("updown", _direction.ToString());
        }

        public override void Restore(XmlElement xml)
        {
            base.Restore(xml);

            _graphic = ushort.Parse(xml.GetAttribute("graphic"));
            _direction = bool.Parse(xml.GetAttribute("updown"));

            RequestUpdateContents();
        }

        public override GumpType GumpType => GumpType.Buff;

        private static class BuffBarManager
        {
            public const int MAX_COOLDOWN_BARS = 20;
            private static CoolDownBar[] coolDownBars = new CoolDownBar[MAX_COOLDOWN_BARS];

            public static int GetActiveBarCount()
            {
                int count = 0;
                for (int i = 0; i < coolDownBars.Length; i++)
                {
                    if (coolDownBars[i] != null && !coolDownBars[i].IsDisposed)
                        count++;
                }
                return count;
            }

            public static void AddCoolDownBar(CoolDownBar coolDownBar, bool topDown, DataBox _boxContainer)
            {
                for (int i = 0; i < coolDownBars.Length; i++)
                {
                    if (coolDownBars[i] != null && !coolDownBars[i].IsDisposed && coolDownBars[i].buffIconType == coolDownBar.buffIconType)
                    {
                        coolDownBars[i].Dispose();
                        coolDownBars[i] = coolDownBar;
                        UpdatePositions(topDown, _boxContainer);
                        return;
                    }
                    if (coolDownBars[i] == null || coolDownBars[i].IsDisposed)
                    {
                        coolDownBars[i] = coolDownBar;
                        UpdatePositions(topDown, _boxContainer);
                        return;
                    }
                }
            }

            public static void UpdatePositions(bool topDown, DataBox _boxContainer)
            {
                int actualI = 0;
                int barStride = CoolDownBar.COOL_DOWN_HEIGHT + BAR_GAP;
                for (int i = 0; i < coolDownBars.Length; i++)
                {
                    if (coolDownBars[i] != null && !coolDownBars[i].IsDisposed)
                    {
                        if (topDown)
                        {
                            coolDownBars[i].Y = (actualI * barStride) + PADDING_HANDLE;
                        }
                        else
                        {
                            coolDownBars[i].Y = _boxContainer.Height - ((actualI + 1) * barStride) - 11;
                        }
                        actualI++;
                    }
                }
            }

            public static void RemoveBuffType(BuffIconType type)
            {
                for (int i = 0; i < coolDownBars.Length; i++)
                {
                    if (coolDownBars[i] != null && !coolDownBars[i].IsDisposed)
                    {
                        if (coolDownBars[i].buffIconType == type)
                        {
                            coolDownBars[i].Dispose();
                        }
                    }
                }
            }

            public static void Clear()
            {
                for (int i = 0; i < coolDownBars.Length; i++)
                {
                    if (coolDownBars[i] != null)
                    {
                        coolDownBars[i].Dispose();
                    }
                }
                coolDownBars = new CoolDownBar[MAX_COOLDOWN_BARS];
            }
        }
    }
}
