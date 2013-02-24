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

		public ConnectImpl()
		{
			_onDataUpdatedEvent = new EventWaitHandle(true, EventResetMode.ManualReset, OnDataUpdatedEventName);
			_onReaderLockEvent = new EventWaitHandle(true, EventResetMode.ManualReset, OnReaderLockEventName);
			_onWriteLockMutex = new Mutex(false, OnWriteLockMutexName);

			bool isNeedInitializeMemoryMappedFile = false;

			using (var lockObj = GetInitLock())
			{
				try
				{
					_memoryMappedFile = MemoryMappedFile.OpenExisting(MemoryMappedFileName);
				}
				catch (FileNotFoundException)
				{
					_memoryMappedFile = MemoryMappedFile.CreateNew(MemoryMappedFileName, MemoryMappedFileSize);
					isNeedInitializeMemoryMappedFile = true;
				}

				_memoryMappedViewAccessor = _memoryMappedFile.CreateViewAccessor();
				_memoryMappedViewStream = _memoryMappedFile.CreateViewStream();
				_memoryMappedViewHandle = _memoryMappedViewStream.SafeMemoryMappedViewHandle.DangerousGetHandle();

				if (isNeedInitializeMemoryMappedFile)
				{
					_memoryMappedViewAccessor.Write(0, 0);
					SaveData(lockObj, new MemoryContract());
				}
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

			bool isInitialized = false;

			using (var lockObj = GetReadLock())
			{
#if _LOGGING
				Log(string.Format("conn{0}.InitializeTestAPI({1}, {2});\r\n", _instanceId, instanceId, instancesCount));
#endif
				var contract = LoadData();
				if (contract.InstancesCount == 0)
				{
					contract.InstancesCount = instancesCount;
				}
				else if (contract.InstancesCount != instancesCount)
				{
					throw new ArgumentException("instancesCount");
				}

				contract.InstancesStatuses[_instanceId] = InstanceStatus.NotInitialized;
				isInitialized = contract.InstancesStatuses.Count == contract.InstancesCount;
				if (isInitialized)
				{
					contract.InstancesStatuses[_instanceId] = InstanceStatus.Initialized;
				}
				SaveData(lockObj, contract);
			}

			while (!isInitialized)
			{
				IsInstanceWaiting = true;
				using (var lockObj = GetReadLock())
				{
					var contract = LoadData();
					isInitialized = contract.InstancesStatuses.Count == contract.InstancesCount;
					if (isInitialized)
					{
						contract.InstancesStatuses[_instanceId] = InstanceStatus.Initialized;
						SaveData(lockObj, contract);
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
			using (var lockObj = GetReadLock())
			{
#if _LOGGING
				Log(string.Format("conn{0}.DeinitializeTestAPI();\r\n", _instanceId));
#endif
				var contract = LoadData();
				contract.InstancesStatuses[_instanceId] = InstanceStatus.Deinitialized;
				SaveData(lockObj, contract);
			}
		}

		public void DeclareVariable(string variableName, VariableOperations variableOperation)
		{
			using (var lockObj = GetReadLock())
			{
#if _LOGGING
				Log(string.Format("conn{0}.DeclareVariable(\"{1}\", VariableOperations.{2});\r\n", _instanceId, variableName, variableOperation));
#endif
				var contract = LoadData();
				contract.VariableOperations[variableName] = variableOperation;
				SaveData(lockObj, contract);
			}
		}

		public void NextTick(int tick)
		{
			using (var lockObj = GetReadLock())
			{
#if _LOGGING
				Log(string.Format("conn{0}.NextTick({1});\r\n", _instanceId, tick));
#endif
				var contract = LoadData();
				contract.InstancesTime[_instanceId] = tick;
				int lastTickOfSlowestInstance = GetLastTickOfSlowestInstance(contract);
				foreach (var variableName in contract.Variables.Select(v => v.Name).Distinct().ToArray())
				{
					int lastVariableTickOfSlowestInstance = GetLastVariableTickOfSlowestInstance(contract, variableName);
					contract.Variables.RemoveAll(v => v.Name == variableName
													  && v.Tick < lastTickOfSlowestInstance
													  && v.Tick < lastVariableTickOfSlowestInstance);
				}
				SaveData(lockObj, contract);
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
					int lastInstanceTick = GetLastInstanceTick(contract);
					int lastTickOfSlowestInstance = GetLastTickOfSlowestInstance(contract);
					int lastVariableTick = GetLastVariableTick(contract, variableName);
					bool isDeinitialized = GetInstanceStatus(contract) == InstanceStatus.Deinitialized;
					shouldWait = lastVariableTick < lastInstanceTick
								 && lastTickOfSlowestInstance < lastInstanceTick;
					if (!(!isDeinitialized && shouldWait))
					{
						VariableOperations variableOperation;
						contract.VariableOperations.TryGetValue(variableName, out variableOperation);

						double result = 0;

						switch (variableOperation)
						{
							case VariableOperations.Nothing:
								result = contract.Variables
												 .Where(v => v.Name == variableName && v.Tick <= lastInstanceTick)
												 .Select(v => v.Value)
												 .LastOrDefault();
								break;
							case VariableOperations.Sum:
								for (int i = 0; i < contract.InstancesCount; ++i)
								{
									result += contract.Variables
													  .Where(v => v.Name == variableName
																  && v.Tick <= lastInstanceTick
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
			using (var lockObj = GetReadLock())
			{
#if _LOGGING
				Log(string.Format("conn{0}.SetVariable(\"{1}\", {2});\r\n", _instanceId, variableName, variableValue));
#endif
				var contract = LoadData();
				int lastInstanceTick = GetLastInstanceTick(contract);
				var lastVariableForThisInstanceQuery = contract.Variables.Where(v => v.Tick == lastInstanceTick
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
						Tick = lastInstanceTick,
						Name = variableName,
						Value = variableValue
					});
				}
				SaveData(lockObj, contract);
			}
		}

		public InstanceStatus GetInstanceStatus()
		{
			using (GetReadLock())
			{
				var contract = LoadData();
				return GetInstanceStatus(contract);
			}
		}

		public int GetLastInstanceTick()
		{
			using (GetReadLock())
			{
				var contract = LoadData();
				return GetLastInstanceTick(contract);
			}
		}

		public bool IsInstanceWaiting { get; private set; }

		private class Lock : IDisposable
		{
			private readonly IntPtr _memoryMappedViewHandle;
			private readonly Mutex _onWriteLockMutex;

			public Lock(IntPtr memoryMappedViewHandle, Mutex onWriteLockMutex)
			{
				_memoryMappedViewHandle = memoryMappedViewHandle;
				_onWriteLockMutex = onWriteLockMutex;
			}

			unsafe public void GetReadLock()
			{
				if (_memoryMappedViewHandle != IntPtr.Zero)
				{
					InterlockedIncrement((long*)_memoryMappedViewHandle);
				}
				_onWriteLockMutex.WaitOne();
			}

			public void GetWriteLock()
			{
			}

			unsafe public void Dispose()
			{
				if (_memoryMappedViewHandle != IntPtr.Zero)
				{
					InterlockedDecrement((long*)_memoryMappedViewHandle);
				}
				_onWriteLockMutex.ReleaseMutex();
			}
		}

		private Lock GetInitLock()
		{
			var lockObj = new Lock(IntPtr.Zero, _onWriteLockMutex);
			lockObj.GetReadLock();
			return lockObj;
		}

		private Lock GetReadLock()
		{
			var lockObj = new Lock(_memoryMappedViewHandle, _onWriteLockMutex);
			lockObj.GetReadLock();
			return lockObj;
		}

		private Lock GetWriteLock()
		{
			var lockObj = new Lock(_memoryMappedViewHandle, _onWriteLockMutex);
			//lockObj.GetWriteLock();
			lockObj.GetReadLock();
			return lockObj;
		}

		private void DataChanged()
		{
			_onDataUpdatedEvent.Set();
		}

		private void WaitDataChanges()
		{
			IsInstanceWaiting = true;
			_onDataUpdatedEvent.WaitOne(1000);
			IsInstanceWaiting = false;
		}

		private int ReadInt32(ref int offset)
		{
			int result = _memoryMappedViewAccessor.ReadInt32(offset);
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

		private void Write(ref int offset, int value)
		{
			_memoryMappedViewAccessor.Write(offset, value);
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
			int offset = IntOffset;
			var contract = new MemoryContract();

			contract.InstancesCount = ReadInt32(ref offset);

			int instancesStatusesCount = ReadInt32(ref offset);
			for (int i = 0; i < instancesStatusesCount; i++)
			{
				contract.InstancesStatuses[ReadInt32(ref offset)] = (InstanceStatus)ReadInt32(ref offset);
			}

			int instancesTimeCount = ReadInt32(ref offset);
			for (int i = 0; i < instancesTimeCount; i++)
			{
				contract.InstancesTime[ReadInt32(ref offset)] = ReadInt32(ref offset);
			}

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

			//_memoryMappedViewStream.Position = 0;
			return contract;
		}

		private void SaveData(Lock lockObj, MemoryContract contract)
		{
			lockObj.GetWriteLock();
			int offset = IntOffset;

			Write(ref offset, contract.InstancesCount);

			Write(ref offset, contract.InstancesStatuses.Count);
			foreach (var status in contract.InstancesStatuses)
			{
				Write(ref offset, status.Key);
				Write(ref offset, (int)status.Value);
			}

			Write(ref offset, contract.InstancesTime.Count);
			foreach (var time in contract.InstancesTime)
			{
				Write(ref offset, time.Key);
				Write(ref offset, time.Value);
			}

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

			DataChanged();
		}

		private InstanceStatus GetInstanceStatus(MemoryContract contract)
		{
			InstanceStatus result;
			contract.InstancesStatuses.TryGetValue(_instanceId, out result);
			return result;
		}

		private int GetLastInstanceTick(MemoryContract contract, int instanceId)
		{
			int result;
			if (!contract.InstancesTime.TryGetValue(instanceId, out result)) return 0;
			return result;
		}

		private int GetLastInstanceTick(MemoryContract contract)
		{
			return GetLastInstanceTick(contract, _instanceId);
		}

		private int GetLastTickOfSlowestInstance(MemoryContract contract)
		{
			if (contract.InstancesCount == 0) return 0;
			int result = int.MaxValue;
			for (int i = 0; i < contract.InstancesCount; ++i)
			{
				if (contract.InstancesStatuses[i] == InstanceStatus.Deinitialized) continue;
				int tick = GetLastInstanceTick(contract, i);
				result = Math.Min(result, tick);
			}
			return result;
		}

		private int GetLastVariableTickOfSlowestInstance(MemoryContract contract, string variableName)
		{
			if (contract.InstancesCount == 0) return 0;
			int result = int.MaxValue;
			for (int i = 0; i < contract.InstancesCount; ++i)
			{
				int tick = GetLastVariableTickOfInstance(contract, variableName, i);
				result = Math.Min(result, tick);
			}
			return result;
		}

		private int GetLastVariableTickOfInstance(MemoryContract contract, string variableName, int instanceId)
		{
			int lastInstanceTick = GetLastInstanceTick(contract);
			var variableQueryOfInstance = contract.Variables.Where(v => v.Name == variableName
			                                                            && v.Tick <= lastInstanceTick
			                                                            && v.InstaceId == instanceId);
			if (!variableQueryOfInstance.Any()) return 0;
			int lastVariableTick = variableQueryOfInstance.Max(v => v.Tick);
			return lastVariableTick;
		}

		private int GetLastVariableTick(MemoryContract contract, string variableName)
		{
			int lastInstanceTick = GetLastInstanceTick(contract);
			var variableQuery = contract.Variables.Where(v => v.Name == variableName && v.Tick <= lastInstanceTick);
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
