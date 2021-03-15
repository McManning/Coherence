
from ctypes import *
import math
from mathutils import Vector, Matrix, Quaternion

class InteropString64(Structure):
    _fields_ = [
        ('buffer', c_char * 64)
    ]

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

class InteropCamera(Structure):
    _fields_ = [
        ('width', c_int),
        ('height', c_int),
        ('isPerspective', c_bool),
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

def identity():
    mat = InteropMatrix4x4()
    mat.m00 = 1
    mat.m11 = 1
    mat.m22 = 1
    mat.m33 = 1
    return mat

def to_interop_transform(obj):
    """Extract local transformation (position, euler angles, scale) from an object.

    This will also automatically perform conversion from
    Blender's RHS Z-up space to Unity's LHS Y-up.

    Args:
        obj (bpy.types.Object): The object to extract transform from

    Returns:
        tuple of InteropVector3's representing (pos, eul, sca)
    """
    mat = Matrix(obj.matrix_world)

    pos = mat.to_translation()
    eul = mat.to_euler('ZXY')

    rot = mat.to_quaternion()

    # Scale pulled from the object not the matrix since
    # the matrix can't represent negative scaling
    sca = obj.scale

    return (
        InteropVector3(pos.x, pos.z, pos.y),
        InteropQuaternion(rot.x, rot.z, rot.y, -rot.w),
        InteropVector3(sca.x, sca.z, sca.y)
    )


def to_interop_matrix4x4(mat):
    """Convert the input matrix to an InteropMatrix4x4

    Args:
        mat (float[]):  float multi-dimensional array of 4 * 4 items in [-inf, inf]. E.g.
                        ((1.0, 0.0, 0.0, 0.0), (0.0, 1.0, 0.0, 0.0), (0.0, 0.0, 1.0, 0.0), (0.0, 0.0, 0.0, 1.0))
    Returns:
        InteropMatrix4x4
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

    Parameters:
        vec (mathutils.Vector)

    Returns:
    """
    return InteropVector3(vec[0], vec[1], vec[2])

def to_interop_vector2(vec):
    """Convert a Blender Vector to an interop type for C#

    Parameters:
        vec (mathutils.Vector)

    Returns:
    """
    result = InteropVector2()
    result.x = vec[0]
    result.y = vec[1]

    return result

def to_interop_int_array(arr):
    """Convert the array of ints to an interop type for C# int[]

    Parameters:
        arr (int[])

    Returns:
        c_int*
    """
    result = (c_int*len(arr))()
    for i in range(len(arr)):
        result[i] = arr[i]

    return result
