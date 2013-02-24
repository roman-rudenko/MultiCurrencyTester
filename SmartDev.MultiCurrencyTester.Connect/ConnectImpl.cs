//#define _LOGGING

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace SmartDev.MultiCurrencyTester.Connect
{
	public class ConnectImpl : IDisposable
	{
		[DllImport("kernel32.dll", SetLastError = true)]
		unsafe public static extern long InterlockedIncrement(long* lpAddend);

		[DllImport("kernel32.dll", SetLastError = true)]
		unsafe public static extern long InterlockedDecrement(long* lpAddend);

		private const int IntOffset = sizeof(int);
		private const int DoubleOffset = sizeof(double);
		private const int ByteOffset = sizeof(byte);

		private const int ReadersCountOffset = 0;
		private const int WritersCountOffset = ReadersCountOffset + IntOffset;
		private const int InstancesCountOffset = WritersCountOffset + IntOffset;
		private const int InstancesStatusesOffset = InstancesCountOffset + IntOffset;
		private int InstancesTimesOffset = -1;
		private int MemoryContractOffset = -1;

		private const long MemoryMappedFileSize = 0xA00000; // 10 megabytes
		private const string MemoryMappedFileName = "SmartDev.MultiCurrencyTester.MemoryMappedFile";
		private const string OnDataUpdatedEventName = "SmartDev.MultiCurrencyTester.OnDataUpdatedEvent";
		private const string OnReaderLockEventName = "SmartDev.MultiCurrencyTester.OnReaderLockEvent";
		private const string OnWriteLockMutexName = "SmartDev.MultiCurrencyTester.OnWriteLockMutex";

		private MemoryMappedFile _memoryMappedFile;
		private MemoryMappedViewAccessor _memoryMappedViewAccessor;
		private MemoryMappedViewStream _memoryMappedViewStream;
		private IntPtr _memoryMappedViewHandle;
		private EventWaitHandle _onDataUpdatedEvent;
		private EventWaitHandle _onReaderLockEvent;
		private Mutex _onWriteLockMutex;
		private int _instanceId = -1;
		private int _instancesCount = -1;
		private InstanceStatus _instanceStatus = InstanceStatus.Unknown;
		private int _instanceTime = -1;

		public ConnectImpl()
		{
			_onDataUpdatedEvent = new EventWaitHandle(true, EventResetMode.ManualReset, OnDataUpdatedEventName);
			_onReaderLockEvent = new EventWaitHandle(true, EventResetMode.ManualReset, OnReaderLockEventName);
			_onWriteLockMutex = new Mutex(false, OnWriteLockMutexName);

			using (GetInitLock())
			{
				try
				{
					_memoryMappedFile = MemoryMappedFile.OpenExisting(MemoryMappedFileName);
				}
				catch (FileNotFoundException)
				{
					_memoryMappedFile = MemoryMappedFile.CreateNew(MemoryMappedFileName, MemoryMappedFileSize);
				}

				_memoryMappedViewAccessor = _memoryMappedFile.CreateViewAccessor();
				_memoryMappedViewStream = _memoryMappedFile.CreateViewStream();
				_memoryMappedViewHandle = _memoryMappedViewStream.SafeMemoryMappedViewHandle.DangerousGetHandle();
			}
		}

		public void Dispose()
		{
			using (GetInitLock())
			{
				_memoryMappedViewHandle = IntPtr.Zero;

				if (_memoryMappedViewStream != null)
				{
					_memoryMappedViewStream.Close();
					_memoryMappedViewStream.Dispose();
					_memoryMappedViewStream = null;
				}

				if (_memoryMappedViewAccessor != null)
				{
					_memoryMappedViewAccessor.Dispose();
					_memoryMappedViewAccessor = null;
				}

				if (_memoryMappedFile != null)
				{
					_memoryMappedFile.Dispose();
					_memoryMappedFile = null;
				}

				if (_onDataUpdatedEvent != null)
				{
					_onDataUpdatedEvent.Close();
					_onDataUpdatedEvent.Dispose();
					_onDataUpdatedEvent = null;
				}

				if (_onReaderLockEvent != null)
				{
					_onReaderLockEvent.Close();
					_onReaderLockEvent.Dispose();
					_onReaderLockEvent = null;
				}
			}

			if (_onWriteLockMutex != null)
			{
				_onWriteLockMutex.Close();
				_onWriteLockMutex.Dispose();
				_onWriteLockMutex = null;
			}
		}

		public void InitializeTestAPI(int instanceId, int instancesCount)
		{
			if (instanceId < 0) throw new ArgumentException("instanceId");
			if (instancesCount < 1) throw new ArgumentException("instancesCount");
			if (instanceId >= instancesCount) throw new ArgumentException("instanceId");

			_instanceId = instanceId;
			_instancesCount = instancesCount;

			bool isInitialized = false;

			int cachedInstancesCount = ReadInt32(InstancesCountOffset);
			if (cachedInstancesCount == 0)
			{
				Write(InstancesCountOffset, _instancesCount);
			}
			else if (cachedInstancesCount != _instancesCount)
			{
				throw new ArgumentException("instancesCount");
			}

			InstancesTimesOffset = InstancesStatusesOffset + _instancesCount * IntOffset;
			MemoryContractOffset = InstancesTimesOffset + _instancesCount * IntOffset;

#if _LOGGING
				Log(string.Format("conn{0}.InitializeTestAPI({1}, {2});\r\n", _instanceId, instanceId, instancesCount));
#endif
			InstanceStatus = InstanceStatus.NotInitialized;
			isInitialized = GetKnownInstancesCount() == _instancesCount;
			if (isInitialized)
			{
				InstanceStatus = InstanceStatus.Initialized;
			}
			OnDataChanged();

			while (!isInitialized)
			{
				IsInstanceWaiting = true;
				using (var lockObj = GetReadLock())
				{
					var contract = LoadData();
					isInitialized = GetKnownInstancesCount() == _instancesCount;
					if (isInitialized)
					{
						lockObj.GetWriteLock();
						contract = LoadData();
						InstanceStatus = InstanceStatus.Initialized;
						SaveData(contract);
						IsInstanceWaiting = false;
						return;
					}
				}
				WaitDataChanges();
			}
		}

		public void DeinitializeTestAPI()
		{
			if (_instanceId < 0) throw new InvalidOperationException();
#if _LOGGING
			Log(string.Format("conn{0}.DeinitializeTestAPI();\r\n", _instanceId));
#endif
			InstanceStatus = InstanceStatus.Deinitialized;
			OnDataChanged();
		}

		public void DeclareVariable(string variableName, VariableOperations variableOperation)
		{
			using (var lockObj = GetWriteLock())
			{
#if _LOGGING
				Log(string.Format("conn{0}.DeclareVariable(\"{1}\", VariableOperations.{2});\r\n", _instanceId, variableName, variableOperation));
#endif
				var contract = LoadData();
				contract.VariableOperations[variableName] = variableOperation;
				SaveData(contract);
			}
		}

		public void NextTick(int tick)
		{
			InstanceTime = tick;
			using (var lockObj = GetWriteLock())
			{
#if _LOGGING
				Log(string.Format("conn{0}.NextTick({1});\r\n", _instanceId, tick));
#endif
				var contract = LoadData();
				int lastTickOfSlowestInstance = GetLastTickOfSlowestInstance(contract);
				foreach (var variableName in contract.Variables.Select(v => v.Name).Distinct().ToArray())
				{
					int lastVariableTickOfSlowestInstance = GetLastVariableTickOfSlowestInstance(contract, variableName);
					contract.Variables.RemoveAll(v => v.Name == variableName
													  && v.Tick < lastTickOfSlowestInstance
													  && v.Tick < lastVariableTickOfSlowestInstance);
				}
				lockObj.GetWriteLock();
				SaveData(contract);
			}
		}

		public double GetVariable(string variableName)
		{
			bool shouldWait = true;
#if _LOGGING
			bool isLogged = false;
			Guid tran = Guid.NewGuid();
#endif
			while (shouldWait)
			{
				using (GetReadLock())
				{
#if _LOGGING
					if (!isLogged)
					{
						isLogged = true;
						Log(string.Format("/*{2} conn{0}.GetVariable(\"{1}\");*/\r\n", _instanceId, variableName, tran));
					}
#endif
					var contract = LoadData();
					int lastTickOfSlowestInstance = GetLastTickOfSlowestInstance(contract);
					int lastVariableTick = GetLastVariableTick(contract, variableName);
					bool isDeinitialized = InstanceStatus == InstanceStatus.Deinitialized;
					shouldWait = lastVariableTick < InstanceTime
								 && lastTickOfSlowestInstance < InstanceTime;
					if (!(!isDeinitialized && shouldWait))
					{
						VariableOperations variableOperation;
						contract.VariableOperations.TryGetValue(variableName, out variableOperation);

						double result = 0;

						switch (variableOperation)
						{
							case VariableOperations.Nothing:
								result = contract.Variables
												 .Where(v => v.Name == variableName && v.Tick <= InstanceTime)
												 .Select(v => v.Value)
												 .LastOrDefault();
								break;
							case VariableOperations.Sum:
								for (int i = 0; i < _instancesCount; ++i)
								{
									result += contract.Variables
													  .Where(v => v.Name == variableName
																  && v.Tick <= InstanceTime
																  && v.InstaceId == i)
													  .Select(v => v.Value)
													  .LastOrDefault();
								}
								break;
							default:
								throw new NotImplementedException();
						}
#if _LOGGING
						Log(string.Format("/*{2} */ conn{0}.GetVariable(\"{1}\"); /*{3} */\r\n", _instanceId, variableName,
						                  tran.ToString().Substring(0, 8), tran));
#endif
						return result;
					}
				}
				WaitDataChanges();
			}
			return 0;
		}

		public double GetVariableForTick(string variableName, int tick)
		{
			using (GetReadLock())
			{
				var contract = LoadData();
				return contract.Variables
							   .Where(v => v.Name == variableName && v.Tick == tick)
							   .Select(v => v.Value)
							   .LastOrDefault();
			}
		}

		public void SetVariable(string variableName, double variableValue)
		{
			using (var lockObj = GetWriteLock())
			{
#if _LOGGING
				Log(string.Format("conn{0}.SetVariable(\"{1}\", {2});\r\n", _instanceId, variableName, variableValue));
#endif
				var contract = LoadData();
				var lastVariableForThisInstanceQuery = contract.Variables.Where(v => v.Tick == InstanceTime
																				&& v.InstaceId == _instanceId
																				&& v.Name == variableName);
				if (lastVariableForThisInstanceQuery.Any())
				{
					var lastVariableForThisInstance = lastVariableForThisInstanceQuery.Single();
					if (lastVariableForThisInstance.Value == variableValue) return;
					lastVariableForThisInstance.Value = variableValue;
				}
				else
				{
					contract.Variables.Add(new VariableConract
					{
						InstaceId = _instanceId,
						Tick = InstanceTime,
						Name = variableName,
						Value = variableValue
					});
				}
				SaveData(contract);
			}
		}

		public InstanceStatus InstanceStatus
		{
			get { return _instanceStatus; }
			set
			{
				if (_instanceStatus != value)
				{
					_instanceStatus = value;
					Write(InstancesStatusesOffset + _instanceId*IntOffset, (int)value);
				}
			}
		}

		public InstanceStatus GetInstanceStatus(int instanceId)
		{
			return (InstanceStatus) ReadInt32(InstancesStatusesOffset + instanceId * IntOffset);
		}

		public int GetKnownInstancesCount()
		{
			int result = 0;
			for (int i = 0; i < _instancesCount; i++)
			{
				if (GetInstanceStatus(i) != InstanceStatus.Unknown)
				{
					result++;
				}
			}
			return result;
		}

		public int InstanceTime
		{
			get { return _instanceTime; }
			set
			{
				if (_instanceTime != value)
				{
					_instanceTime = value;
					Write(InstancesTimesOffset + _instanceId * IntOffset, value);
				}
			}
		}

		public int GetInstanceTime(int instanceId)
		{
			return ReadInt32(InstancesTimesOffset + instanceId * IntOffset);
		}

		public bool IsInstanceWaiting { get; private set; }

		private int InstancesCount
		{
			get { return ReadInt32(InstancesCountOffset); }
			set { Write(InstancesCountOffset, value); }
		}

		private class Lock : IDisposable
		{
			private readonly IntPtr _memoryMappedViewHandle;
			private MemoryMappedViewAccessor _memoryMappedViewAccessor;
			private readonly Mutex _onWriteLockMutex;
			private EventWaitHandle _onReaderLockEvent;

			public Lock(IntPtr memoryMappedViewHandle,
				MemoryMappedViewAccessor memoryMappedViewAccessor,
				Mutex onWriteLockMutex,
				EventWaitHandle onReaderLockEvent)
			{
				_memoryMappedViewHandle = memoryMappedViewHandle;
				_memoryMappedViewAccessor = memoryMappedViewAccessor;
				_onWriteLockMutex = onWriteLockMutex;
				_onReaderLockEvent = onReaderLockEvent;
			}

			public void GetReadLock()
			{
				if (!IsInitLock)
				{
					while (!_onReaderLockEvent.WaitOne(1000)) { }
					IsReadLock = true;
				}
				else
				{
					_onWriteLockMutex.WaitOne();
				}
			}

			public void GetWriteLock()
			{
				if (!IsInitLock)
				{
					IsReadLock = true;
					IsWriteLock = true;
					_onWriteLockMutex.WaitOne();
					_onReaderLockEvent.Reset();
					while (ReadersCount > WritersCount)
					{
						Thread.Sleep(1000);
					}
				}
			}

			public void Dispose()
			{
				if (IsWriteLock)
				{
					_onReaderLockEvent.Set();
				}

				if (IsInitLock || IsWriteLock)
				{
					_onWriteLockMutex.ReleaseMutex();
				}

				IsWriteLock = false;
				IsReadLock = false;
			}

			//private int ReadersCount { get { return _memoryMappedViewAccessor.ReadInt32(ReadersCountOffset); } }
			//private int WritersCount { get { return _memoryMappedViewAccessor.ReadInt32(WritersCountOffset); } }
			private int ReadersCount
			{
				get
				{
					int result = _memoryMappedViewAccessor.ReadInt32(ReadersCountOffset);
					return result;
				}
			}
			private int WritersCount
			{
				get
				{
					int result = _memoryMappedViewAccessor.ReadInt32(WritersCountOffset);
					return result;
				}
			}

			private bool _isReadLock = false;
			private bool _isWriteLock = false;

			private bool IsInitLock
			{
				get
				{
					return _onReaderLockEvent == null
						|| _memoryMappedViewHandle == IntPtr.Zero;
				}
			}

			unsafe private bool IsReadLock
			{
				get { return _isReadLock; }
				set
				{
					if (!_isReadLock && value)
					{
						InterlockedIncrement((long*)(_memoryMappedViewHandle + ReadersCountOffset));
					}
					if (_isReadLock && !value)
					{
						InterlockedDecrement((long*)(_memoryMappedViewHandle + ReadersCountOffset));
					}
					_isReadLock = value;
				}
			}

			unsafe private bool IsWriteLock
			{
				get { return _isWriteLock; }
				set
				{
					if (!_isWriteLock && value)
					{
						InterlockedIncrement((long*) (_memoryMappedViewHandle + WritersCountOffset));
					}
					if (_isWriteLock && !value)
					{
						InterlockedDecrement((long*)(_memoryMappedViewHandle + WritersCountOffset));
					}
					_isWriteLock = value;
				}
			}
		}

		private Lock GetInitLock()
		{
			var lockObj = new Lock(IntPtr.Zero, null, _onWriteLockMutex, null);
			lockObj.GetReadLock();
			return lockObj;
		}

		private Lock GetReadLock()
		{
			var lockObj = new Lock(_memoryMappedViewHandle, _memoryMappedViewAccessor, _onWriteLockMutex, _onReaderLockEvent);
			lockObj.GetReadLock();
			return lockObj;
		}

		private Lock GetWriteLock()
		{
			var lockObj = new Lock(_memoryMappedViewHandle, _memoryMappedViewAccessor, _onWriteLockMutex, _onReaderLockEvent);
			lockObj.GetWriteLock();
			return lockObj;
		}

		private void OnDataChanged()
		{
			_onDataUpdatedEvent.Set();
		}

		private void WaitDataChanges()
		{
			IsInstanceWaiting = true;
			_onDataUpdatedEvent.WaitOne(1000);
			IsInstanceWaiting = false;
		}

		private int ReadInt32(int offset)
		{
			return _memoryMappedViewAccessor.ReadInt32(offset);
		}

		private int ReadInt32(ref int offset)
		{
			int result = ReadInt32(offset);
			offset += IntOffset;
			return result;
		}

		private double ReadDouble(ref int offset)
		{
			double result = _memoryMappedViewAccessor.ReadDouble(offset);
			offset += DoubleOffset;
			return result;
		}

		private string ReadString(ref int offset)
		{
			int size = ReadInt32(ref offset);
			var buffer = new byte[size];
			int read = _memoryMappedViewAccessor.ReadArray(offset, buffer, 0, size);
			if (read != size) throw new NotImplementedException();
			offset += size * ByteOffset;
			return Encoding.ASCII.GetString(buffer);
		}

		private void Write(int offset, int value)
		{
			_memoryMappedViewAccessor.Write(offset, value);
		}

		private void Write(ref int offset, int value)
		{
			Write(offset, value);
			offset += IntOffset;
		}

		private void Write(ref int offset, double value)
		{
			_memoryMappedViewAccessor.Write(offset, value);
			offset += DoubleOffset;
		}

		private void Write(ref int offset, string value)
		{
			var buffer = Encoding.ASCII.GetBytes(value);
			Write(ref offset, buffer.Length);
			_memoryMappedViewAccessor.WriteArray(offset, buffer, 0, buffer.Length);
			offset += buffer.Length * ByteOffset;
		}

		private MemoryContract LoadData()
		{
			int offset = MemoryContractOffset;
			var contract = new MemoryContract();

			int variablesCount = ReadInt32(ref offset);
			for (int i = 0; i < variablesCount; i++)
			{
				contract.Variables.Add(new VariableConract
					{
						InstaceId = ReadInt32(ref offset),
						Tick = ReadInt32(ref offset),
						Name = ReadString(ref offset),
						Value = ReadDouble(ref offset)
					});
			}

			int variableOperationsCount = ReadInt32(ref offset);
			for (int i = 0; i < variableOperationsCount; i++)
			{
				contract.VariableOperations[ReadString(ref offset)] = (VariableOperations) ReadInt32(ref offset);
			}

			return contract;
		}

		private void SaveData(MemoryContract contract)
		{
			int offset = MemoryContractOffset;

			Write(ref offset, contract.Variables.Count);
			foreach (var variable in contract.Variables)
			{
				Write(ref offset, variable.InstaceId);
				Write(ref offset, variable.Tick);
				Write(ref offset, variable.Name);
				Write(ref offset, variable.Value);
			}

			Write(ref offset, contract.VariableOperations.Count);
			foreach (var varOperation in contract.VariableOperations)
			{
				Write(ref offset, varOperation.Key);
				Write(ref offset, (int)varOperation.Value);
			}

			if (offset > MemoryMappedFileSize)
			{
				throw new InternalBufferOverflowException();
			}

			OnDataChanged();
		}

		private int GetLastTickOfSlowestInstance(MemoryContract contract)
		{
			if (_instancesCount == 0) return 0;
			int result = int.MaxValue;
			for (int i = 0; i < _instancesCount; ++i)
			{
				if (GetInstanceStatus(i) == InstanceStatus.Deinitialized) continue;
				int tick = GetInstanceTime(i);
				result = Math.Min(result, tick);
			}
			return result;
		}

		private int GetLastVariableTickOfSlowestInstance(MemoryContract contract, string variableName)
		{
			if (_instancesCount == 0) return 0;
			int result = int.MaxValue;
			for (int i = 0; i < _instancesCount; ++i)
			{
				int tick = GetLastVariableTickOfInstance(contract, variableName, i);
				result = Math.Min(result, tick);
			}
			return result;
		}

		private int GetLastVariableTickOfInstance(MemoryContract contract, string variableName, int instanceId)
		{
			var variableQueryOfInstance = contract.Variables.Where(v => v.Name == variableName
																		&& v.Tick <= InstanceTime
			                                                            && v.InstaceId == instanceId);
			if (!variableQueryOfInstance.Any()) return 0;
			int lastVariableTick = variableQueryOfInstance.Max(v => v.Tick);
			return lastVariableTick;
		}

		private int GetLastVariableTick(MemoryContract contract, string variableName)
		{
			var variableQuery = contract.Variables.Where(v => v.Name == variableName && v.Tick <= InstanceTime);
			if (!variableQuery.Any()) return 0;
			int lastVariableTick = variableQuery.Max(v => v.Tick);
			return lastVariableTick;
		}

#if _LOGGING
		private void Log(string line)
		{
			File.AppendAllText(string.Format("d:\\test{0}.log", _instanceId), "/* " + Thread.CurrentThread.ManagedThreadId + " */" + line);
			File.AppendAllText("d:\\test.log", line);
		}
#endif
	}
}
