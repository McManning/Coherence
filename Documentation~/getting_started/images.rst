
Images
=======

Coherence can sync images you edit within Blender to `Render Textures <https://docs.unity3d.com/Manual/class-RenderTexture.html>`_ in Unity.


Adding Unity Texture Slots
----------------------------

In Unity's *Coherence Settings > Texture Sync* panel add a new slot with a unique ID and Render Texture asset.

.. figure:: https://i.imgur.com/SDqWQQg.png
    :alt: Add new texture slot in Unity

.. TODO: Rules for the render texture? Scale? Format?


Syncing Blender Images
-----------------------

In Blender's Image Editor, create a new image with **Alpha** and **32 bit Float** selected.

.. figure:: https://i.imgur.com/X2FcFO5.png
    :alt: Create new image in Blender

In the *Image > Coherence Texture Sync* panel you can specify the texture slot in Unity that receives the synced image data.

.. figure:: https://i.imgur.com/gZSLcdN.png
    :alt: Assign sync slot to image

While painting on the image in Blender you can view your results in realtime on Unity's synced Render Texture.

.. figure:: https://i.imgur.com/19HAMKDl.mp4
    :alt: Painting  in the image editor
    :target: https://i.imgur.com/19HAMKD.mp4

    Painting  in the image editor (click image for mp4)



Painting on Material Inputs
----------------------------

One use of the image sync tool is to paint directly onto different Unity material inputs directly from Blender.

In Unity make sure your texture slot's Render Texture is assigned to the material surface input you want to actively paint into.

.. figure:: https://i.imgur.com/aOCL1qFl.png
    :alt: Assign RenderTexture as material input

Then within Blender switch to *Texture Paint* mode. In *Active Tool Settings* change your mode to *Single Image* and point it to the image currently syncing to the Coherence texture slot.

.. figure:: https://i.imgur.com/bMHuug3l.png
    :alt: Single Image mode

After setup you can now paint directly on your mesh within Blender's viewport and your changes will be reflected on the material in Unity.

.. figure:: https://i.imgur.com/N49kDa3l.mp4
    :alt: Painting in the viewport
    :target: https://i.imgur.com/N49kDa3.mp4

    Painting in the viewport (click image for mp4)

This technique can be expanded on by adding a texture slot per channel in your material (base color, metallic, emissive, etc) and quickly swapping between different images to author your material through Blender's tooling.

.. figure:: https://i.imgur.com/UYZY02Nl.mp4
    :alt: Painting multiple material channels
    :target: https://i.imgur.com/UYZY02N.mp4

    Painting on multiple material channels (click image for mp4)
