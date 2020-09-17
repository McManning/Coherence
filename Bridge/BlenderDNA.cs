

// Alternatively, Python does the heavy lifting of
// per-blender-version parsing DNAMesh and then just
// hands us the ptrs to the appropriate subtypes

// This is only really needed for custom split normals.
// Via Mesh->ldata layer
// But that means we need to fill out Mesh all the way down
// to the ldata entry (which means filling out ID, etc)
// And then pull the layer from CustomData->layers
// and requiring CustomData->typemap

using System;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Channels;

/**
* DNA structures from Blender
*/

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
public struct MVert
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] co;
    
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public short[] no;

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
public struct MLoopUV
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public float[] uv;

    public int flag;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct MLoopTri
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public uint[] tri;
    
    public uint poly;
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
}
