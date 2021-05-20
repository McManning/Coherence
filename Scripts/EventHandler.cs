using System;
using SharedMemory;

namespace Coherence
{
    public interface IEventHandler
    {
        void Dispatch(string id, int size, IntPtr data);
    }

    public class EventHandler<T> : IEventHandler where T : struct
    {
        public Action<string, T> Callback { get; set; }

        public void Dispatch(string id, int size, IntPtr data)
        {
            Callback(id, FastStructure.PtrToStructure<T>(data));
        }
    }
}
