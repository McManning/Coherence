
from ctypes import create_string_buffer

__string_buffer_cache = dict()

def get_string_buffer(value: str):
    """Use a cache to reuse C string buffers wherever possible"""
    global __string_buffer_cache

    # TODO: Is this any faster than just create_string_buffer?
    # timeit plz.
    try:
        return __string_buffer_cache[value]
    except KeyError:
        buf = create_string_buffer(value.encode())
        __string_buffer_cache[value] = buf
        return buf

def generate_unique_id():
    """Create a unique Uint32 bridge ID for the object"""
    # TODO: collision handling and all that.
    #id = time.time() * 1000000 - random.random()
    id = int.from_bytes(os.urandom(2), byteorder='big')
    return id

def is_supported_object(obj):
    """Test if the given object can be sent to the bridge

    Parameters:
        obj (bpy.types.Object)

    Returns:
        boolean: True if supported
    """

    if obj.type == 'MESH':
        return True

    # TODO: Other supported types?

    return False

def is_renamed(obj):
    """Test if the given object has been renamed at some point.
    The first call to this (or apply_rename) will always be False.

    This will constantly return true until apply_rename()
    is called on the object.

    Parameters:
        obj (bpy.types.Object)

    Returns:
        boolean: True if it has been renamed.
    """
    # It's difficult to identify renamed vs copied in Blender,
    # so we instead track both the memory address + name together
    # to check for a change. If the address changes alongside the name,
    # then it was probably copied. If it has the same address and a new
    # name, then it was renamed.
    try:
        return obj['prev_name'] != obj.name and obj['prev_ptr'] == obj.as_pointer()
    except KeyError:
        # Haven't queried yet, so assume false?
        # This is awful.
        apply_rename(obj)
        return False

def apply_rename(obj):
    """Apply a rename to an object so that is_renamed() no longer returns true.

    Parameters:
        obj (bpy.types.Object)
    """
    obj['prev_name'] = obj.name
    obj['prev_ptr'] = obj.as_pointer()

def get_objects_with_material(mat):
    """Aggregation for objects with a material reference

    Parameters:
        mat (bpy.types.Material)

    Returns:
        set(bpy.types.Object)
    """
    results = set()
    for obj in bpy.context.scene.objects: # bpy.data.scenes[0]
        for slot in obj.material_slots:
            if slot.material == mat:
                results.add(obj)

    return results

def get_material_unity_name(mat) -> str:
    """Retrieve the name of the Material exposed to Unity.

    This can be the default material name or an override.
    If the provided mat is None, a default will be returned.

    Parameters:
        mat (bpy.types.Material|None)

    Returns:
        string
    """

    if not mat:
        return 'Default'
    elif mat.coherence.use_override_name and mat.coherence.override_name:
        return mat.coherence.override_name

    return mat.name

def get_object_uid(obj) -> int:
    """Retrieve a unique identifier that exists throughout the lifetime of an object

    Parameters:
        obj (bpy.types.Object)
    """
    # TODO: Improve on this. I can't guarantee that Blender
    # won't reallocate an instance to somewhere else. But we can't
    # store a UID on an IntProperty or the object dictionary because
    # it'll be copied with the object. Nor can we use the name,
    # because a renamed object will just be a new object.
    return obj.as_pointer() & 0xffffffff

def get_material_uid(mat) -> int:
    """Retrieve a unique identifier that exists throughout the lifetime of a material

    Parameters:
        mat (bpy.types.Material)
    """
    # TODO: Same as above
    return mat.as_pointer() & 0xffffffff

def log(msg):
    print(msg, flush = True)

def debug(msg):
    print(msg, flush = True)
    pass

def error(msg):
    print('ERROR: ' + msg, flush = True)

def warning(msg):
    print('WARNING: ' + msg, flush = True)
