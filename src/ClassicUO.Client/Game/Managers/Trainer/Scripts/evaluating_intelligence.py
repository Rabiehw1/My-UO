import API

# One Evaluating Intelligence attempt on yourself; the Trainer tab repeats it.
API.UseSkill("Evaluating Intelligence")
if API.WaitForTarget("any", 3):
    API.TargetSelf()
