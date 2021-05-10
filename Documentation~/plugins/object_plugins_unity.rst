
Unity API Reference
--------------------

.. default-domain:: sphinxsharp

.. type:: public class ObjectPluginAttribute

    .. property:: public string Name { get; set; }

        The name of the Blender Object Plugin (required).

    .. property:: public string Plugin { get; set; }

        Name of the parent Global Plugin that this is associated with (required).

.. type:: public interface IObjectPlugin

    Interface for a third party Object Plugin. When added to a :type:`MonoBehaviour`
    this adds extension methods for Coherence events and vertex streams.

    .. method:: public void AddHandler<T>(string id, Action<string, T> callback)
        :param(1): Unique event- ID
        :param(2): Action to execute in response to the event from Blender

        Add an event handler for when Blender sends a custom message for this object
        (e.g. through a matching Blender SceneObject plugin)

    .. method:: public void RemoveHandler<T>(string id, Action<string, T> callback)
        :param(1): Unique event- ID
        :param(2): Action to remove

        Remove a callback previously added with :meth:`AddHandler\<T\>`

    .. method:: public void RemoveAllHandlers()

        Remove all callbacks for inbound messages

    .. method:: public void SendEvent<T>(string id, T payload)
        :param(1): Unique event- ID
        :param(2): Serializable struct instance of T to send to Blender

        Send an arbitrary block of data to Blender.

        Data sent will be associated with this object on the Blender side
        through a matching SceneObject plugin kind.

    .. method:: public void AddVertexDataStream<T>(string id, Action<string, Mesh, ArrayBuffer<T>> callback)
        :param(1): Unique stream ID
        :param(2):  Callback method to execute when receiving stream updates from Blender.

        Add a callback to be executed every time vertex data is synced.

        The callback recieves the Unity :type:`Mesh` that was updated and an :type:`ArrayBuffer`
        containing the custom per-vertex data generated from Blender.

        The number of elements in the buffer match the number of vertices in the Unity :type:`Mesh`.
        When compressing loop elements down to unique vertices, the source buffer from Blender
        will also be compressed the same way to generate unique per-vertex elements.

        This is unlike :py:meth:`.SceneObject.add_custom_vertex_data_stream` where the stream
        must contain the same number of elements as there are loops in the evaluated Blender mesh.

    .. method:: public void RemoveVertexDataStream(string id)
        :param(1): Unique stream ID

        Remove a previously registered vertex data stream handler

    .. method:: void Start()

        Standard Unity :meth:`MonoBehaviour.Start` method.

        This is equivalent to :py:meth:`.SceneObject.on_create`

    .. method:: void OnDestroy()

        Standard Unity :meth:`MonoBehaviour.OnDestroy` method.

        This is equivalent to :py:meth:`.SceneObject.on_destroy`
