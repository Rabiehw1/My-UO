using ClassicUO.Game;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Managers.Support;
using ClassicUO.IO;

namespace ClassicUO.Network.PacketHandlers;

internal static class TargetCursor
{
    public static void Receive(World world, ref StackDataReader p)
    {
        var cursorTarget = (CursorTarget)p.ReadUInt8();
        uint cursorId = p.ReadUInt32BE();
        var targetType = (TargetType)p.ReadUInt8();

        world.TargetManager.SetTargeting(cursorTarget, cursorId, targetType);

        bool partyTarget = world.Party.HasPendingHealCursor && world.Party.PartyHealTimer < Time.Ticks;
        bool autoTarget = TargetManager.NextAutoTarget.IsSet;
        if (partyTarget)
        {
            world.TargetManager.Target(world.Party.PartyHealTarget);
            world.Party.PartyHealTimer = 0;
            world.Party.PartyHealTarget = 0;
        }
        else if (autoTarget && TargetManager.NextAutoTarget.Matches(targetType))
            world.TargetManager.Target(TargetManager.NextAutoTarget.TargetSerial);

        bool skinTarget = AutoSkinningManager.Instance.TryHandleCursor(world, cursorTarget, targetType, partyTarget || autoTarget);
        SupportAgentManager.Instance.OnPetOrderCursor(cursorTarget, targetType, partyTarget || autoTarget || skinTarget);

        // Always clear after any target cursor (no queuing)
        TargetManager.NextAutoTarget.Clear();
    }
}
