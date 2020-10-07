
using SharedMemory;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Data management for a Blender scene.
/// 
/// Handles serialization of the data into a format consumable by Unity.
/// </summary>
class Scene : IInteropSerializable<InteropScene>
{
    public InteropScene data;
    
    public string Name => "Scene";

    public Scene()
    {
    }

    public InteropScene Serialize()
    {
        return data;
    }

    public void Deserialize(InteropScene interopData)
    {
        throw new InvalidOperationException();
    }
}
