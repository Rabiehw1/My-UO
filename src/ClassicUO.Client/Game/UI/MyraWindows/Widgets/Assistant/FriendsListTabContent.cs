#nullable enable
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.Agents;
using ClassicUO.Game.UI.MyraWindows.Widgets.Assistant.ItemLists;
using Myra.Graphics2D.UI;

namespace ClassicUO.Game.UI.MyraWindows.Widgets.Assistant;

public static class FriendsListTabContent
{
    public static Widget Build() => Build(AgentPageInteraction.InGame);

    internal static Widget Build(AgentPageInteraction interaction)
    {
        var friendsListPanel = new VerticalStackPanel { Spacing = 4 };

        void BuildFriendsList()
        {
            friendsListPanel.Widgets.Clear();

            List<FriendEntry> friends = FriendsListManager.Instance.GetFriends();

            if (friends.Count == 0)
            {
                friendsListPanel.Widgets.Add(new MyraLabel("No friends added yet.", MyraLabel.TextStyle.P));
                return;
            }

            var grid = new MyraGrid();
            grid.SetupWithHeaders(
                GridColumnInfo.Fill(TazLang.Get("mog_kw_name")),
                GridColumnInfo.Numeric(TazLang.Get("assistant_friends_serial")),
                GridColumnInfo.Auto(TazLang.Get("assistant_friends_date_added")),
                GridColumnInfo.Auto(TazLang.Get("agent_col_actions"))
            );

            int row = 1;
            for (int i = friends.Count - 1; i >= 0; i--)
            {
                FriendEntry f = friends[i];

                grid.AddWidget(MyraGrid.TableText(f.Name ?? TazLang.Get("assistant_friends_unknown")), row, 0);
                grid.AddWidget(MyraGrid.TableText(f.Serial != 0 ? f.Serial.ToString() : "—", alignRight: true), row, 1);
                grid.AddWidget(MyraGrid.TableText(f.DateAdded.ToString("yyyy-MM-dd")), row, 2);
                grid.AddWidget(MyraStyle.ApplyButtonDangerStyle(new MyraButton(TazLang.Get("remove"), () =>
                {
                    bool removed = f.Serial != 0
                        ? FriendsListManager.Instance.RemoveFriend(f.Serial)
                        : FriendsListManager.Instance.RemoveFriend(f.Name);

                    if (removed)
                    {
                        GameActions.Print(World.Instance, $"Removed {f.Name} from friends list");
                        BuildFriendsList();
                    }
                })), row, 3);

                row++;
            }

            friendsListPanel.Widgets.Add(grid);
        }

        BuildFriendsList();

        var root = new VerticalStackPanel { Spacing = 6 };
        var add = new MyraButton("Add Friend", () =>
        {
            GameActions.Print(World.Instance, "Target a player to add to friends list");
            interaction.SelectTarget(targeted =>
            {
                if (targeted is Mobile mobile)
                {
                    if (FriendsListManager.Instance.AddFriend(mobile))
                    {
                        GameActions.Print(World.Instance, $"Added {mobile.Name} to friends list");
                        BuildFriendsList();
                    }
                    else
                    {
                        GameActions.Print(World.Instance, $"Could not add {mobile.Name} — already in friends list");
                    }
                }
                else
                {
                    GameActions.Print(World.Instance, "Invalid target — must be a player");
                }
            });
        });
        root.Widgets.Add(ItemListsTabContent.Section(interaction, "Saved friends",
            new MyraLabel("Add a player by targeting them in game. Agents that use the friends list read these entries.", MyraLabel.TextStyle.P) { Wrap = true },
            add,
            interaction.IsDesktop ? friendsListPanel : new ScrollViewer { Height = 300, Content = friendsListPanel }));

        return root;
    }
}
