using SharedMemory;
using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;

[Serializable]
public class InterprocessMessage
{
    public string Foo;
    public string Bar;
}


class LegacyBridge
{
    NamedPipeServerStream server;

    public LegacyBridge()
    {
        server = new NamedPipeServerStream(
            "Foo", 
            PipeDirection.InOut, 
            1, 
            PipeTransmissionMode.Message, 
            PipeOptions.Asynchronous
        );
    }

    public void Start()
    {
        try
        {
            Console.WriteLine("Waiting for connections async");
            server.BeginWaitForConnection((result) => {
                if (isStopping) return;
                    
                Console.WriteLine("Waiting for connections cb");

                lock (lockObject)
                { 
                    if (isStopping)
                    {
                        return;
                    }

                    server.EndWaitForConnection(result);
                    OnConnected();
                    Read();
                }
            }, null);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Start() exception: {e.Message}");
        }
    }

    private bool isStopping = false;
    private readonly object lockObject = new object();

    void WaitForConnectionCallback(IAsyncResult result)
    {
        if (isStopping) return;

        lock (lockObject)
        { 
            if (isStopping)
            {
                return;
            }

            server.EndWaitForConnection(result);
            OnConnected();
            Read();
        }
    }

    /// <summary>
    /// Read a message off the buffer asynchronously
    /// </summary>
    void Read()
    {
        try
        {
            var buffer = new byte[1024];
            server.BeginRead(buffer, 0, buffer.Length, (result) => {
                var totalBytes = server.EndRead(result);

                if (totalBytes == 0) // closed pipe
                {
                    OnClosedPipe();
                    return;
                }

                if (!server.IsMessageComplete)
                {
                    throw new Exception("Message is incomplete. Expected it all in one go");
                }

                // Reshape buffer into a struct and pass it off.
                Console.WriteLine(Encoding.Default.GetString(buffer));

                // Read next message
                Read();
            }, null);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Initial read exception: {e.Message}");
            throw;
        }
    }
        
    void OnClosedPipe()
    {
        Console.WriteLine("Closed pipe");

        if (isStopping) return;

        lock (lockObject)
        {
            if (!isStopping)
            {
                OnDisconnected();
                Restart();
            }
        }
    }

    void OnConnected()
    {
        Console.WriteLine("Client connected");
    }

    void OnDisconnected()
    {
        Console.WriteLine("Client disconnected");
        server.Disconnect();
        Start(); // Begin again, waiting for a new connection
    }

    public void Restart()
    {
        // TODO: Not re-create the pipe?
    }

    public void Stop()
    {
        Console.Write("Stopping server");

        isStopping = true;
        try
        {
            if (server.IsConnected)
            {
                server.Disconnect();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to disconnect server: {e.Message}");
            throw;
        }
        finally
        {
            server.Close();
            server.Dispose();
        }
    }
}
