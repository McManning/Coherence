
using System;
using System.Runtime.InteropServices;

public class MockMesh
{
    public InteropVector3[] vertices;
    public uint[] triangles;
    public InteropVector2[] uv;
    public InteropVector2[] uv2;
    // uv3 ... uv8 are limits on Unity's side

    public MockMesh(int triangleCount)
    {
        // cubecount is an estimate on triangle count
        // just to make it look less.. boring?
        int cubeCount = triangleCount / 12;

        vertices = new InteropVector3[cubeCount * 8];
        triangles = new uint[cubeCount * 12 * 3];

        var rand = new Random();

        for (int c = 0; c < cubeCount; c++)
        { 
            var offset = new InteropVector3(
                ((float)rand.NextDouble() - 0.5f) * (cubeCount * 0.05f),
                ((float)rand.NextDouble() - 0.5f) * (cubeCount * 0.05f),
                ((float)rand.NextDouble() - 0.5f) * (cubeCount * 0.05f)
            );

            var verts = new [] {
                new InteropVector3(0, 0, 0) + offset,
                new InteropVector3(1, 0, 0) + offset,
                new InteropVector3(1, 1, 0) + offset,
                new InteropVector3(0, 1, 0) + offset,
                new InteropVector3(0, 1, 1) + offset,
                new InteropVector3(1, 1, 1) + offset,
                new InteropVector3(1, 0, 1) + offset,
                new InteropVector3(0, 0, 1) + offset,
            };

            var i = c * 8;
            var tris = new [] { 
                i+0, i+2, i+1, //face front
                i+0, i+3, i+2,
                i+2, i+3, i+4, //face top
                i+2, i+4, i+5,
                i+1, i+2, i+5, //face right
                i+1, i+5, i+6,
                i+0, i+7, i+4, //face left
                i+0, i+4, i+3,
                i+5, i+4, i+7, //face back
                i+5, i+7, i+6,
                i+0, i+6, i+7, //face bottom
                i+0, i+1, i+6
            };

            Array.Copy(verts, 0, vertices, c * 8, verts.Length);
            Array.Copy(tris, 0, triangles, c * 12 * 3, tris.Length);
        }

        uv = new InteropVector2[vertices.Length];
    }

    /// <summary>
    /// Move the vertices around according to time t
    /// </summary>
    /// <param name="t"></param>
    internal void WiggleVertices(double t)
    {
        // Each box is moved in a different direction, but all 
        // vertices of a box (8-per) moved uniformly together
        for (int i = 0; i < vertices.Length; i += 8)
        {
            var rand = new Random(i);

            var sint = (float)Math.Sin(t * 0.05f) * (float)rand.Next(-5, 5);
            
            int axis = rand.Next(0, 3);

            for (int j = i; j < i + 8; j++)
            {
                if (axis == 0) vertices[j].x += sint;
                if (axis == 1) vertices[j].y += sint;
                if (axis == 2) vertices[j].z += sint;
            }
        }
    }
}

public class MockScene
{
    // stuff?
}


/*
static InteropMatrix4x4 IDENTITY_MATRIX = new InteropMatrix4x4
{
    m00 = 1, m01 = 0, m02 = 0, m03 = 0,
    m10 = 0, m11 = 1, m12 = 0, m13 = 0,
    m20 = 0, m21 = 0, m22 = 1, m23 = 0,
    m30 = 0, m31 = 0, m32 = 0, m33 = 1
};

/// <summary>
/// Mock runner that uses the API methods exposed to Blender
/// </summary>
/// <param name="args"></param>
static void Main(string[] args)
{
    int OBJECT_COUNT = 10;
    int TRIANGLE_COUNT = 30 * 1000;

    Console.WriteLine(
        $"Running Mock API with {OBJECT_COUNT*TRIANGLE_COUNT} triangles " +
        $"and {OBJECT_COUNT*TRIANGLE_COUNT * 3} vertices"
    );

    var objects = new MockMesh[OBJECT_COUNT];
    for (int i = 0; i < OBJECT_COUNT; i++)
    {
        objects[i] = new MockMesh(TRIANGLE_COUNT);
    }

    try
    {
        Start();

        // Load up a mock viewport
        AddViewport(1, Viewport.MAX_VIEWPORT_WIDTH, Viewport.MAX_VIEWPORT_HEIGHT);

        // SetViewportCamera(1, IDENTITY_MATRIX);

        for (int i = 0; i < OBJECT_COUNT; i++)
        {
            AddMeshObjectToScene($"Mock Mesh #{i}", i, IDENTITY_MATRIX);

            // Can't mock the DNAMesh versions, so we use these instead.
            CopyVerticesFromArray(i, objects[i].vertices);
            CopyTrianglesFromArray(i, objects[i].triangles);
        }

        bool running = true;
        double t = 0;
        while (running)
        {
            t++;

            // Close on ESC
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape)
                {
                    running = false;
                }
            }

            // Pretend we're running at 60 FPS
            Thread.Sleep(1000 / 60);

            Update();

            if (IsConnectedToUnity())
            {

                objects[0].WiggleVertices(t);
                CopyVerticesFromArray(0, objects[0].vertices);
            }

            if (IsConnectedToUnity() && false)
            {
                // Consume viewport renders from Unity each Update()
                if (HasNewRenderTexture(1))
                {
                    // var width = GetRenderWidth(1);
                    // var height = GetRenderHeight(1);
                    // var pixels = GetRenderPixelsRGB24(1);

                    // Console.WriteLine($"NEW RENDER {width} x {height} with {pixels.Length} pixels");
                }

                // Push modified mesh data to Unity each Update()
                // For mocking - we assume the mock always updates
                //CopyVerticesFromArray(1, mesh.vertices);
                //CopyTrianglesFromArray(1, mesh.triangles);
            }
        }

        Console.WriteLine("ESC on main loop");
    }
    catch (Exception e)
    {
        Console.WriteLine($"Exception while running: {e.Message}");
    }
    finally
    {
        Console.WriteLine("Shutting it all down");
        Shutdown();
    }
}*/