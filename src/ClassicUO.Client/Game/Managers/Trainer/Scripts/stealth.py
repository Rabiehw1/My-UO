import API

# One Stealth attempt; the Trainer tab repeats it. Stealth needs you hidden, so a visible
# character hides instead (that use trains Hiding).
if API.Player.IsHidden:
    API.UseSkill("Stealth")
else:
    API.UseSkill("Hiding")
