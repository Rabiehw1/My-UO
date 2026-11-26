import API

# One Anatomy attempt on yourself; the Trainer tab repeats it.
API.UseSkill("Anatomy")
if API.WaitForTarget("any", 3):
    API.TargetSelf()
