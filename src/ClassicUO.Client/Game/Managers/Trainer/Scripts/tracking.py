import API

# One Tracking attempt; the Trainer tab repeats it. The skill is checked when used, so the creature
# list it opens is closed again.
API.UseSkill("Tracking")
if API.WaitForGump(delay=3):
    API.CloseGump()
