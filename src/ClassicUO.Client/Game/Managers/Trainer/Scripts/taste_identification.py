import API

# One Taste Identification attempt on the food picked in the Trainer tab; the tab repeats it.
TARGET = 0  # set by the Trainer tab

API.UseSkill("Taste Identification")
if API.WaitForTarget("any", 3):
    API.Target(TARGET)
