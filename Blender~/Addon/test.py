"""
MVP for integrations with Python code and the Coherence DLL.

At any point, Blender can add/remove scene data to be tracked by the DLL.
At any point, Blender can add/remove viewports to be tracked by the DLL.

While not connected to Unity, we can call .Connect('sharedMemoryName') periodically until
it works and we're connected to Unity.

While connected to Unity, we can call .Update() periodically to pump the message queue
between Blender and Unity.

Tracked scene objects and viewports updated in the DLL will be synced to Unity whenever
the DLL is connected to Unity.

Once Unity disconnects - via a safe disconnect or a timeout (crash, etc) - Blender
will release shared memory and go into a not connected state.

Once .Shutdown() is called - DLL will clear all tracked objects / viewports and
disconnect from Unity if connected.

.Shutdown() (followed by a .Connect) should happen whenever we load a new scene in Blender.

"""
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

CONNECTION_NAME = create_string_buffer("Coherence".encode())
VERSION_INFO = create_string_buffer("Test Runner 0.1".encode())

def on_connected_to_shared_memory():
    print('Connected to shared memory')
    # Send initial dump of scene data to the bridge if not already

def on_connected_to_unity():
    print('Unity connected')
    # Send whatever needs to be sent AFTER initial handshake

def on_disconnected_from_unity():
    print('Unity disconnected')
    bridge.Clear()
    # Do whatever cleanup is needed

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

def event_loop():
    if bridge.IsConnectedToUnity():
        # Send typical IO between Blender and Unity
        time.sleep(1.0 / 60.0)
        bridge.Update()

        # test_consume_render_texture()

        # During an update we lost connection.
        if not bridge.IsConnectedToUnity():
            on_disconnected_from_unity()
    else:
        # Attempt to connect to shared memory if not already
        if not bridge.IsConnectedToSharedMemory():
            response = bridge.Connect(CONNECTION_NAME, VERSION_INFO)
            if response == 1:
                on_connected_to_shared_memory()
            elif response == -1:
                print('UNKNOWN ERROR WOO!')
                exit()
            else: # the space doesn't exist. Delay longer until it does.
                time.sleep(5)

        # Poll for a confirmation from Unity until we get one.
        bridge.Update()
        time.sleep(0.1)

        if bridge.IsConnectedToUnity():
            on_connected_to_unity()

def run():
    running = True
    try:
        while running:
            event_loop()

    except KeyboardInterrupt:
        print('Shutting down')
        bridge.Disconnect()

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

    run()

# Viewport camera would just be a raw ass matrix, right?
