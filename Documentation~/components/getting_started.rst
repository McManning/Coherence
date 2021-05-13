
Writing Third Party Plugins
============================

Coherence plugins take the form of **Components** - as Python classes or Unity MonoBehaviours - that are added and removed from :class:`bpy.types.Object` instances in the scene.

A third party component attached to an object can listen to Coherence events (enabled, connected, etc) and send or receive custom events with a linked component in the connected application.

.. tip::

    All components on the same :class:`bpy.types.Object` share the same synced GameObject, so you can essentially think of each Coherence Component as our Blender-equivalent to MonoBehaviours.

Creating Blender Components
----------------------------

Blender is used for the "source of truth" of which objects in the scene should sync with Unity. Only objects with one or more components are synced to Unity.

An object can have multiple components attached to it, but it cannot contain duplicates of the same component.

To add your own component start by creating a Blender addon that registers a new component class with the Coherence API within Blender's ``register`` method:

.. code-block:: python

    bl_info = {
        'name': 'My Coherence Plugin',
        # ... your other addon settings ...
    }

    import bpy
    import Coherence

    class Light(Coherence.api.Component):
        """Custom component representing our Blender light.
        """
        @classmethod
        def poll(cls, bpy_obj):
            return bpy_obj.type == 'LIGHT'

        def on_create(self):
            print('Added Light Component!')

        def on_destroy(self):
            print('Removed Light Component!')

    def register():
        Coherence.api.register_component(Light)

    def unregister():
        Coherence.api.unregister_component(Light)

The class name ``Light`` is our common component name shared between applications and must be unique across all third party plugins registered with Coherence.

After the plugin has been registered all existing and new objects in the scene will be tested against the component's :meth:`.Component.poll` to determine if that component should automatically attach itself to that scene object.

If you want to manually attach components instead of using :meth:`Component.poll` you can use :func:`add_component` at any time after the component has been registered:

.. code-block:: python

    sun = bpy.data.objects['Sun']
    Coherence.api.add_component(sun, Light)

If this is the first component attached to the object then Coherence will start syncing the object's transformation with Unity as a new :sphinxsharp:type:`UnityEngine.GameObject`.

Creating Unity Components
--------------------------

After a component has been attached to an object in Blender, a matching :sphinxsharp:type:`UnityEngine.MonoBehaviour` can be automatically added to the synced GameObject.

Add a new MonoBehaviour to your Unity project:

.. code-block:: C#

    using UnityEngine;
    using Coherence;

    [ExecuteAlways]
    [Component("Light")]
    public class BlenderLight : MonoBehaviour, IComponent
    {
        private void OnEnable()
        {
            Debug.Log("Added Light Component!");
        }

        private void OnDisable()
        {
            Debug.Log("Removed Light Component!");
        }
    }

The :sphinxsharp:type:`ComponentAttribute` of your MonoBehaviour must match the name of the component class in Blender (``Light`` in the prior example).

To make sure the component works in edit mode you will also need to add Unity's `[ExecuteAlways] <https://docs.unity3d.com/ScriptReference/ExecuteAlways.html>`_ attribute.

By adding the :sphinxsharp:type:`IComponent` interface to the MonoBehaviour your component can now access additional Coherence API features through added extension methods.

After recompiling assemblies register your component with Coherence by selecting it in the **Register Component** button menu in Unity's Coherence Settings window.

Once you have added both synced components you can start using the Component API to share events and data between applications.


Removing Components
--------------------

Calling :meth:`.Component.destroy` or :func:`destroy_component` from Blender will remove **both** the Blender Component and the matching Unity MonoBehaviour:

.. code-block:: python

    sun = bpy.data.objects['Sun']
    Coherence.api.destroy_component(sun, Light)

Like Unity, both :meth:`.Component.on_disable` and :meth:`.Component.on_destroy` will be called when removed.

If an object has no components remaining, it will no longer be synced and the matching :sphinxsharp:type:`UnityEngine.GameObject` will be destroyed.

When removing a Scene Component, provide your current scene for :func:`destroy_component`:

.. code-block:: python

    scene = bpy.context.scene
    Coherence.api.destroy_component(scene, MyPlugin)
