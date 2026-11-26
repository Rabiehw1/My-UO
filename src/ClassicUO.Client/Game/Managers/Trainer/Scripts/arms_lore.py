import API

# One Arms Lore attempt on the weapon or armor picked in the Trainer tab; the tab repeats it.
TARGET = 0  # set by the Trainer tab

API.UseSkill("Arms Lore")
if API.WaitForTarget("any", 3):
    API.Target(TARGET)
