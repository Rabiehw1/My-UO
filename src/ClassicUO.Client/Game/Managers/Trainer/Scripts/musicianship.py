import API

# One Musicianship attempt: plays the first instrument found in your backpack. The Trainer tab repeats it.
INSTRUMENTS = [0x0EB1, 0x0EB2, 0x0EB3, 0x0EB4, 0x0E9C, 0x0E9D, 0x0E9E]

instrument = None
for graphic in INSTRUMENTS:
    instrument = API.FindType(graphic, API.Backpack)
    if instrument:
        break

if instrument:
    API.UseObject(instrument.Serial)
else:
    API.SysMsg("Musicianship: no instrument in your backpack.", 33)
