
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

    public Dictionary<int, SceneObject> Objects { get; private set; }

    public Scene()
    {
        Objects = new Dictionary<int, SceneObject>();
    }

    public bool HasObject(int id)
    {
        return Objects.ContainsKey(id);
    }

    public SceneObject GetObject(int id)
    {
        if (!Objects.ContainsKey(id))
        {
            throw new Exception($"Object {id} does not exist in the scene");
        }

        return Objects[id];
    }

    public void AddObject(SceneObject obj)
    {
        if (Objects.ContainsKey(obj.data.id))
        {
            throw new Exception($"Object {obj.data.id} already exists in the scene");
        }

        Objects[obj.data.id] = obj;
    }

    public SceneObject RemoveObject(int id)
    {
        if (!Objects.ContainsKey(id))
        {
            throw new Exception($"Object {id} does not exist in the scene");
        }

        var removed = Objects[id];
        Objects.Remove(id);
        return removed;
    }

    public int[] GetObjectIds()
    {
        return Objects.Keys.ToArray();
    }

    /// <summary>
    /// Write the scene data to the given buffer.
    /// 
    /// It will contain a BlenderScene followed by an array
    /// of BlenderObject entries for each object in the scene.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="startIndex"></param>
    public void ToBytes(byte[] buffer, int startIndex)
    {
        data.objectCount = Objects.Count;

        var index = startIndex;

        FastStructure.CopyTo(ref data, buffer, index);
        index += FastStructure.SizeOf<InteropScene>();

        var objectSize = FastStructure.SizeOf<InteropSceneObject>();
        foreach (var obj in Objects)
        {
            var data = obj.Value.data;
            FastStructure.CopyTo(ref data, buffer, index);
            index += objectSize;
        }
    }

    /// <summary>
    /// Get the number of bytes required for ToBytes()
    /// </summary>
    /// <returns></returns>
    public int SizeOf()
    {
        var stateSize = FastStructure.SizeOf<InteropScene>();
        var objectSize = FastStructure.SizeOf<InteropSceneObject>();

        return stateSize + objectSize * Objects.Count;
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
