#nullable enable
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.MyraWindows.Widgets;
using ClassicUO.LegionScripting;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows;

public class ScriptEditorWindow : MyraControl
{
    private readonly ScriptFile _script;
    private const int MAX_LENGTH = 1024 * 1024;

    public ScriptEditorWindow(ScriptFile script)
        : base(script.FileName)
    {
        _script = script;
        string content = string.Join("\n", script.ReadFromFile());

        if (content.Length > MAX_LENGTH)
        {
            GameActions.Print("File too large to edit!", Constants.HUE_ERROR);
            _disposeRequested = true;
            IsVisible = false; //Need to still add to uimanager to properly dispose later.
        }
        else
        {
            Build(content);
        }

        CenterInViewPort();
        UIManager.Add(this);
        BringOnTop();
    }

    private void Build(string content)
    {
        var editor = new ScriptSourceEditor(content, _script.Type);
        var saveButton = new MyraButton("Save Changes") { Enabled = false };
        saveButton.OnClick = () =>
        {
            _script.OverrideFileContents(editor.Source);
            saveButton.Enabled = false;
        };
        editor.SourceChanged += (_, _) => saveButton.Enabled = true;

        var root = new VerticalStackPanel { Spacing = MyraStyle.STANDARD_SPACING };
        root.Widgets.Add(editor);
        root.Widgets.Add(saveButton);
        SetRootContent(root);
    }
}
