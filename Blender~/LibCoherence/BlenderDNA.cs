﻿
using System;
using System.Runtime.InteropServices;
using SharpRNA;

namespace Coherence.BlenderDNA
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MVert
    {
        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        //public float[] co;
        public float co_x;
        public float co_y;
        public float co_z;

        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        //public short[] no;
        public short no_x;
        public short no_y;
        public short no_z;

        public char flag;
        public char bweight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MLoop
    {
        public uint v;
        public uint e;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MLoopUV : IInteropConvertible<InteropVector2>
    {
        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        //public float[] uv;
        public float u;
        public float v;

        public int flag;

        public InteropVector2 ToInterop()
        {
            return new InteropVector2(u, v);
        }

        public bool Equals(InteropVector2 vec)
        {
            return u == vec.x
                && v == vec.y;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MLoopTri
    {
        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        //public uint[] tri;
        public uint tri_0;
        public uint tri_1;
        public uint tri_2;

        public uint poly;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct MLoopCol : IInteropConvertible<InteropColor32>
    {
        public byte r, g, b, a;

        public InteropColor32 ToInterop()
        {
            return new InteropColor32(r, g, b, a);
        }

        public bool Equals(InteropColor32 col)
        {
            return r == col.r
                && g == col.g
                && b == col.b
                && a == col.a;
        }
    }

    [DNA("CustomDataLayer")]
    public struct CustomDataLayer
    {
        public const int CD_NORMAL = 8;

        /// <summary>Type of data in this layer</summary>
        [DNA("type")]
        public int type;

        /// <summary>Number of the active layer of this type</summary>
        [DNA("active")]
        public int active;

        /// <summary>Layer name</summary>
        [DNA("name")]
        public SharpRNA.NativeArray<byte> name; // TODO: string RNA

        /// <summary>
        /// Layer data.
        ///
        /// This is intended to be reinterpreted to a new data size / type
        /// once the format of what's stored in this layer is known
        /// (e.g. vec3s, floats, etc)
        /// </summary>
        [DNA("data")]
        public SharpRNA.NativeArray<byte> data;
    }

    [DNA("CustomData")]
    public struct CustomData
    {
        /// <summary>
        /// Mapping of types to indices of the first layer of that type.
        /// </summary>
        [DNA("typemap")]
        public SharpRNA.NativeArray<int> typemap;

        /// <summary>
        /// Custom data layers ordered by type
        /// </summary>
        [DNA("layers", SizeField = "totlayer")]
        public SharpRNA.NativeArray<CustomDataLayer> layers;

        /// <summary>
        /// Find an index within <see cref="layers"/> that is of the given <paramref name="type"/> and active.
        /// </summary>
        /// <param name="type"></param>
        /// <returns>-1 if not found, otherwise an index >= 0</returns>
        public int GetActiveLayerIndex(int type)
        {
            var index = typemap[type];
            return index >= 0 ? index + layers[index].active : -1;
        }

        /// <summary>
        /// Get the <see cref="CustomDataLayer.data"/> for an active layer of the given <paramref name="type"/>.
        /// </summary>
        /// <typeparam name="T">Type to reinterpret the data into</typeparam>
        /// <param name="type">Blender layer ID</param>
        /// <param name="count">Size of the reinterpreted NativeArray<typeparamref name="T"/>.</param>
        /// <returns></returns>
        public SharpRNA.NativeArray<T> GetActiveLayerData<T>(int type, int count) where T : struct
        {
            int index = GetActiveLayerIndex(type);
            if (index == -1)
            {
                return null;
            }

            return layers[index].data.Reinterpret<T>(count);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VertexWeight
    {
        /// <summary>Unique index for the vertex group</summary>
        public int vertexGroupIndex;

        /// <summary>Weight between 0.0 and 1.0</summary>
        public float weight;
    }

    [DNA("MDeformVert")]
    public struct VertexWeights
    {
        [DNA("dw", SizeField = "totweight")]
        public SharpRNA.NativeArray<VertexWeight> weights;
    }

    [DNA("Mesh")]
    public struct Mesh
    {
        /// <summary>
        /// Custom data layer associated with loop vertices
        /// </summary>
        [DNA("ldata")]
        public CustomData loopCustomData;

        /// <summary>
        /// Number of entries in loop data (loop vertices, loop normals, etc)
        /// </summary>
        [DNA("totloop")]
        public int loopCount;

        /// <summary>
        /// Vertex deformation weights by group ID
        /// </summary>
        [DNA("dvert", SizeField = "totvert")]
        public SharpRNA.NativeArray<VertexWeights> vertexWeights;

        public SharpRNA.NativeArray<InteropVector3> GetCustomNormals()
        {
            return loopCustomData.GetActiveLayerData<InteropVector3>(
                CustomDataLayer.CD_NORMAL,
                loopCount
            );
        }

        public void TestReportVertexWeights()
        {
            for (int i = 0; i < vertexWeights.Count; i++)
            {
                var weights = vertexWeights[i].weights;
                for (int j = 0; j < weights.Count; j++)
                {
                    Console.WriteLine(
                        $"v:{i} group: {weights[j].vertexGroupIndex}, weight: {weights[j].weight}"
                    );
                }
            }
        }
    }

  /*

    // This is only really needed for custom split normals.
    // Via Mesh->ldata layer
    // But that means we need to fill out Mesh all the way down
    // to the ldata entry (which means filling out ID, etc)
    // And then pull the layer from CustomData->layers
    // and requiring CustomData->typemap
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct ID
    {
        public IntPtr next;
        public IntPtr prev;
        public IntPtr newid;
        public IntPtr lib;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 66)]
        public char[] name;

        public ushort flag;

        public int tag;
        public int us;
        public int icon_id;
        public int recalc;

        public int recalc_up_to_undo_push;
        public int recalc_after_undo_push;

        public uint session_uuid;

        public IntPtr properties;
        public IntPtr override_library;
        public IntPtr orig_id;
        public IntPtr py_instance;

        // [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        //public byte[] _pad;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct CustomData
    {
        public IntPtr layers;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 47)]
        public int[] typemap;

        public int totlayer;
        public int maxlayer;

        public int totsize;

        public IntPtr pool;
        public IntPtr external;
    }


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct Mesh
    {
        public ID id;

        // Animation data
        public IntPtr adt;

        // Old animation system, deprecated for 2.5.
        public IntPtr ipo;
        public IntPtr key;
        public IntPtr mat;
        public IntPtr mselect;

        // BMESH ONLY
        public IntPtr mpoly;
        public IntPtr mloop;
        public IntPtr mloopuv;
        public IntPtr mloopcol;
        // END BMESH ONLY

        // Legacy face storage (quads & tries only),
        // faces are now stored in Mesh.mpoly & Mesh.mloop arrays.
        public IntPtr mface;
        public IntPtr mtface;
        public IntPtr tface;
        public IntPtr mvert;
        public IntPtr medge;
        public IntPtr dvert;

        // Array of colors for tessellated faces, must be number of
        // tessellated faces * 4 in length
        public IntPtr mcol;
        public IntPtr texcomesh;

        public IntPtr edit_mesh;

        public CustomData vdata;
        public CustomData edata;
        public CustomData fdata;

        // BMESH ONLY
        public CustomData pdata;
        public CustomData ldata;
        // END BMESH ONLY

        public int totvert;
        public int totedge;
        public int totface;
        public int totselect;

        // BMESH ONLY
        public int totpoly;
        public int totloop;
        // END BMESH ONLY

        // ... etc. Not listed as we don't access it.
    }*/
}
