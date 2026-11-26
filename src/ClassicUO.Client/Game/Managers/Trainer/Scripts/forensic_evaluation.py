import API

# One Forensic Evaluation attempt on the nearest corpse; the Trainer tab repeats it.
CORPSE = 0x2006

corpses = API.GetItemsOnGround(2, CORPSE)
if not corpses:
    API.SysMsg("Forensic Evaluation: no corpse within 2 tiles.", 33)
else:
    API.UseSkill("Forensic Evaluation")
    if API.WaitForTarget("any", 3):
        API.Target(corpses[0].Serial)
