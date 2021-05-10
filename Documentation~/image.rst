[WIP] Image Syncing
==============

TODO:
    * Creating an image to sync
    * Setup on Unity's side
    * Setup on Blender's side
    * Syncing channels

Outline:

-  Create a named texture slot in Unity, point it to an RT (or other
   texture target)
-  Create image in blender - making sure it's float with an alpha
   channel
-  Assign the texture to the slot in Blender and start painting on it
-  More advanced workflows - painting on multiple layers like
   spec/metallic/diffuse at once and material setup to support that

Image Dump:

.. figure:: https://i.imgur.com/SDqWQQg.png
   :alt: Add new slot in Unity

.. figure:: https://i.imgur.com/X2FcFO5.png
   :alt: Create new image in Blender

.. figure:: https://i.imgur.com/gZSLcdN.png
   :alt: Assign sync slot to image

.. figure:: https://i.imgur.com/aOCL1qFl.png
   :alt: Assign RenderTexture as material input

.. figure:: https://i.imgur.com/Dno5xdZ.png
   :alt: Texture Paint mode

*Active Tool Settings > Mode* to *Single Image* and select the image
currently synced

.. figure:: https://i.imgur.com/bMHuug3l.png
   :alt: Single Image mode


Videos:


.. figure:: https://i.imgur.com/19HAMKDl.mp4
    :alt: Painting  in the image editor
    :target: https://i.imgur.com/19HAMKD.mp4

    Painting  in the image editor (click image for mp4)

.. figure:: https://i.imgur.com/N49kDa3l.mp4
    :alt: Painting in the viewport
    :target: https://i.imgur.com/N49kDa3.mp4

    Painting in the viewport (click image for mp4)

.. figure:: https://i.imgur.com/UYZY02Nl.mp4
    :alt: Painting multiple material channels
    :target: https://i.imgur.com/UYZY02N.mp4

    Painting on multiple material channels (click image for mp4)

