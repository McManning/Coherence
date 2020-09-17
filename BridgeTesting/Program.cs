using System;

namespace BridgeTesting
{
    class Program
    {
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

                SetViewportCamera(1, IDENTITY_MATRIX);

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
                        // translate the object around randomly
                        /*var mat = IDENTITY_MATRIX;
                        mat.m03 = (float)Math.Sin(t);
                        

                        SetObjectTransform(0, mat);
                        */

                        objects[0].WiggleVertices(t);
                        CopyVerticesFromArray(0, objects[0].vertices);
                    }

                    if (IsConnectedToUnity() && false)
                    {
                        // Consume viewport renders from Unity each Update()
                        if (HasNewRender(1))
                        {
                            var width = GetRenderWidth(1);
                            var height = GetRenderHeight(1);
                            var pixels = GetRenderPixelsRGB24(1);

                            Console.WriteLine($"NEW RENDER {width} x {height} with {pixels.Length} pixels");
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
        }
    }
}
