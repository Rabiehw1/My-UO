import API

# One Meditation attempt; the Trainer tab repeats it. It only trains while your mana is not full,
# you are out of war mode and your hands are empty.
if API.Player.Mana >= API.Player.ManaMax:
    API.SysMsg("Meditation: your mana is full, so this use will not train.", 33)
API.UseSkill("Meditation")
