# OpenXR Face tracking

This mod will contain all important files to allow users to use basic face tracking without setup (of course, it will only go as well as it can).

## Whats the reason behind it?

The idea behind is that you can use VR headsets that uses OpenXR eye and mouth tracking like the HTC Vive Focus Vision or the HTC Vive Pro Eye (with newer firmware).
It will also allow using newer VRs that will use OpenXR Tracking for face and eye.

## idea of features

- **Default setup:** just drag and drop the mod into the mods folder and start ChilloutVR, activate the mod with a toggle and let the magic happen
- **More detailed setup:** if you want to have specific blendshapes to run while using eye or face tracking you can put the mod into your Unity project and add the script to your avatar to add something like ear movement when raising an eyebrow
- **enable or disable different tracking:** to avoid conflicts with different tracking systems you can disable the OpenXR tracking for mouth or eyes

## what should work?

- **CCK setup usage:** the CCK already contains many important things that allow already a based version of eye tracking or mouth tracking. For e.g. its possible to use default eye parameters with the eye limitations in the CCK or use the blinking setup of Chillout for the eyelid.
- **Bone usage:** some avatars are already face tracking compatible and allow the user with a setup in the avatar discriptor to implement everything needed. However not everyone knows how to set this up without issues. The mod should be able to track every neccessary bone for eye or mouth tracking.
- **Blendshape usage:** some other avatars also uses blendshapes to make everything trackable. for that it's possible to use the face tracking mesh of the cck and check every blendshape that it contains to see if there might be already the right blendshapes to use. This could require to have the modscript at least in your avatar.

## What you think?

If you think that is a nice idea and could make ChilloutVR even more interesting feel free and help me with that poject :D
