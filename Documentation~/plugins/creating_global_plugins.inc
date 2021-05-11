
Creating Global Plugins
-------------------------

Create your own Blender addon that registers a class with the Coherence API:

.. code-block:: python

    bl_info = {
        'name': 'My Coherence Plugin',
        # ... your other addon settings ...
    }

    import bpy
    import Coherence

    class MyPlugin(Coherence.api.Plugin):
        def on_registered(self):
            print('Registered MyPlugin!')

        def on_unregistered(self):
            print('Unregistered MyPlugin!')

    def register():
        Coherence.api.register_plugin(MyPlugin)

    def unregister():
        Coherence.api.unregister_plugin(MyPlugin)

These are registered like any other Blender addon through Blender's addon window. While calling the typical `register()` and `unregister()` methods you will also register your custom plugin classes with Coherence's API.

In almost all cases, you will want a matching plugin created within Unity for handling communication between the two applications.

Create a ScriptableObject within Unity that implements the :sphinxsharp:type:`IPlugin` interface and uses the :sphinxsharp:type:`PluginAttribute` with a name that matches your Blender plugin's class name:

.. code-block:: c#

    using UnityEngine;
    using Coherence;

    [Plugin("MyPlugin")]
    public class MyPlugin : ScriptableObject, IPlugin
    {
        public void OnRegistered()
        {
            Debug.Log("Registered MyPlugin!");
        }

        public void OnUnregistered()
        {
            Debug.Log("Unregistered MyPlugin!");
        }
    }

After rebuilding assemblies, click the *Register Plugin* button in the Coherence Settings window to add your new plugin to the list of registered plugins.

By adding a Global Plugin, you can listen to a number of Coherence events or start communicating with your Blender addon through events API. See :doc:`sending_events` for more information.
