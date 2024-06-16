using System.Buffers;

namespace RockEngine.Vulkan.VkBuilders
{
    public abstract class DisposableBuilder : IDisposable
    {
        protected List<MemoryHandle> _memory = new List<MemoryHandle>();

        protected bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                }

                // Free unmanaged resources (unmanaged objects) and override a finalizer below.
                // Set large fields to null.
                foreach (var item in _memory)
                {
                    item.Dispose();
                }

                _disposed = true;
            }
        }

         // TODO: переопределить метод завершения, только если "Dispose(bool disposing)" содержит код для освобождения неуправляемых ресурсов
         ~DisposableBuilder()
         {
             // Не изменяйте этот код. Разместите код очистки в методе "Dispose(bool disposing)".
             Dispose(disposing: false);
         }

        public void Dispose()
        {
            // Не изменяйте этот код. Разместите код очистки в методе "Dispose(bool disposing)".
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected MemoryHandle CreateMemoryHandle<T>(params T[] value)
        {
            var memHandle = value.AsMemory().Pin();
            _memory.Add(memHandle);
            return memHandle;
        }
    }
}
