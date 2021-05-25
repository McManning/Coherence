
##############################################
# ctypes data structures to match Interop.cs
# and DLL load/unload wrappers.
##############################################

import bpy
import os
import re
from ctypes import *
import math
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion, Color
from .utils import (
    get_string_buffer,
    log
)

# RpcMessage ID for InteropComponentMessage payloads.
RPC_COMPONENT_MESSAGE_ID = 255

class InteropString64(Structure):
    _fields_ = [
        ('buffer', c_char * 64)
    ]

    @property
    def empty(self):
        return self.buffer[0] == 0

    @property
    def value(self) -> str:
        return self.buffer.decode()

class InteropMatrix4x4(Structure):
    _fields_ = [
        ('m00', c_float),
        ('m33', c_float),
        ('m23', c_float),
        ('m13', c_float),
        ('m03', c_float),
        ('m32', c_float),
        ('m22', c_float),
        ('m02', c_float),
        ('m12', c_float),
        ('m21', c_float),
        ('m11', c_float),
        ('m01', c_float),
        ('m30', c_float),
        ('m20', c_float),
        ('m10', c_float),
        ('m31', c_float),
    ]

class InteropVector2(Structure):
    _fields_ = [
        ('x', c_float),
        ('y', c_float),
    ]

class InteropVector3(Structure):
    _fields_ = [
        ('x', c_float),
        ('y', c_float),
        ('z', c_float),
    ]

    def __init__(self, x, y, z):
        self.x = x
        self.y = y
        self.z = z

class InteropVector4(Structure):
    _fields_ = [
        ('x', c_float),
        ('y', c_float),
        ('z', c_float),
        ('w', c_float),
    ]

    def __init__(self, x, y, z, w):
        self.x = x
        self.y = y
        self.z = z
        self.w = w

class InteropQuaternion(Structure):
    _fields_ = [
        ('x', c_float),
        ('y', c_float),
        ('z', c_float),
        ('w', c_float),
    ]

    def __init__(self, x, y, z, w):
        self.x = x
        self.y = y
        self.z = z
        self.w = w

class InteropTransform(Structure):
    _fields_ = [
        ('parent', InteropString64),
        ('position', InteropVector3),
        ('rotation', InteropQuaternion),
        ('scale', InteropVector3)
    ]

class InteropCamera(Structure):
    _fields_ = [
        ('width', c_int),
        ('height', c_int),
        ('isPerspective', c_int),
        ('lens', c_float),
        ('viewDistance', c_float),
        ('position', InteropVector3),
        ('forward', InteropVector3),
        ('up', InteropVector3)
    ]

class RenderTextureData(Structure):
    _fields_ = [
        ('viewportId', c_int),
        ('width', c_int),
        ('height', c_int),
        ('frame', c_int),
        ('pixels', POINTER(c_ubyte))
    ]

# class InteropComponent(Structure):
#     _fields_ = [
#         ('name', InteropString64),
#         ('target', InteropString64),
#         ('mesh', InteropString64),
#         ('material', InteropString64),
#         ('enabled', c_bool)
#     ]

class InteropComponent(Structure):
    _fields_ = [
        ('name', InteropString64),
        ('target', InteropString64),
        ('mesh', InteropString64),
        ('material', InteropString64),
        ('enabled', c_int),
    ]

class InteropComponentMessage(Structure):
    _fields_ = [
        ('target', InteropString64),
        ('id', InteropString64),
        ('size', c_int),
        ('data', POINTER(c_byte))
    ]

class InteropProperty(Structure):
    BOOLEAN = 1
    INTEGER = 2
    FLOAT = 3
    STRING = 4
    ENUM = 5
    COLOR = 6
    FLOAT_VECTOR2 = 7
    FLOAT_VECTOR3 = 8
    FLOAT_VECTOR4 = 9

    _fields_ = [
        ('name', InteropString64),
        ('type', c_int),
        ('intValue', c_int),
        ('vectorValue', InteropVector4),
        ('stringValue', InteropString64),
    ]

    def set_value_from_bpy_property(self, value):
        """Update type and associated *Value field based on the input value

        Args:
            value (mixed):  Native property value (bool, int, Color, bpy_prop_array, etc)
                            that comes from a Blender PropertyGroup entry

        Raises:
            TypeError: If the value cannot be converted to an interop property type
        """
        if type(value) is bool:
            self.type = self.BOOLEAN
            self.intValue = c_int(value)
        elif type(value) is int:
            self.type = self.INTEGER
            self.intValue = c_int(value)
        elif type(value) is float:
            self.type = self.FLOAT
            self.vectorValue = InteropVector4(value, 0, 0, 0)
        elif type(value) is str: # enums are strings as well here.
            self.type = self.STRING
            self.stringValue = InteropString64(value.encode())
        elif type(value) is Color:
            self.type = self.COLOR
            self.vectorValue = InteropVector4(value.r, value.g, value.b, 1)
        elif type(value) is bpy.types.bpy_prop_array:
            dim = len(value)
            if dim == 2:
                self.type = self.FLOAT_VECTOR2
                self.vectorValue = InteropVector4(value[0], value[1], 0, 0)
            elif dim == 3:
                self.type = self.FLOAT_VECTOR3
                self.vectorValue = InteropVector4(value[0], value[1], value[2], 0)
            elif dim == 4:
                self.type = self.FLOAT_VECTOR4
                self.vectorValue = InteropVector4(value[0], value[1], value[2], value[3])
            else:
                raise TypeError(
                    'Cannot convert array of dimension [{}] to interop'.format(dim)
                )
        else:
            raise TypeError(
                'Cannot convert [{}] type [{}] to interop'.format(value, type(value))
            )

class InteropMessageHeader(Structure):
    _fields_ = [
        ('type', c_byte),
        ('index', c_int),
        ('length', c_int),
        ('count', c_int),
    ]

class InteropMessage(Structure):
    _fields_ = [
        ('header', InteropMessageHeader),
        ('target', InteropString64),
        ('data', POINTER(c_byte)),
    ]

    @property
    def invalid(self):
        return self.header.type < 1

    def as_component_message(self):
        """Reinterpret as InteropComponentMessage

        Returns:
            Union[:class:`.InteropComponentMessage`, None]
        """
        if self.header.type != RPC_COMPONENT_MESSAGE_ID:
            return None

        return InteropComponentMessage.from_address(self.data)

def identity():
    """Get the identity matrix

    Returns:
        :class:`.InteropMatrix4x4`
    """
    mat = InteropMatrix4x4()
    mat.m00 = 1
    mat.m11 = 1
    mat.m22 = 1
    mat.m33 = 1
    return mat

def to_interop_transform(obj):
    """Extract transformation (parent, position, euler angles, scale) from an object.

    This will also automatically perform conversion from
    Blender's RHS Z-up space to Unity's LHS Y-up.

    Args:
        obj (bpy.types.Object): The object to extract transform from

    Returns:
        :class:`.InteropTransform`
    """
    mat = Matrix(obj.matrix_world)

    pos = mat.to_translation()
    eul = mat.to_euler('ZXY')

    rot = mat.to_quaternion()

    # Scale pulled from the object not the matrix since
    # the matrix can't represent negative scaling
    sca = obj.scale

    # Get the parent name IFF it's an object.
    # TODO: Figure out how parenting to armatures and whatnot would work.
    # Ref: https://docs.blender.org/api/current/bpy.types.Object.html#bpy.types.Object.parent_type
    parent_name = ''
    if obj.parent_type == 'OBJECT' and obj.parent is not None:
        parent_name = obj.parent.name

    transform = InteropTransform()
    transform.parent = InteropString64(parent_name.encode())
    transform.position = InteropVector3(pos.x, pos.z, pos.y)
    transform.rotation = InteropQuaternion(rot.x, rot.z, rot.y, -rot.w)
    transform.scale = InteropVector3(sca.x, sca.z, sca.y)

    return transform


def to_interop_matrix4x4(mat):
    """Convert the input matrix to an InteropMatrix4x4

    Args:
        mat (float[]):  float multi-dimensional array of 4 * 4 items in [-inf, inf]. E.g.
                        ((1.0, 0.0, 0.0, 0.0), (0.0, 1.0, 0.0, 0.0), (0.0, 0.0, 1.0, 0.0), (0.0, 0.0, 0.0, 1.0))
    Returns:
        :class:`.InteropMatrix4x4`
    """
    raise NotImplementedError('Matrices not supported anymore due to laziness. Upgrade.')

    # Swap euler y/z angles
    eul = mat.to_euler()
    eul.yz = eul.zy
    rot = eul.to_matrix().to_4x4()

    # Swap translation y/z
    t = mat.to_translation()
    loc = Matrix.Translation((t.x, t.z, t.y))

    sca = mat.to_scale()
    scal.yz = sca.zy
    sca = sca.to_matrix().to_4x4()

    sca = Matrix.Scale(0.5, 4, (0.0, 0.0, 1.0))

    m = loc @ rot @ sca

    RHS_Z_TO_LHS_Y = Matrix((
        (1.0, 0.0, 0.0, 0.0),
        (0.0, 0.0, 1.0, 0.0),
        (0.0, 1.0, 0.0, 0.0),
        (0.0, 0.0, 0.0, 1.0)
    ))

    # Math is still all wrong, also, not even going to bother at this point.
    # I'd rather transfer with 3 vec3s and reconstruct on unity's side.
    # It makes space transforms / swaps a lot easier.

    # Convert Blender's RHS Z-up matrix to Unity's LHS Y-up
    #m = mat * Matrix.Rotation(math.radians(90.0), 4, 'X') # RHS_Z_TO_LHS_Y @ mat
    m = mat

    result = InteropMatrix4x4()
    result.m00 = m[0][0]
    result.m01 = m[0][1]
    result.m02 = m[0][2]
    result.m03 = m[0][3]

    result.m10 = m[1][0]
    result.m11 = m[1][1]
    result.m12 = m[1][2]
    result.m13 = m[1][3]

    result.m20 = m[2][0]
    result.m21 = m[2][1]
    result.m22 = m[2][2]
    result.m23 = m[2][3]

    result.m30 = m[3][0]
    result.m31 = m[3][1]
    result.m32 = m[3][2]
    result.m33 = m[3][3]

    # Space conversion here!

    # position is easy - rotation is not.


    return result

def to_interop_vector3(vec):
    """Convert a Blender Vector to an interop type for C#

    Args:
        vec (:class:`.mathutils.Vector`)

    Returns:
        :class:`.InteropVector3`
    """
    return InteropVector3(vec[0], vec[1], vec[2])

def to_interop_quaternion(rot):
    """Convert a Blender Quaternion to an interop type for C#

    This automatically converts rotation space to match Unity

    Args:
        rot (:class:`mathutils.Quaternion`)

    Returns:
        :class:`.InteropQuaternion`
    """
    raise NotImplementedError('Need use cases before reimplementing')
    return InteropQuaternion(rot.x, rot.z, -rot.y, rot.w)

def to_interop_vector2(vec):
    """Convert a Blender Vector to an interop type for C#

    Args:
        vec (:class:`mathutils.Vector`)

    Returns:
        :class:`.InteropVector2`
    """
    result = InteropVector2()
    result.x = vec[0]
    result.y = vec[1]

    return result

def to_interop_int_array(arr):
    """Convert the array of ints to an interop type for C# int[]

    Args:
        arr (int[])

    Returns:
        ctypes.POINTER(c_int)
    """
    result = (c_int*len(arr))()
    for i in range(len(arr)):
        result[i] = arr[i]

    return result

def to_interop_property_name(name: str) -> str:
    """Convert the input name to an interop property name.

    InteropProperty names are lowercase alphanumeric only.

    Args:
        name (str)

    Returns:
        str
    """
    return re.sub(r'[^A-Za-z0-9]', '', name).lower()

def update_transform(obj):
    """Send a transform of the given object to Coherence

    Args:
        obj (bpy.types.Object)
    """
    lib.SetObjectTransform(
        get_string_buffer(obj.name),
        to_interop_transform(obj)
    )

def load_library(path: str):
    """Load LibCoherence and typehint methods

    Args:
        path (str): Location of LibCoherence.dll

    Returns:
        ctypes.CDLL
    """
    if os.getenv('SPHINX_BUILD'):
        # Skip DLL load if building docs
        return None

    path = Path(__file__).parent.parent.joinpath(path).absolute()
    log('Loading DLL from {}'.format(path))

    lib = cdll.LoadLibrary(str(path))

    # Typehint all the API calls we actually need to typehint
    lib.Connect.restype = c_int
    lib.Disconnect.restype = c_int
    lib.Clear.restype = c_int
    lib.SetViewportCamera.argtypes = (c_int, InteropCamera)

    #self.lib.GetTextureSlots.argtypes = (
    #    POINTER(InteropString64),   # Target buffer
    #    c_int                       # size
    #)
    lib.GetTextureSlots.restype = c_int

    lib.Update.restype = InteropMessage

    lib.UpdateTexturePixels.argtypes = (
        c_void_p,   # name
        c_int,      # width
        c_int,      # height
        c_void_p    # pixels
    )
    lib.UpdateTexturePixels.restype = c_int

    lib.CopyMeshDataNative.argtypes = (
        c_void_p,   # name
        c_void_p,   # loops
        c_uint,     # loopSize
        c_void_p,   # loopTris
        c_uint,     # loopTrisSize
        c_void_p,   # verts
        c_uint,     # verticesSize
        c_void_p,   # loopCols
        c_void_p,   # loopUVs
        c_void_p,   # loopUV2s
        c_void_p,   # loopUV3s
        c_void_p,   # loopUV4s
    )
    lib.CopyMeshDataNative.restype = c_int

    lib.GetRenderTexture.argtypes = (c_uint, )
    lib.GetRenderTexture.restype = RenderTextureData

    lib.ReleaseRenderTextureLock.argtypes = (c_uint, )
    lib.ReleaseRenderTextureLock.restype = c_int

    lib.AddObjectToScene.argtypes = (
        c_void_p,           # name
        c_void_p,           # Type
        InteropTransform,   # transform
    )
    lib.AddObjectToScene.restype = c_int

    lib.SetObjectTransform.argtypes = (
        c_void_p,           # name
        InteropTransform,   # transform
    )
    lib.SetObjectTransform.restype = c_int

    lib.AddComponent.argtypes = (InteropComponent,)
    lib.UpdateComponent.argtypes = (InteropComponent,)
    lib.DestroyComponent.argtypes = (InteropComponent,)
    lib.UpdateComponentProperty.argtypes = (InteropComponent, InteropProperty)

    return lib

# def free_lib(self):
#     # Windows-specific handling for freeing the DLL.
#     # See: https://stackoverflow.com/questions/359498/how-can-i-unload-a-dll-using-ctypes-in-python
#     handle = lib._handle
#     del lib
#     lib = None

#     kernel32 = WinDLL('kernel32', use_last_error=True)
#     kernel32.FreeLibrary.argtypes = [wintypes.HMODULE]
#     kernel32.FreeLibrary(handle)

# Location of the Coherence DLL - relative to addon root
DLL_PATH = 'lib/LibCoherence.dll'

lib = load_library(DLL_PATH)
