
Mapping Materials to Unity
===========================

The **Material Settings** section in Unity's `Coherence Settings` window defines the mapping from Blender material names to Unity material assets.

.. image:: https://i.imgur.com/CmzNmW8.png
    :alt: Material Settings UI

The **Material Overrides** list is a direct one-to-one mapping between a named material in Blender and a Unity material asset and is the first thing checked. You can use the +/- buttons to add and remove mappings from this list.

If a Blender material is not in the overrides list then Coherence will use Unity's `Resources.Load() <https://docs.unity3d.com/ScriptReference/Resources.Load.html>`_ to find a match in the Resources directory. The **Resources Path** setting is an optional subdirectory to search for materials within your `Resources` folder.

If Coherence fails to find a match in either the overrides or resources path, then the **Default Material** will be applied to the mesh.

Example:
    If I have a Blender material named "Diffuse" attached to a synced SceneObject then, given the image above, the following matches will be tried in order:

    1. An entry in **Material Overrides** with ``Diffuse`` as the key
    2. ``Resources/Materials/Diffuse.mat``
    3. ``Blender-Default.mat``
