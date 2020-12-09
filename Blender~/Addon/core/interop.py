
from ctypes import *

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

class InteropCamera(Structure):
    _fields_ = [
        ('width', c_int),
        ('height', c_int),
        ('lens', c_float),
        ('position', InteropVector3),
        ('forward', InteropVector3),
        ('up', InteropVector3),
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

def to_interop_matrix4x4(mat):
    """Convert the input matrix to an InteropMatrix4x4

    Parameters:
        mat (float[]):  float multi-dimensional array of 4 * 4 items in [-inf, inf]. E.g.
                        ((1.0, 0.0, 0.0, 0.0), (0.0, 1.0, 0.0, 0.0), (0.0, 0.0, 1.0, 0.0), (0.0, 0.0, 0.0, 1.0))
    Returns:
        InteropMatrix4x4
    """
    result = InteropMatrix4x4()
    result.m00 = mat[0][0]
    result.m01 = mat[0][1]
    result.m02 = mat[0][2]
    result.m03 = mat[0][3]

    result.m10 = mat[1][0]
    result.m11 = mat[1][1]
    result.m12 = mat[1][2]
    result.m13 = mat[1][3]

    result.m20 = mat[2][0]
    result.m21 = mat[2][1]
    result.m22 = mat[2][2]
    result.m23 = mat[2][3]

    result.m30 = mat[3][0]
    result.m31 = mat[3][1]
    result.m32 = mat[3][2]
    result.m33 = mat[3][3]

    return result

def to_interop_vector3(vec):
    """Convert a Blender Vector to an interop type for C#

    Parameters:
        vec (mathutils.Vector)

    Returns:
    """
    result = InteropVector3()
    result.x = vec[0]
    result.y = vec[1]
    result.z = vec[2]

    return result

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
