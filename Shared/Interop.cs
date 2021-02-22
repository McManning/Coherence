﻿///
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

    public interface IInteropConvertible<T> where T : struct
    {
        T ToInterop();

        bool Equals(T interop);
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
        UpdateUV,

        /// <summary>
        /// Notify Unity that a range of object UV2s have been modified.
        ///
        /// <para>
        ///     Payload: <see cref="InteropVector2"/>[]
        /// </para>
        /// </summary>
        UpdateUV2,

        /// <summary>
        /// Notify Unity that a range of object UV3s have been modified.
        ///
        /// <para>
        ///     Payload: <see cref="InteropVector2"/>[]
        /// </para>
        /// </summary>
        UpdateUV3,

        /// <summary>
        /// Notify Unity that a range of object UV4s have been modified.
        ///
        /// <para>
        ///     Payload: <see cref="InteropVector2"/>[]
        /// </para>
        /// </summary>
        UpdateUV4,

        /// <summary>
        /// Notify Unity that a range of vertex colors have been modified.
        ///
        /// <para>
        ///     Payload: <see cref="InteropColor32"/>[]
        /// </para>
        /// </summary>
        UpdateVertexColors,

        /// <summary>
        /// Notify Unity that a range of vertices have updated bone weight counts.
        ///
        /// <para>
        ///     Payload: <see cref="byte"/>[] number of nonzero weights per vertex
        /// </para>
        /// </summary>
        UpdateBonesPerVertex,

        /// <summary>
        /// Notify Unity that a range of weights have changed.
        ///
        /// <para>
        ///     Payload: <see cref="InteropBoneWeight"/>[]
        /// </para>
        /// </summary>
        UpdateBoneWeights,

        /// <summary>
        /// Notify Unity that the active material name for a
        /// <see cref="InteropSceneObject"/> has changed.
        ///
        /// <para>
        ///     Payload: Material name as <see cref="byte"/>[]
        /// </para>
        /// </summary>
        UpdateMaterial,

        /// <summary>
        ///
        /// <para>
        ///     Payload: <see cref="InteropTexture"/>
        /// </para>
        /// </summary>
        UpdateTexture,

        /// <summary>
        ///
        /// <para>
        ///     Payload: <see cref="InteropColor32"/>[]
        /// </para>
        /// </summary>
        UpdateTextureData,
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
        /// Total number of elements in the array, if an array of items
        /// </summary>
        public int length;

        /// <summary>
        /// Number of elements in this particular message, if an array of items.
        ///
        /// This may differ from <see cref="length"/> if the array
        /// has been split into multiple smaller messages - or we
        /// are only sending deltas of an array
        /// </summary>
        public int count;

        // TODO: Some sort of "total array size" for array fragments.
        // This would let us preallocate the array to before receiving
        // multiple fragments to fill it (e.g. vertex data)

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return obj is InteropMessageHeader o
                && o.type == type
                && o.index == index
                && o.length == length
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

        public bool isPerspective;

        public float lens;
        public float viewDistance;

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
                && cam.isPerspective == isPerspective
                && cam.viewDistance == viewDistance
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
        // manually map to a Unity Matrix4x4 on the other side.
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

        public override string ToString()
        {
            return $"[{m00} {m01} {m02} {m03}\n{m10} {m11} {m12} {m13}\n{m20} {m21} {m22} {m23}\n{m30} {m31} {m32} {m33}]\n";
        }
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

    /// <summary>
    /// Type information for <see cref="InteropSceneObject"/> so Unity
    ///  knows how to represent the particular Blender object in the scene.
    /// </summary>
    public enum SceneObjectType
    {
        Mesh = 1,
        Other,
    }

    /// <summary>
    /// How to display an object within Unity - controllable from Blender.
    /// Specific render modes would override all materials for that object.
    /// </summary>
    public enum ObjectDisplayMode
    {
        /// <summary>Use Unity materials associated with the object (default)</summary>
        Material = 0,

        /// <summary>Render normal information</summary>
        Normals,

        /// <summary>Render vertex color information</summary>
        VertexColors,

        /// <summary>Checker pattern for the UV channel</summary>
        UV,

        /// <summary>Checker pattern for the UV2 channel</summary>
        UV2,

        /// <summary>Checker pattern for the UV3 channel</summary>
        UV3,

        /// <summary>Checker pattern for the UV4 channel</summary>
        UV4,
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

        /// <summary>
        /// How to display this object in Unity
        /// </summary>
        public ObjectDisplayMode display;

        /// <summary>
        /// World space transformation
        /// </summary>
        public InteropMatrix4x4 transform;

        /// <summary>
        /// Name of the material used by this object
        /// </summary>
        public InteropString64 material;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropTexture
    {
        public int width;
        public int height;
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

        public InteropVector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        internal bool Approx(InteropVector2 v)
        {
            var epsilon = 1e-6f;

            return Math.Abs(x - v.x) < epsilon
                && Math.Abs(y - v.y) < epsilon;
        }

        public override string ToString()
        {
            return $"InteropVector2({x}, {y})";
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
    /// Structure that matches the layout of a UnityEngine.Vector3
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropVector3
    {
        public float x;
        public float y;
        public float z;

        public InteropVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public InteropVector3(float[] co)
        {
            x = co[0];
            y = co[1];
            z = co[2];
        }

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
            return $"InteropVector3({x}, {y}, {z})";
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

    /// <summary>
    /// Structure that matches the layout of a UnityEngine.BoneWeight1
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropBoneWeight
    {
        public int boneIndex;
        public float weight;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropColor24
    {
        public byte r;
        public byte g;
        public byte b;
    }

    /// <summary>
    /// Floating point color that aligns with <see cref="UnityEngine.Color32"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InteropColor32
    {
        public InteropColor32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public byte r;
        public byte g;
        public byte b;
        public byte a;
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

    public delegate int InteropMessageProducer(string target, InteropMessageHeader hdr, IntPtr ptr);

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
            internal InteropMessageProducer producer;
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
            InteropLogger.Debug($"    Q-> {target}:{type:F}");
            outboundQueue.Enqueue(new InteropMessage
            {
                target = target,
                header = new InteropMessageHeader {
                    type = type,
                    index = 0,
                    length = 0,
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
                length = 0,
                count = 0
            };

            var payload = FastStructure.ToBytes(ref data);

            // If it's already queued, replace the payload
            /*var queued = FindQueuedMessage(target, ref header);
            if (queued != null)
            {
                queued.payload = payload;
                return true;
            }*/

            // Remove the old one to then queue up one at the end
            // This ensures messages that are queued up together
            // remain in their queued order.
            RemoveQueuedMessage(target, ref header);


            InteropLogger.Debug($"    ROQ-> {target}:{header.type:F}");
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

        private void RemoveQueuedMessage(string target, ref InteropMessageHeader header)
        {
            var replacement = new Queue<InteropMessage>();
            foreach (var message in outboundQueue)
            {
                if (message.target != target || !message.header.Equals(header))
                {
                    replacement.Enqueue(message);
                }
            }

            outboundQueue = replacement;
        }

        /// <summary>
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="type"></param>
        /// <param name="target"></param>
        /// <param name="buffer"></param>
        public void QueueArray<T>(RpcRequest type, string target, IArray<T> buffer) where T : struct
        {
            var headerSize = FastStructure.SizeOf<InteropMessageHeader>();
            var elementSize = FastStructure.SizeOf<T>();

            if (headerSize + elementSize * buffer.Length > messageProducer.NodeBufferSize)
            {
                throw new Exception($"Cannot queue {buffer.Length} elements of {typeof(T)} - will not fit in a single message");
            }

            // Construct a header with metadata for the array
            var header = new InteropMessageHeader {
                type = type,
                length = buffer.MaxLength,
                index = buffer.Offset,
                count = buffer.Length
            };

            // Remove any queued messages with the same outbound header
            RemoveQueuedMessage(target, ref header);

            InteropLogger.Debug($"    QA-> {target}:{type:F}");
            outboundQueue.Enqueue(new InteropMessage
            {
                target = target,
                header = header,
                producer = (tar, hdr, ptr) => {
                    buffer.CopyTo(ptr, 0, buffer.Length);
                    return elementSize * buffer.Length;
                }
            });
        }

        /// <summary>
        /// Read from the queue into the consumer callable.
        ///
        /// <paramref name="consumer"/> is expected to return the number of bytes
        /// consumed, sans the header.
        /// </summary>
        /// <param name="consumer"></param>
        public int Read(Func<string, InteropMessageHeader, IntPtr, int> consumer)
        {
            return messageConsumer.Read((ptr) =>
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
            }, 0);
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

            // Pump the queue until we fill the outbound buffer
            int bytesWritten;
            do
            {
                // Only dequeue a message once we have an available node for writing
                bytesWritten = messageProducer.Write((ptr) =>
                {
                    var next = outboundQueue.Dequeue();
                    InteropLogger.Debug($"    W-> {next.target}:{next.header.type:F}");
                    return WriteMessage(next, ptr);
                }, 0);
            } while (bytesWritten > 0 && outboundQueue.Count() > 0);
        }

        internal void ClearQueue()
        {
            outboundQueue.Clear();
        }
    }
}
