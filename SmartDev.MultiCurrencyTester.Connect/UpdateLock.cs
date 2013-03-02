using System;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace SmartDev.MultiCurrencyTester.Connect
{
	internal class UpdateLock : IDisposable
	{
		private readonly Mutex _onWriteLockMutex;
		private readonly MemoryMappedViewAccessor _memoryMappedViewAccessor;
		private int _prevVariablesLock;

		public UpdateLock(Mutex onWriteLockMutex, MemoryMappedViewAccessor memoryMappedViewAccessor)
		{
			_onWriteLockMutex = onWriteLockMutex;
			_memoryMappedViewAccessor = memoryMappedViewAccessor;

			_onWriteLockMutex.WaitOne();
			if (_memoryMappedViewAccessor != null)
			{
				_prevVariablesLock = _memoryMappedViewAccessor.ReadInt32(Constants.VariablesLockOffset);
				_memoryMappedViewAccessor.Write(Constants.VariablesLockOffset, -1);
			}
		}

		public void Dispose()
		{
			if (_memoryMappedViewAccessor != null)
			{
				_prevVariablesLock++;
				_memoryMappedViewAccessor.Write(Constants.VariablesLockOffset, _prevVariablesLock);
			}
			_onWriteLockMutex.ReleaseMutex();
		}
	}
}

