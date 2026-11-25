#nullable enable

using System;
using System.Xml;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.UI.Gumps;

/// <summary>
/// An on-screen icon for one shard-book spell (Necro, Earth, Holy, Songs, Archaic). Drawn in the book's colour with
/// the spell's initials, since the client has no art for these spells; double-click (or single-click, per the
/// global icon setting) casts it. Saved and restored with the gump layout like regular spell icons.
/// </summary>
public sealed class ShardSpellButtonGump : AnchorableGump
{
    #region Private members

    private const int BASE_SIZE = 44;
    private const int BORDER = 2;
    private const ushort TEXT_HUE = 0x0481;
    private const byte TEXT_FONT = 1;
    private const int DRAG_TOLERANCE = 5;

    private ShardSpellBook _book;
    private int _index;

    #endregion

    #region Ctor

    /// <summary>Creates an empty icon, filled by <see cref="Restore" />.</summary>
    /// <param name="world">The game world.</param>
    public ShardSpellButtonGump(World world) : base(world, 0, 0)
    {
        CanMove = true;
        AcceptMouseInput = true;
        CanCloseWithRightClick = true;
        CanBeLocked = false;
        Width = Height = (int)Math.Round(BASE_SIZE * (ProfileManager.CurrentProfile.SpellIconScale / 100f));
    }

    /// <summary>Creates an icon for spell <paramref name="index" /> of <paramref name="book" />.</summary>
    /// <param name="world">The game world.</param>
    /// <param name="book">Book holding the spell.</param>
    /// <param name="index">Position of the spell in the book.</param>
    public ShardSpellButtonGump(World world, ShardSpellBook book, int index) : this(world)
    {
        _book = book;
        _index = index;
        BuildGump();
    }

    #endregion

    #region Public accessors

    public override GumpType GumpType => GumpType.ShardSpellButton;

    #endregion

    #region Public methods

    public override bool OnMouseDoubleClick(int x, int y, MouseButtonType button)
    {
        if (ProfileManager.GlobalSettings.SingleClickIconUse || button != MouseButtonType.Left)
            return false;

        ShardSpellCaster.Instance.Cast(_book, _index);
        return true;
    }

    public override void OnMouseUp(int x, int y, MouseButtonType button)
    {
        base.OnMouseUp(x, y, button);

        Point offset = Mouse.LDragOffset;

        if (ProfileManager.GlobalSettings.SingleClickIconUse
            && button == MouseButtonType.Left
            && Math.Abs(offset.X) < DRAG_TOLERANCE
            && Math.Abs(offset.Y) < DRAG_TOLERANCE)
        {
            ShardSpellCaster.Instance.Cast(_book, _index);
        }
    }

    public override void Save(XmlTextWriter writer)
    {
        base.Save(writer);
        writer.WriteAttributeString("book", ((int)_book).ToString());
        writer.WriteAttributeString("index", _index.ToString());
    }

    public override void Restore(XmlElement xml)
    {
        base.Restore(xml);
        _ = int.TryParse(xml.GetAttribute("book"), out int book);
        _ = int.TryParse(xml.GetAttribute("index"), out _index);
        _book = (ShardSpellBook)book;

        if (ShardSpellCatalog.GetSpell(_book, _index) == null)
        {
            Dispose();
            return;
        }

        BuildGump();
    }

    #endregion

    #region Private methods

    private void BuildGump()
    {
        ShardSpellBookInfo info = ShardSpellCatalog.Get(_book);
        ShardSpell spell = ShardSpellCatalog.GetSpell(_book, _index)!;

        Add(new AlphaBlendControl(1f) { Width = Width, Height = Height, BaseColor = Color.Black, AcceptMouseInput = false });
        Add(new AlphaBlendControl(1f)
        {
            X = BORDER,
            Y = BORDER,
            Width = Width - BORDER * 2,
            Height = Height - BORDER * 2,
            BaseColor = info.IconColor,
            AcceptMouseInput = false
        });

        var label = new Label(spell.Abbreviation, true, TEXT_HUE, Width, TEXT_FONT, FontStyle.BlackBorder, TEXT_ALIGN_TYPE.TS_CENTER)
        {
            AcceptMouseInput = false
        };
        label.Y = (Height - label.Height) / 2;
        Add(label);

        SetTooltip($"{spell.Name}\n{info.Name}");

        GroupMatrixWidth = Width;
        GroupMatrixHeight = Height;
        AnchorType = ANCHOR_TYPE.SPELL;
    }

    #endregion
}
