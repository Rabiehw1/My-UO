#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ClassicUO.Configuration;

namespace ClassicUO.Game.Managers.ShardSpells;

/// <summary>Per-character choices for shard spell books, saved to <c>shard_spellbooks.json</c>.</summary>
public sealed class ShardSpellSave : JsonSave<ShardSpellSave>, INotifyPropertyChanged
{
    /// <summary>The player's own copy of each book, by book name. Spells are cast by using this exact item.</summary>
    public Dictionary<string, uint> BookSerials { get; set; } = new();

    /// <summary>Gump buttons that differ from the book's reading order, keyed "Book:index".</summary>
    public Dictionary<string, int> ButtonOverrides { get; set; } = new();

    protected override SettingsScope Scope => SettingsScope.Char;
    protected override string FileName => "shard_spellbooks.json";
    protected override JsonTypeInfo<ShardSpellSave> TypeInfo => ShardSpellJsonContext.Default.ShardSpellSave;
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ShardSpellSave))]
internal partial class ShardSpellJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Casts shard spells: uses the player's picked book and presses the spell's gump button. Created per game session
/// and driven from GameScene.Update(); game thread only.
/// </summary>
internal sealed class ShardSpellCaster : IDisposable
{
    #region Public accessors

    /// <summary>The caster for the current game session; created on first use.</summary>
    public static ShardSpellCaster Instance
    {
        get
        {
            field ??= new ShardSpellCaster();
            return field;
        }
        private set;
    }

    /// <summary>Settings of the logged-in character, loaded on first use.</summary>
    public ShardSpellSave Settings => _settings ??= ShardSpellSave.Load();

    #endregion

    #region Private members

    private readonly World _world;
    private readonly PendingGumpButton _pendingGump;
    private ShardSpellSave? _settings;

    #endregion

    #region Ctor

    private ShardSpellCaster()
    {
        _world = World.Instance;
        _pendingGump = new PendingGumpButton(_world);
    }

    #endregion

    #region Public methods

    /// <summary>Casts spell number <paramref name="index" /> of <paramref name="book" />.</summary>
    /// <param name="book">Book holding the spell.</param>
    /// <param name="index">Position of the spell in the book.</param>
    public void Cast(ShardSpellBook book, int index)
    {
        ShardSpell? spell = ShardSpellCatalog.GetSpell(book, index);

        if (spell == null || !_world.InGame)
            return;

        uint bookSerial = GetBookSerial(book);

        if (bookSerial == 0)
        {
            GameActions.Print(TazLang.Get("shardspells_nobook", [ShardSpellCatalog.Get(book).Name]), Constants.HUE_WARN);
            return;
        }

        _pendingGump.Arm(GetButton(book, index), ShardSpellCatalog.Get(book).Name, Time.Ticks);
        GameActions.DoubleClick(_world, bookSerial, ignoreQueue: true);
    }

    /// <param name="book">Book to look up.</param>
    /// <returns>The picked book's serial, or 0 if none was picked.</returns>
    public uint GetBookSerial(ShardSpellBook book) => Settings.BookSerials.GetValueOrDefault(book.ToString());

    /// <summary>Remembers the player's copy of <paramref name="book" /> and saves.</summary>
    /// <param name="book">Book type.</param>
    /// <param name="serial">Item serial of the player's book.</param>
    public void SetBookSerial(ShardSpellBook book, uint serial)
    {
        Settings.BookSerials[book.ToString()] = serial;
        Settings.Save();
    }

    /// <param name="book">Book holding the spell.</param>
    /// <param name="index">Position of the spell in the book.</param>
    /// <returns>The gump button that casts the spell.</returns>
    public int GetButton(ShardSpellBook book, int index) =>
        Settings.ButtonOverrides.TryGetValue(ButtonKey(book, index), out int button)
            ? button
            : ShardSpellCatalog.GetSpell(book, index)?.DefaultButton ?? 0;

    /// <summary>Changes the gump button of one spell; the book's reading order is used when equal.</summary>
    /// <param name="book">Book holding the spell.</param>
    /// <param name="index">Position of the spell in the book.</param>
    /// <param name="button">New gump button.</param>
    public void SetButton(ShardSpellBook book, int index, int button)
    {
        string key = ButtonKey(book, index);

        if (button == ShardSpellCatalog.GetSpell(book, index)?.DefaultButton)
            Settings.ButtonOverrides.Remove(key);
        else
            Settings.ButtonOverrides[key] = button;
    }

    /// <summary>Presses a pending gump button once its gump opens. Call every frame.</summary>
    public void Update() => _pendingGump.Update(Time.Ticks);

    /// <summary>Saves settings; called when the game scene unloads.</summary>
    public void Dispose()
    {
        _settings?.Save();
        Instance = null!;
    }

    #endregion

    #region Private methods

    private static string ButtonKey(ShardSpellBook book, int index) => $"{book}:{index}";

    #endregion
}
