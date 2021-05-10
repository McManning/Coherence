
Unity API Reference
--------------------

Some sort of C# class doc magic.

.. default-domain:: sphinxsharp

.. type:: public class PluginAttribute

    .. property:: public string Name { get; set; }

        The name of the Global Plugin (required).

.. type:: public interface IPlugin

    Interface for a third party global plugin. When added to a :type:`ScriptableObject`
    this exposes a number of additional methods for Coherence events.

    The "On*" event handlers are all optional and are only executed
    when declared in your :type:`ScriptableObject`.

    .. method:: public void AddHandler<T>(string id, Action<string, T> callback)
        :param(1): Unique message ID
        :param(2): Action to execute in response to the event from Blender

        Add a callback for when a global plugin on Blender's side with the
        same name sends an event.

    .. method:: public void RemoveHandler<T>(string id, Action<string, T> callback)
        :param(1): Unique message ID
        :param(2): Action to remove

        Remove a callback previously added with :meth:`AddHandler`

    .. method:: public void RemoveAllHandlers()

        Remove all callbacks for inbound messages

    .. method:: public void SendEvent<T>(string id, T payload)
        :param(1): Unique event ID
        :param(2): Serializable struct instance of T to send to Blender

        Send an arbitrary block of data to Blender.

        Data sent will be associated with this object on the Blender side
        through a matching SceneObject plugin kind.

    .. method:: void OnRegistered()

        Called when the plugin is added to the registered plugins list

        Equivalent to :py:meth:`.Plugin.on_registered`

    .. method:: void OnUnregistered()

        Called when the plugin is removed from the registered plugins list

        Equivalent to :py:meth:`.Plugin.on_unregistered`

    .. method:: void OnCoherenceEnabled()

        Called when the Coherence connection has been enabled.

        Equivalent to :py:meth:`.Plugin.on_enable`

    .. method:: void OnCoherenceDisabled()

        Called when the Coherence connection is disabled.

        Equivalent to :py:meth:`.Plugin.on_disable`

    .. method:: void OnCoherenceConnected()

        Perform any additional work after Coherence establishes a connection

        Equivalent to :py:meth:`.Plugin.on_connected`

    .. method:: void OnCoherenceDisconnected()

        Perform any cleanup after Coherence disconnects from the host.

        Equivalent to :py:meth:`.Plugin.on_disconnected`

    .. method:: void OnAddObject(GameObject obj)
        :param(1): GameObject that has been added

        Called when a new GameObject is added to Coherence from Blender.

        This is similar to :py:meth:`.Plugin.on_add_bpy_object` but only
        includes those objects that have been synced from Blender by
        one or more Object Plugins on Blender's side.

    .. method:: void OnRemoveObject(GameObject obj)

        Called when the :type:`ISceneObject` with the same plugin name
        as this plugin is removed from a GameObject.

        This is only executed on :type:`ScriptableObject` singleton plugins.

        Similar to :py:meth:`.Plugin.on_remove_bpy_object`
