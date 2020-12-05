
import os
import time
from ctypes import *

bridge = cdll.LoadLibrary(os.path.abspath('../LibCoherence/bin/Debug/LibCoherence.dll'))

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

def to_interop_int_array(arr):
    """Convert the array of ints to an interop type for C# int[]

    Parameter:
        arr (int[])

    Returns:
        c_int*
    """
    result = (c_int*len(arr))()
    for i in range(len(arr)):
        result[i] = arr[i]

    return result

prev_frame = -1
prev_width = 0
prev_height = 0

def test_consume_render_texture():
    global prev_frame, prev_width, prev_height

    bridge.GetRenderTexture.argtype = (c_uint) # Viewport ID
    bridge.GetRenderTexture.restype = RenderTextureData

    bridge.ConsumeRenderTextures()

    rt = bridge.GetRenderTexture(1)

    if rt.width > 0 and rt.height > 0 and rt.frame > prev_frame:

        if rt.width != prev_width or rt.height != prev_height:
            print('RESIZE')
            prev_width = rt.width
            prev_height = rt.height

        print(rt.viewportId, rt.width, rt.height, rt.frame, rt.pixels, cast(rt.pixels, c_void_p).value)
        prev_frame = rt.frame
        for i in range(0, 10 * 3, 3):
            print('- {}, {}, {}'.format(rt.pixels[i], rt.pixels[i+1], rt.pixels[i+2]))

def add_mock_scene():
    # Add mesh tests
    name = create_string_buffer("Foo bar".encode())
    bridge.AddMeshObjectToScene(10, name, identity())

    name = create_string_buffer("Fizz Buzz".encode())
    bridge.AddMeshObjectToScene(100, name, identity())

    bridge.AddViewport(1, 800, 600)

    # Test for writing visible ID array
    visible_ids = [10, 100]
    visible_ids_ptr = (c_int * len(visible_ids))(*visible_ids)
    bridge.SetVisibleObjects(1, visible_ids_ptr, len(visible_ids))

    position = InteropVector3()
    position.x = 0
    position.y = 0
    position.z = 0

    # Blender is Z up
    up = InteropVector3()
    up.x = 0
    up.y = 0
    up.z = 1

    forward = InteropVector3()
    forward.x = 0
    forward.y = 1
    forward.z = 0

    bridge.SetViewportCamera.argtypes = (c_uint, InteropCamera)

    camera = InteropCamera()
    camera.width = 800
    camera.height = 600
    camera.lens = 50.0
    camera.position = position
    camera.forward = forward
    camera.up = up

    bridge.SetViewportCamera(1, camera)

def connect():
    """Run a connect retry loop until we can connect successfully to Unity"""
    conn = create_string_buffer("Coherence".encode())

    while True:
        response = bridge.Start(conn)
        if response == 0:
            print('No shared memory space - start unity first. Retrying in 3 seconds')
            time.sleep(3)
        elif response == -1:
            print('ERROR WOO!')
            exit()
        else: # Connected
            break

def run():
    running = True
    try:
        while running:
            time.sleep(1.0 / 60.0)
            bridge.Update()
            test_consume_render_texture()

            # Bad timing here. IsConnected doesn't happen until Unity responds
            # with a .Connect which means we gotta wait...
            # if not bridge.IsConnectedToUnity():
            #    print('Got a disconnect from Unity')
            #    return

    except KeyboardInterrupt:
        print('Shutting down')
        bridge.Shutdown()


def experimental():

    bridge.GetLastError.restype = c_char_p
    err = bridge.GetLastError()
    print(err)

    # result = bridge.ResizeViewport(5, 100, 200)
    # print(result)

    # err = bridge.GetLastError()
    # if err:
    #     print(err.decode())

    # bridge.GetRenderTexture.argtype = (c_uint, c_uint, c_uint)
    # bridge.GetRenderTexture.restype = RenderTextureData
    # data = bridge.GetRenderTexture(14, 8, 6)

    # print(data.viewportId, data.width, data.height, data.pixels)

    # data = bridge.GetRenderTexture(14, 8, 6)
    # print(data.viewportId, data.width, data.height, data.pixels)

    # data = bridge.GetRenderTexture(14, 100, 60)
    # print(data.viewportId, data.width, data.height, data.pixels)

    # data = bridge.GetRenderTexture(14, 100, 60)
    # print(data.viewportId, data.width, data.height, data.pixels)


if __name__ == '__main__':
    fourteen = bridge.Fourteen()
    if fourteen != 14:
        raise Exception('Could not query Bridge.Fourteen. DLL might be borked')

    print(fourteen)

    add_mock_scene()

    connect()
    run()

# Viewport camera would just be a raw ass matrix, right?
