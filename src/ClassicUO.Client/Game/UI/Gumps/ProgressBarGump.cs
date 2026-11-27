using ClassicUO.Configuration;
using ClassicUO.Game.UI.Controls;

namespace ClassicUO.Game.UI.Gumps;

public class ProgressBarGump : Gump
{
    private readonly BraidedGoldProgress _progress;

    public ProgressBarGump(World world, string title, double startPercentage = 1, int width = 200, int height = 30)
        : base(world, 0, 0)
    {
        CanCloseWithRightClick = true;
        AcceptMouseInput = false;
        WantUpdateSize = false;
        Width = width;
        Height = height;
        Add(_progress = new BraidedGoldProgress(width, height, title, 0x0E75, itemArt: true)
        {
            Fraction = startPercentage
        });
    }

    internal void SetLootProgress(int processed, int total)
    {
        _progress.Fraction = (double)processed / total;
        _progress.Value = TazLang.Get("assistant_braided_loot_count", [processed.ToString(), total.ToString()]);
    }
}
