
from ctypes import create_string_buffer

__string_buffer_cache = dict()

def get_string_buffer(value: str):
    """Use a cache to reuse C string buffers wherever possible"""
    global __string_buffer_cache

    if value is None:
        return None

    # TODO: Is this any faster than just create_string_buffer?
    # timeit plz.
    try:
        return __string_buffer_cache[value]
    except KeyError:
        buf = create_string_buffer(value.encode())
        __string_buffer_cache[value] = buf
        return buf

# def generate_unique_id():
#     """Create a unique Uint32 bridge ID for the object"""
#     # TODO: collision handling and all that.
#     #id = time.time() * 1000000 - random.random()
#     id = int.from_bytes(os.urandom(2), byteorder='big')
#     return id

# def is_supported_object(obj):
#     """Test if the given object can be sent to the bridge

#     Args:
#         obj (bpy.types.Object)

#     Returns:
#         boolean: True if supported
#     """

#     # TODO: Meta wouldn't work here - we need *one* meta object representation
#     # not per-meta.

#     # Anything that can be evaluated to a mesh representation after modifiers.
#     # META is excluded here - as they are dealt with separately.
#     return obj.type in {'MESH', 'CURVE', 'SURFACE', 'FONT' }

# def is_renamed(obj):
#     """Test if the given object has been renamed at some point.
#     The first call to this (or apply_rename) will always be False.

#     This will constantly return true until apply_rename()
#     is called on the object.

#     Args:
#         obj (bpy.types.Object)

#     Returns:
#         boolean: True if it has been renamed.
#     """
#     # It's difficult to identify renamed vs copied in Blender,
#     # so we instead track both the memory address + name together
#     # to check for a change. If the address changes alongside the name,
#     # then it was probably copied. If it has the same address and a new
#     # name, then it was renamed.
#     try:
#         return obj['prev_name'] != obj.name and obj['prev_ptr'] == obj.as_pointer()
#     except KeyError:
#         # Haven't queried yet, so assume false?
#         # This is awful.
#         apply_rename(obj)
#         return False

# def apply_rename(obj):
#     """Apply a rename to an object so that is_renamed() no longer returns true.

#     Args:
#         obj (bpy.types.Object)
#     """
#     obj['prev_name'] = obj.name
#     obj['prev_ptr'] = obj.as_pointer()

# def get_objects_with_material(mat):
#     """Aggregation for objects with a material reference

#     Args:
#         mat (bpy.types.Material)

#     Returns:
#         set(bpy.types.Object)
#     """
#     results = set()
#     for obj in bpy.context.scene.objects: # bpy.data.scenes[0]
#         for slot in obj.material_slots:
#             if slot.material == mat:
#                 results.add(obj)

#     return results

# def get_material_uid(mat) -> str:
#     """Retrieve the unique name of a Material for Unity.

#     If the provided mat is None, a default will be returned.

#     Args:
#         mat (bpy.types.Material|None)

#     Returns:
#         string
#     """
#     if not mat:
#         return 'Default'

#     return mat.name

# def get_object_uid(obj) -> int:
#     """Retrieve a unique identifier that exists throughout the lifetime of an object

#     Args:
#         obj (bpy.types.Object)
#     """
#     # TODO: Improve on this. I can't guarantee that Blender
#     # won't reallocate an instance to somewhere else. But we can't
#     # store a UID on an IntProperty or the object dictionary because
#     # it'll be copied with the object. Nor can we use the name,
#     # because a renamed object will just be a new object.
#     return obj.as_pointer() & 0xffffffff

# def get_mesh_uid(obj) -> str:
#     """Retrieve a unique identifier for the mesh attached to the object

#     If the object has modifiers applied - this will be unique for
#     that object. Otherwise - this may be a common mesh name that
#     is instanced between multiple objects in the scene.

#     Args:
#         obj (bpy.types.Object)

#     Returns:
#         string
#     """
#     if obj.type != 'MESH':
#         return None

#     has_modifiers = len(obj.modifiers) > 0

#     # If there are no modifiers - return the unique mesh name.
#     # This mesh may be instanced between multiple objects.
#     if not has_modifiers:
#         return obj.data.name

#     # If there are modifiers - we need to generate a unique
#     # name for this object + mesh combination.

#     # TODO: Better convention here. If this is > 63 characters
#     # it won't transfer. And this can still have a collision:
#     # Mesh named `foo__bar` can collide with a `foo` object
#     # with a `bar` mesh + modifiers.
#     return '{}__{}'.format(obj.name, obj.data.name)

def log(msg):
    print(msg, flush = True)

def debug(msg):
    print(msg, flush = True)
    pass

def error(msg, exception = None):
    print('ERROR: ' + msg, flush = True)
    if exception:
        print(exception)

def warning(msg):
    print('WARNING: ' + msg, flush = True)


class EventObservers:
    """Basic observer list for a set of event callbacks"""
    _observers: list

    def __init__(self):
        self._observers = []

    def append(self, callback):
        self._observers.append(callback)

    def remove(self, callback):
        self._observers.remove(callback)

    def dispatch(self, *event):
        for observer in self._observers:
            try:
                observer(*event)
            except:
                # TODO: Error handling for failed handlers that don't break others.
                pass

class PluginMessageHandler:
    """Entity that can send and receive :class:`.InteropPluginMessage`

    Example:
        ::

            class MyObj(PluginMessageHandler):
                def __init__(self):
                    self.add_handler('Foo', self.on_foo)

                def on_foo(self, id: str, size: int, data: c_void_p):
                    ...
    """

    #: dict[str, set]: Message ID mapped to a set of callback methods
    _handlers: dict

    def add_handler(self, id: str, callback):
        """
        Add an event handler for when Unity sends a custom message
        for this object (e.g. through a associated Unity plugin)

        Args:
            id (str):   Unique message ID
            callback:   Callback to execute when receiving the message
        """
        if not self._handlers:
            self._handlers = {}

        handlers = self._handlers.get(id, set())
        self._handlers.add(callback)
        self._handlers[id] = handlers

    def remove_handler(self, id: str, callback):
        """Remove a callback previously added with :meth:`add_handler()`

        Args:
            id (str):   Unique message ID
            callback:   Callback to execute when receiving the message

        Raises:
            KeyError:   If the handler was not registered
        """
        self._handlers[id].remove(callback)

    def remove_all_handlers(self):
        """Remove all callbacks for inbound messages"""
        self._handlers = {}

    def send_event(self, id: str, size: int, data):
        """Send an arbitrary block of data to Unity.

        Data sent will be associated with this object on the Unity side.

        Args:
            id (str):           Unique message ID
            size (int):         Size of the payload to send
            data (c_void_p):    Payload to send

        Returns:
            int: non-zero on failure
        """
        raise NotImplementedError

    def _dispatch(self, message):
        """Dispatch a message to all listeners

        Args:
            message (:class:`.InteropPluginMessage`)

        Raises:
            KeyError: If no handler is registered for inbound message ID
        """
        self._handlers[message.id].dispatch(
            message.id,
            message.size,
            message.data
        )
