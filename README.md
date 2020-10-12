# Coherence

Load up a Unity scene and connect to Blender to sync your Blender scene directly with Unity in realtime.

![Coherence Demo](Documentation~/demo.gif)

(For additional videos, check out [https://twitter.com/cmcmanning](https://twitter.com/cmcmanning))

## Use Cases

- Leverage the full suite of Blender tools for level editing within Unity
- WYSIWYG shader workflow for those more esoteric rendering pipelines (e.g. custom URPs)
- Quicker iteration during Unity's play mode (e.g. making adjustments while testing in VR)

## Work in Progress

This project is still in proof of concept phase and is under active development. It has a lot of usability/stability issues that need be resolved before an initial release.

The project goals are listed below as the target feature list.

## Features

- [x] Sync and render Unity cameras back into the Blender viewport in realtime as you work
- [x] Support for any Unity graphics pipeline (ShaderLab, URP, HDRP) rendered back into Blender
- [x] Send Blender mesh data (vertices, normals, UVs) to Unity in realtime
- [ ] Sync Blender's [Texture Paint](https://docs.blender.org/manual/en/latest/sculpt_paint/texture_paint/introduction.html) to Unity textures in realtime
- [ ] Animation workflow support (bones, weight painting, keyframe editing, etc)
- [ ] Quickly open Unity scene objects in Blender for editing
