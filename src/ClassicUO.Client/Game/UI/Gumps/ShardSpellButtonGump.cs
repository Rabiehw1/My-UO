#nullable enable

using System;
using System.Xml;
using ClassicUO.Assets;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Input;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ClassicUO.Game.UI.Gumps;

/// <summary>
/// An on-screen icon for one shard-book spell. Saved and restored with the gump layout like regular spell icons.
/// </summary>
public sealed class ShardSpellButtonGump : AnchorableGump
{
    #region Private members

    private const int BASE_SIZE = 44;
    private const int ART_SIZE = 64;
    private const int BORDER = 2;
    private const ushort TEXT_HUE = 0x0481;
    private const byte TEXT_FONT = 1;
    private const int DRAG_TOLERANCE = 5;

    private ShardSpellBook _book;
    private int _index;
    private Texture2D? _art;
    private Texture2D? _illustratedArt;
    private Rectangle _artBounds;

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
    protected override bool RequiresModifierToCloseWhenAnchored => false;

    #endregion

    #region Public methods

    public override bool Draw(UltimaBatcher2D batcher, int x, int y)
    {
        if (IsDisposed)
            return false;

        if (GetCurrentArt() is { IsDisposed: false } texture)
        {
            batcher.Draw(texture, new Rectangle(x, y, Width, Height), _artBounds,
                ShaderHueTranslator.GetHueVector(0, false, Alpha, true));
        }

        return base.Draw(batcher, x, y);
    }

    internal Texture2D? GetCurrentArt() =>
        ProfileManager.CurrentProfile?.UseIllustratedShardSpellIcons == true
            ? _illustratedArt ?? _art
            : _art ?? _illustratedArt;

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

        // Rows follow ShardSpellBook and columns follow the saved spell index.
        // Empty cells at the ends of shorter books are transparent.
        _artBounds = new Rectangle(_index * ART_SIZE, (int)_book * ART_SIZE, ART_SIZE, ART_SIZE);
        _art = LoadArt("ShardSpellIcons.png");
        _illustratedArt = LoadArt("ShardSpellIllustrations.png");
        if (_art == null && _illustratedArt == null)
            AddLetterFallback(info, spell);

        SetTooltip($"{spell.Name}\n{info.Name}");

        GroupMatrixWidth = Width;
        GroupMatrixHeight = Height;
        AnchorType = ANCHOR_TYPE.SPELL;
    }

    private Texture2D? LoadArt(string name) =>
        ExternalImageLoader.Instance.TryGetEmbeddedTexture(name, out Texture2D texture)
        && texture is { IsDisposed: false } && texture.Bounds.Contains(_artBounds) ? texture : null;

    private void AddLetterFallback(ShardSpellBookInfo info, ShardSpell spell)
    {
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
    }

    #endregion
}
