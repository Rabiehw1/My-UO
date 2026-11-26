import API

# One Animal Lore attempt on the animal picked in the Trainer tab; the tab repeats it.
TARGET = 0  # set by the Trainer tab

API.UseSkill("Animal Lore")
if API.WaitForTarget("any", 3):
    API.Target(TARGET)
