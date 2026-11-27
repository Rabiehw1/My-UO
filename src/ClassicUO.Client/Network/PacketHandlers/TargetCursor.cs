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

        bool liveCursor = world.TargetManager.IsTargeting && TargetManager.IsLiveServerCursor(cursorTarget, targetType);
        uint partyTarget = liveCursor ? world.Party.TakeHealCursor(cursorTarget, targetType) : 0;
        bool autoTarget = liveCursor && partyTarget == 0 && TargetManager.NextAutoTarget.CanAnswer(cursorTarget, targetType);
        if (partyTarget != 0)
            world.TargetManager.Target(partyTarget, preserveAutoTarget: true);
        else if (autoTarget)
            world.TargetManager.Target(TargetManager.NextAutoTarget.TargetSerial);

        bool claimed = partyTarget != 0 || autoTarget;
        bool skinTarget = AutoSkinningManager.Instance.TryHandleCursor(world, cursorTarget, targetType, claimed);
        if (liveCursor && !claimed && !skinTarget && Game.Managers.Trainer.PracticeTrainer.TryCursor(world, cursorTarget, targetType))
            return;
        if (liveCursor && !claimed && !skinTarget && Game.Managers.Trainer.InscriptionTrainer.TryCursor(world, cursorTarget, targetType))
            return;
        if (liveCursor && !claimed && !skinTarget && Game.Managers.Trainer.FishingTrainer.TryCursor(world, cursorTarget, targetType))
            return;
        if (liveCursor && !claimed && !skinTarget && Game.Managers.Trainer.BardTrainer.TryCursor(world, cursorTarget, targetType))
            return;
        if (liveCursor && !claimed && !skinTarget && Game.Managers.Trainer.BeggingTrainer.TryCursor(world, targetType))
            return;
        if (liveCursor && !claimed && !skinTarget && Game.Managers.Trainer.BlacksmithTrainer.TryRecoveryCursor(world, cursorTarget, targetType))
            return;
        if (liveCursor && !claimed && !skinTarget && Game.Managers.Trainer.LumberjackTrainer.TryCursor(world, cursorTarget, targetType))
            return;
        if (liveCursor && !claimed && !skinTarget && Game.Managers.Trainer.TailoringTrainer.TryCursor(world, cursorTarget, targetType))
            return;
        if (liveCursor)
            SupportAgentManager.Instance.OnPetOrderCursor(cursorTarget, targetType, claimed || skinTarget);
    }
}
