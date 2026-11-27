using ClassicUO.Configuration;
using ClassicUO.Game.UI.Controls;

namespace ClassicUO.Game.UI.Gumps;

public class ProgressBarGump : Gump
{
    private readonly BraidedGoldProgress _progress;
    private readonly BraidedGoldResizeGrip _resizeGrip;

    public ProgressBarGump(World world, string title, double startPercentage = 1, int width = BraidedGoldProgress.CardWidth, int height = BraidedGoldProgress.CardHeight)
        : base(world, 0, 0)
    {
        CanCloseWithRightClick = true;
        AcceptMouseInput = false;
        WantUpdateSize = false;
        Width = width;
        Height = height;
        Add(_progress = new BraidedGoldProgress(width, height, title, BraidedGoldIcon.Loot)
        {
            Fraction = startPercentage
        });

        Profile profile = ProfileManager.CurrentProfile;
        _resizeGrip = new BraidedGoldResizeGrip(this, () => profile.BraidedGoldLootScale, scale =>
        {
            profile.BraidedGoldLootScale = scale;
            Resize(scale);
        });
        Add(_resizeGrip);
        Resize(profile.BraidedGoldLootScale);

        void Resize(int scale)
        {
            Width = BraidedGoldResizeGrip.ScalePixels(width, scale);
            Height = BraidedGoldResizeGrip.ScalePixels(height, scale);
            _progress.Resize(Width, Height);
            _resizeGrip.X = Width - _resizeGrip.Width;
            _resizeGrip.Y = Height - _resizeGrip.Height;
        }
    }

    internal void SetLootProgress(int processed, int total)
    {
        _progress.Fraction = (double)processed / total;
        _progress.Value = TazLang.Get("assistant_braided_loot_count", [processed.ToString(), total.ToString()]);
    }
}
