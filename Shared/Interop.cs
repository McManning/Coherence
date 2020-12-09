///
/// Shared structures between Unity and the Blender plugin
///
using SharedMemory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

#if UNITY_EDITOR
using UnityEngine;
#endif

namespace Coherence
{
    public interface IInteropSerializable<T> where T : struct
    {
        string Name { get; }

        T Serialize();

        void Deserialize(T interopData);
    }

    public enum RpcRequest : byte
    {
        /// <summary>
        /// Sent by Blender the first time it connects to the shared memory space,
        /// then sent by Unity in response to Blender's Connect request.
        ///
        /// <para>
        ///     Payload: <see cref="InteropBlenderState"/> from Blender
        ///     or a <see cref="InteropUnityState"/> from Unity.
        /// </para>
        /// </summary>
        Connect = 1,

        /// <summary>
        /// Notify Blender/Unity of an expected disconnect.
        /// </summary>
        Disconnect,

        /// <summary>
        /// Get updated information about Unity's current state and settings
        ///
        /// <para>
        ///     Payload: <see cref="InteropUnityState"/>
        /// </para>
        /// </summary>
        UpdateUnityState,

        /// <summary>
        /// Get updated information about Blender's current state and settings
        ///
        /// <para>
        ///     Payload: <see cref="InteropBlenderState"/>
        /// </para>
        /// </summary>
        UpdateBlenderState,

        /// <summary>
        /// Notify Unity that a new <see cref="InteropViewport"/>
        /// has been created in Blender.
        ///
        /// <para>
        ///     Payload: <see cref="InteropViewport"/>
        /// </para>
        /// </summary>
        AddViewport,

        /// <summary>
        /// Notify Unity that a <see cref="InteropViewport"/>
        /// has been removed from Blender.
        ///
        /// <para>
        ///     Payload: <see cref="InteropViewport"/>
        /// </para>
        /// </summary>
        RemoveViewport,

        /// <summary>
        /// Notify Unity of an updated <see cref="InteropViewport"/>
        /// and visibility list.
        ///
        /// <para>
        ///     Payload: <see cref="InteropViewport"/>
        /// </para>
        /// </summary>
        UpdateViewport,

        /// <summary>
        /// Notify Unity of what object IDs are visible in a given viewport
        ///
        /// <para>
        ///     Payload: <see cref="int"/>[]
        /// </para>
        /// </summary>
        UpdateVisibleObjects,

        /// <summary>
        /// Notify Unity of an updated <see cref="InteropScene"/>.
        ///
        /// <para>
        ///     Payload: <see cref="InteropScene"/>
        /// </para>
        /// </summary>
        UpdateScene, // DEPRECATED.

        /// <summary>
        /// Notify Unity that a <see cref="InteropSceneObject"/>
        /// has been added to the scene.
        ///
        /// <para>
        ///     Payload: <see cref="InteropSceneObject"/>
        /// </para>
        /// </summary>
        AddObjectToScene,

        /// <summary>
        /// Notify Unity that a <see cref="InteropSceneObject"/>
        /// has been removed from the scene.
        ///
        /// <para>
        ///     Payload: <see cref="InteropSceneObject"/>
        /// </para>
        /// </summary>
        RemoveObjectFromScene,

        /// <summary>
        /// Notify Unity that a <see cref="InteropSceneObject"/>
        /// has been updated in the scene (new transform, metadata, etc).
        ///
        /// <para>
        ///     Payload: <see cref="InteropSceneObject"/>
        /// </para>
        /// </summary>
        UpdateSceneObject,

        /// <summary>
        /// Notify Unity that a range of object vertices have been modified.
        ///
        /// <para>
        ///     Payload: <see cref="InteropVector3"/>[]
        /// </para>
        /// </summary>
        UpdateVertices,

        /// <summary>
        /// Notify Unity that a range of object vertices have been modified.
        ///
        /// <para>
        ///     Payload: <see cref="int"/>[]
        /// </para>
        /// </summary>
        UpdateTriangles,

        /// <summary>
        /// Notify Unity that a range of vertex normals have been modified.
        ///
        /// <para>
        ///     Payload: <see cref="InteropVector3"/>[]
        /// </para>
        /// </summary>
        UpdateNormals,

        /// <summary>
        /// Notify Unity that a range of object UVs have been modified.
        ///
        /// <para>
        ///     Payload: <see cref="InteropVector2"/>[]
        /// </para>
        /// </summary>
        UpdateUVs,

        /// <summary>
        /// Notify Unity that the active material name for a
        /// <see cref="InteropSceneObject"/> has changed.
        ///
        /// <para>
        ///     Payload: Material name as <see cref="byte"/>[]
        /// </para>
        /// </summary>
        UpdateMaterial,
    }

    public enum RpcResponse : byte
    {
        /// <summary>
        /// Sync local data with an updated Blender state
        /// (camera position, objects, etc)
        /// </summary>
        BlenderState = 1
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropMessageHeader
    {
        public RpcRequest type;

        /// <summary>
        /// Array index if this is an array of items
        /// </summary>
        public int index;

        /// <summary>
        /// Number of elements in the array, if an array of data
        /// </summary>
        public int count;

        public override int GetHashCode()
        {
            return base.GetHashCode(); // TODO: Impl.
        }

        public override bool Equals(object obj)
        {
            return obj is InteropMessageHeader o
                && o.type == type
                && o.index == index
                && o.count == count;
        }
    }

    /// <summary>
    /// Settings set by Unity and sent to Blender
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropUnityState
    {
        // [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        // public string version;

        /// <summary>
        /// Plugin version used by Unity
        /// </summary>
        public int version;

    }

    /// <summary>
    /// Misc information about Blender's current state (settings, version, etc).
    ///
    /// This message is sent to Unity on first connect and periodically thereafter.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropBlenderState
    {
        /// <summary>
        /// Blender version information
        /// </summary>
        public InteropString64 version;
    }

    /// <summary>
    /// State information for a Blender viewport.
    ///
    /// Each active viewport in Blender using the RenderEngine may
    /// send its own BlenderState. Scene objects are shared between
    /// all viewports but visibility may change between viewports.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropViewport
    {
        /// <summary>
        /// Unique identifier for this viewport.
        ///
        /// Slightly redundant for name, but used for the pixels
        /// consumer/producer to easily map pixel data to a viewport.
        /// </summary>
        public int id;

        /// <summary>
        /// Metadata about the viewport camera
        /// </summary>
        public InteropCamera camera;
    }

    /// <summary>
    /// Current viewport camera state (transform, matrices, etc)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropCamera
    {
        /// <summary>Viewport width in pixels</summary>
        public int width;

        /// <summary>Viewport height in pixels</summary>
        public int height;

        public float lens;

        public InteropVector3 position;
        public InteropVector3 forward;
        public InteropVector3 up;

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return obj is InteropCamera cam
                && cam.width == width
                && cam.height == height
                && cam.lens == lens
                && cam.position.Approx(position)
                && cam.forward.Approx(forward)
                && cam.up.Approx(up);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropMatrix4x4
    {
        // Layout matches UnityEngine.Matrix4x4... maybe?
        // This might be better to organize our way and
        // manually map to a unity Matrix4x4 on the other side.
        public float m00;
        public float m33;
        public float m23;
        public float m13;
        public float m03;
        public float m32;
        public float m22;
        public float m02;
        public float m12;
        public float m21;
        public float m11;
        public float m01;
        public float m30;
        public float m20;
        public float m10;
        public float m31;
    }

    /// <summary>
    /// Blender scene metadata
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropScene // DEPRECATED
    {
        /// <summary>
        /// Number of BlenderObject instances in the scene
        /// </summary>
        public int objectCount;
    }

    public enum SceneObjectType
    {
        Mesh = 1,
        Other,
    }

    /// <summary>
    /// Fixed length (63 byte + \0) string that can be packed into an interop struct.
    ///
    /// <para>
    ///     This exists to add string support to <see cref="FastStructure" />
    ///     and safely pack strings within other structs in shared memory.
    ///
    ///     Can be implicitly cast to and from <see cref="string"/>.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct InteropString64
    {
        public fixed sbyte buffer[64];

        public InteropString64(string value)
        {
            Value = value;
        }

        /// <summary>
        /// Read or write the fixed character buffer
        /// </summary>
        public string Value
        {
            get
            {
                fixed (sbyte* s = buffer)
                {
                    return new string(s); // , 0, 64, Encoding.ASCII);
                }
            }
            set
            {
                fixed (sbyte* s = buffer)
                {
                    if (value == null)
                    {
                        *s = 0;
                        return;
                    }

                    if (value.Length > 63)
                    {
                        throw new OverflowException(
                            $"String `{value}` is too large to pack into an InteropString64"
                        );
                    }

                    int i = 0;
                    foreach (char c in value)
                    {
                        *(s + i) = (sbyte)c;
                        i++;
                        if (i >= 63) break;
                    }
                    *(s + i) = 0;
                }
            }
        }

        public override bool Equals(object obj)
        {
            return Value.Equals(obj);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }

        public static implicit operator string(InteropString64 s)
        {
            return s.Value;
        }

        public static implicit operator InteropString64(string s)
        {
            return new InteropString64(s);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropSceneObject
    {
        public InteropString64 name;

        public SceneObjectType type;
        public InteropMatrix4x4 transform;

        /// <summary>
        /// Number of <see cref="InteropVector3"/> elements in the vertex array.
        /// This will also control the size of normal/UV/weight/etc arrays.
        /// </summary>
        public int vertexCount;

        /// <summary>
        /// Number of <see cref="int"/> elements in the triangle array
        /// </summary>
        public int triangleCount;

        /// <summary>
        /// Name of the material used by this object
        /// </summary>
        public InteropString64 material;
    }

    /// <summary>
    /// Structure that matches the layout of a UnityEngine.Vector3
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropVector3
    {
        public InteropVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public float x;
        public float y;
        public float z;

        public static InteropVector3 operator +(InteropVector3 a, InteropVector3 b)
        {
            return new InteropVector3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        internal bool Approx(InteropVector3 v)
        {
            var epsilon = 1e-6f;

            return Math.Abs(x - v.x) < epsilon
                && Math.Abs(y - v.y) < epsilon
                && Math.Abs(z - v.z) < epsilon;
        }

        public override string ToString()
        {
            return $"({x}, {y}, {z})";
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return obj is InteropVector3 vector && Approx(vector);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropVector2
    {
        public float x;
        public float y;

        public InteropVector2(float[] xy)
        {
            this.x = xy[0];
            this.y = xy[1];
        }

        internal bool Approx(InteropVector2 v)
        {
            var epsilon = 1e-6f;

            return Math.Abs(x - v.x) < epsilon
                && Math.Abs(y - v.y) < epsilon;
        }

        public override string ToString()
        {
            return $"({x}, {y})";
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return obj is InteropVector2 vector && Approx(vector);
        }
    }

    /// <summary>
    /// Structure that matches the layout of a UnityEngine.BoneWeight
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropBoneWeight
    {
        public float weight0;
        public float weight1;
        public float weight2;
        public float weight3;

        public int boneIndex0;
        public int boneIndex1;
        public int boneIndex2;
        public int boneIndex3;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropColor24
    {
        public byte r;
        public byte g;
        public byte b;
    }

    /// <summary>
    /// Struct prepending raw render data from Unity
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropRenderHeader
    {
        public int viewportId;
        public int width;
        public int height;

        // TODO: Format stuff would go here. (RGBA/ARGB/32/F/etc)
        // For now we assume it's just InteropColor24[]
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RenderTextureData // TODO: InteropViewportTexture?
    {
        public static RenderTextureData Invalid = new RenderTextureData
        {
            viewportId = -1,
            width = 0,
            height = 0,
            pixels = IntPtr.Zero
        };

        public int viewportId;
        public int width;
        public int height;
        public int frame;

        public IntPtr pixels;
    }

    /*
    public static class InteropUtility
    {
        public unsafe static void ToFixedBuffer(this string str, sbyte* s, int len)
        {
            int i = 0;
            foreach (char c in str)
            {
                *(s + i) = (sbyte)c;
                i++;
                if (i >= len - 1) break;
            }
            *(s + i) = 0;
        }
    }
    */

    public static class InteropLogger
    {
        [Conditional("DEBUG")]
        public static void Debug(string message)
        {
        #if UNITY_EDITOR
            UnityEngine.Debug.Log(message);
        #else
            Console.WriteLine(message);
        #endif
        }

        public static void Warning(string message)
        {
        #if UNITY_EDITOR
            UnityEngine.Debug.LogWarning(message);
        #else
            Console.WriteLine("WARNING: " + message);
        #endif
        }

        public static void Error(string message)
        {
        #if UNITY_EDITOR
            UnityEngine.Debug.LogError(message);
        #else
            Console.WriteLine("ERROR: " + message);
        #endif
        }
    }

    /// <summary>
    /// Simplified verison of RpcBuffer that:
    ///
    /// A. Doesn't care about response matching
    /// B. Doesn't run in async tasks (for Unity support)
    /// C. Doesn't split messages
    /// </summary>
    public class InteropMessenger : IDisposable
    {
        CircularBuffer messageProducer;
        CircularBuffer messageConsumer;

        internal class InteropMessage
        {
            internal string target;
            internal InteropMessageHeader header;
            internal byte[] payload;
            internal Func<string, InteropMessageHeader, IntPtr, int> producer;
        }

        Queue<InteropMessage> outboundQueue;

        public void ConnectAsMaster(string consumerId, string producerId, int nodeCount, int nodeBufferSize)
        {
            messageProducer = new CircularBuffer(producerId, nodeCount, nodeBufferSize);
            messageConsumer = new CircularBuffer(consumerId, nodeCount, nodeBufferSize);
            outboundQueue = new Queue<InteropMessage>();
        }

        public void ConnectAsSlave(string consumerId, string producerId)
        {
            messageProducer = new CircularBuffer(producerId);
            messageConsumer = new CircularBuffer(consumerId);
            outboundQueue = new Queue<InteropMessage>();
        }

        public void Dispose()
        {
            outboundQueue?.Clear();

            messageProducer?.Dispose();
            messageProducer = null;

            messageConsumer?.Dispose();
            messageConsumer = null;
        }

        /// <summary>
        /// Queue an outbound message containing a <typeparamref name="T"/> payload
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="target"></param>
        /// <param name="type"></param>
        /// <param name="data"></param>
        public void Queue<T>(RpcRequest type, string target, ref T data) where T : struct
        {
            outboundQueue.Enqueue(new InteropMessage
            {
                target = target,
                header = new InteropMessageHeader {
                    type = type,
                    index = 0,
                    count = 0
                },
                payload = FastStructure.ToBytes(ref data)
            });
        }

        public bool ReplaceOrQueue<T>(RpcRequest type, string target, ref T data) where T : struct
        {
            var header = new InteropMessageHeader {
                type = type,
                index = 0,
                count = 0
            };

            var payload = FastStructure.ToBytes(ref data);

            // If it's already queued, replace the payload
            var queued = FindQueuedMessage(target, ref header);
            if (queued != null)
            {
                queued.payload = payload;
                return true;
            }

            outboundQueue.Enqueue(new InteropMessage
            {
                target = target,
                header = header,
                payload = payload
            });

            return false;
        }

        private InteropMessage FindQueuedMessage(string target, ref InteropMessageHeader header)
        {
            foreach (var message in outboundQueue)
            {
                if (message.target == target && message.header.Equals(header))
                {
                    return message;
                }
            }

            return null;
        }

        /// <summary>
        /// Queue an outbound message containing one or more <typeparamref name="T"/> values.
        ///
        /// <para>
        ///     If we cannot fit the entire dataset into a single message, and
        ///     <paramref name="allowSplitMessages"/> is true then the payload will
        ///     be split into multiple messages, each with a distinct
        ///     <see cref="InteropMessageHeader.index"/> and <see cref="InteropMessageHeader.count"/>
        ///     range.
        /// </para>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="type"></param>
        /// <param name="target"></param>
        /// <param name="data"></param>
        /// <param name="allowSplitMessages"></param>
        public bool ReplaceOrQueueArray<T>(RpcRequest type, string target, T[] data, bool allowSplitMessages) where T : struct
        {
            var headerSize = FastStructure.SizeOf<InteropMessageHeader>();
            var elementSize = FastStructure.SizeOf<T>();

            // TODO: Splitting. Right now assume fit or fail.
            if (headerSize + elementSize * data.Length > messageProducer.NodeBufferSize)
            {
                throw new Exception($"Cannot queue {data.Length} elements of {typeof(T)} - will not fit in a single message");
            }

            var header = new InteropMessageHeader {
                type = type,
                index = 0,
                count = data.Length
            };

            // TODO: If the source array size changes - find queued won't be correct.

            // We assume ReplaceOrQueue because of the below TODO - multiple queued arrays
            // would be pointing to the same data anyway.

            // If it's already queued, we don't need to do anything.
            var queued = FindQueuedMessage(target, ref header);
            if (queued != null)
            {
                return true;
            }

            outboundQueue.Enqueue(new InteropMessage
            {
                target = target,
                header = header,
                producer = (tar, hdr, ptr) => {
                    if (hdr.count < 1 || hdr.index + hdr.count > data.Length)
                    {
                        throw new Exception($"Producer out of range of dataset - {hdr.type} - {tar}");
                    }
                    // TODO: My concern here would be what happens if the buffer changes before this is sent?
                    // This would send the updated buffer - BUT that probably wouldn't be a problem because
                    // we're trying to send the most recent data at all times anyway, right?
                    // Even if it's sitting in queue for a while.

                    // Also seems like this should be an implicit QueueOrReplace - because if multiple
                    // queued messsages point to the same array - they're going to send the same array data.

                    // Could leave this up to the QueueArray caller - passing in this Func<...>
                    // and we're just responsible for adjusting the header to the ranges that fit.
                    FastStructure.WriteArray(ptr, data, hdr.index, hdr.count);
                    return elementSize * hdr.count;
                }
            });

            return false;
        }

        /*
        private void Queue(InteropMessageHeader header, byte[] payload)
        {
            outboundQueue.Enqueue(new InteropMessage
            {
                target = "",
                header = header,
                payload = payload,
                producer = null
            });
        }

        private void Queue<T>(InteropMessageHeader header, ref T data) where T : struct
        {
            Queue(header, FastStructure.ToBytes(ref data));
        }

        private void Queue(InteropMessageHeader header, Func<string, InteropMessageHeader, IntPtr, int> producer)
        {
            outboundQueue.Enqueue(new InteropMessage
            {
                target = "",
                header = header,
                payload = null,
                producer = producer
            });
        }

        /// <summary>
        /// Queue a new message or replace one with a matching header (via .Equals)
        /// </summary>
        /// <param name="header"></param>
        /// <param name="payload"></param>
        private void QueueOrReplace(InteropMessageHeader header, byte[] payload)
        {
            var queued = FindQueuedMessage(header);
            if (queued != null)
            {
                queued.header = header;
                queued.payload = payload;
                queued.producer = null;
                return;
            }

            Queue(header, payload);
        }

        private void QueueOrReplace(InteropMessageHeader header, Func<string, InteropMessageHeader, IntPtr, int> producer)
        {
            var queued = FindQueuedMessage(header);
            if (queued != null)
            {
                queued.header = header;
                queued.payload = null;
                queued.producer = producer;
                return;
            }

            Queue(header, producer);
        }
        */

        /// <summary>
        /// Read from the queue into the consumer callable.
        ///
        /// <paramref name="consumer"/> is expected to return the number of bytes
        /// consumed, sans the header.
        /// </summary>
        /// <param name="consumer"></param>
        public void Read(Func<string, InteropMessageHeader, IntPtr, int> consumer)
        {
            int finalBytesRead = messageConsumer.Read((ptr) =>
            {
                int bytesRead = 0;

                // Read target name (varying length string)
                int targetSize = FastStructure.PtrToStructure<int>(ptr + bytesRead);
                bytesRead += FastStructure.SizeOf<int>();

                string targetName = "";
                if (targetSize > 0)
                {
                    byte[] target = new byte[targetSize];

                    FastStructure.ReadBytes(target, ptr + bytesRead, 0, targetSize);
                    targetName = Encoding.UTF8.GetString(target);
                    bytesRead += targetSize;
                }

                // Read message header
                var headerSize = FastStructure.SizeOf<InteropMessageHeader>();
                var header = FastStructure.PtrToStructure<InteropMessageHeader>(ptr + bytesRead);
                bytesRead += headerSize;

                // Call consumer to handle the rest of the payload
                bytesRead += consumer(targetName, header, ptr + bytesRead);

                // InteropLogger.Debug($"Consume {bytesRead} bytes - {header.type} for `{targetName}`");

                return bytesRead;
            }, 5);
        }

        /// <summary>
        /// Write a disconnect message immediately into the buffer, if possible
        /// </summary>
        public void WriteDisconnect()
        {
            // Wait a good amount of time before sending a disconnect to
            // give us a better chance at firing it off.
            int bytesWritten = messageProducer.Write((ptr) =>
            {
                var message = new InteropMessage()
                {
                    target = "",
                    header = new InteropMessageHeader
                    {
                        type = RpcRequest.Disconnect,
                        index = 0,
                        count = 0
                    },
                    payload = null,
                    producer = null
                };

                return WriteMessage(message, ptr);
            }, 1000);
        }

        /// <summary>
        /// Write <paramref name="message"/> into the next available
        /// node's buffer at <paramref name="ptr"/>.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ptr"></param>
        /// <returns></returns>
        private int WriteMessage(InteropMessage message, IntPtr ptr)
        {
            int bytesWritten = 0;

            // Write the target name (varying length string)
            byte[] target = Encoding.UTF8.GetBytes(message.target);
            int targetLen = target.Length;
            FastStructure.StructureToPtr(ref targetLen, ptr + bytesWritten);
            bytesWritten += FastStructure.SizeOf<int>();

            if (targetLen > 0)
            {
                FastStructure.WriteBytes(ptr + bytesWritten, target, 0, targetLen);
                bytesWritten += targetLen;
            }

            // Write the message header
            var headerSize = FastStructure.SizeOf<InteropMessageHeader>();
            var header = message.header;

            FastStructure.StructureToPtr(ref header, ptr + bytesWritten);
            bytesWritten += headerSize;

            // If there's a custom producer, execute it for writing the payload
            if (message.producer != null)
            {
                bytesWritten += message.producer(message.target, header, ptr + bytesWritten);
            }

            // If there's a payload included with the message, copy it
            if (message.payload != null)
            {
                FastStructure.WriteBytes(ptr + bytesWritten, message.payload, 0, message.payload.Length);
                bytesWritten += message.payload.Length;
            }

            // InteropLogger.Debug($"Produce {bytesWritten} bytes - {header.type} for `{message.target}`");

            return bytesWritten;
        }

        /// <summary>
        /// Process queued messages and write if possible
        /// </summary>
        public void ProcessOutboundQueue()
        {
            if (outboundQueue.Count() < 1)
            {
                return;
            }

            if (outboundQueue.Count() > 10)
            {
                InteropLogger.Warning($"Outbound queue is at {outboundQueue.Count()} messages");
            }

            // Only dequeue a message once we have an available node for writing
            int bytesWritten = messageProducer.Write((ptr) =>
            {
                var next = outboundQueue.Dequeue();
                return WriteMessage(next, ptr);
            }, 5);
        }

        internal void ClearQueue()
        {
            outboundQueue.Clear();
        }
    }
}
