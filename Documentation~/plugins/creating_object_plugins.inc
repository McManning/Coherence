
Creating Object Plugins
------------------------

Object Plugins are instantiated onto a single :class:`bpy.types.Object` and synced :sphinxsharp:type:`UnityEngine.GameObject`.

Object Plugins are always associated with a parent Global Plugin and will automatically be registered and unregistered alongside the parent.

Global Plugins in Blender can create instances of Object Plugins that are attached to :class:`bpy.types.Object` instances in the scene. Once an object has one or more Object Plugins attached to it, that object is synced to Unity and is **optionally** given a matching Object Plugin MonoBehaviour.

Multiple Object Plugins may be attached to the same object in both Unity and Blender, all from different third party plugins simultaneously. For example, one plugin may handle transmitting the objects mesh representation to Unity while another plugin adds additional vertex data streams, custom properties, or send events triggered by Blender operators.

.. note::
    Only Blender plugins can control when Object Plugins are instantiated through :py:meth:`.GlobalPlugin.instantiate`. Unity will simply copy instantiation from Blender for a matching MonoBehaviour.

A Global Plugin may instantiate Object Plugins at any time after registration but this is typically done within the :py:meth:`.GlobalPlugin.on_add_bpy_object` callback. Plugins receive this event callback for every object in the scene and can filter out which objects should have a specific Object Plugin.

As an example: we have a plugin that attaches a ``Light`` Object Plugin to every Blender object of type ``LIGHT``:

.. code-block:: python

    import bpy
    import Coherence

    class Light(Coherence.api.ObjectPlugin):
        """Custom Object Plugin representing our Blender light.

        The class name (Light) will be used as the object plugin name
        and made available to the plugin(s) within Unity.
        """
        def on_create(self):
            print('Added Light Object Plugin!')

        def on_destroy(self):
            print('Removed Light Object Plugin!')

    class LightsPlugin(Coherence.api.Plugin):
        """
        Global Plugin that instantiates Light Object Plugins for Blender lights
        """
        def on_add_bpy_object(self, bpy_obj):
            if bpy_obj.type == 'LIGHT':
                # Instantiate a Light Object Plugin for this Object
                self.instantiate(Light, bpy_obj)

    def register():
        Coherence.api.register_plugin(LightsPlugin)

    def unregister():
        Coherence.api.unregister_plugin(LightsPlugin)

The above example will call :py:meth:`.GlobalPlugin.instantiate` to create a copy of the Light class onto every object in the scene of type ``LIGHT``. If lights were previously not synced to Unity, this will now add them as individual GameObjects and keep their transformations synced between applications.

To make use of the new Object Plugin from Blender, we want to create a matching Object Plugin in Unity:

.. code-block:: C#

    using UnityEngine;
    using Coherence;

    [ObjectPlugin("Light", Plugin = "LightsPlugin")]
    public class BlenderLight : MonoBehaviour, IObjectPlugin
    {
        /// Standard Unity OnEnable called when attached to a GameObject
        private void OnEnable()
        {
            Debug.Log("Added Light Object Plugin!");
        }

        /// Standard Unity OnDisable called when removing from a GameObject
        private void OnDisable()
        {
            Debug.Log("Removed Light Object Plugin!");
        }
    }

Object Plugins in Unity are MonoBehaviours that get automatically added to the GameObject synced with Blender's :py:class:`bpy.types.Object`.

In order for Coherence to identify your plugin you must declare both the Object Plugin name and the parent Global Plugin name in the :sphinxsharp:type:`ObjectPluginAttribute`.

Similar to Global Plugins, you must register the plugin via **Register Plugin** in the Coherence Settings window. If there is already a global plugin registered under the same name, this will simply add the Object Plugin to the already registered plugin.

.. tip::

    You do not have to add a ScriptableObject for a Global Plugin referenced by Object Plugins if you do not have any use for it. You just need a global plugin name in :sphinxsharp:type:`ObjectPluginAttribute` that matches the copy in Blender.
