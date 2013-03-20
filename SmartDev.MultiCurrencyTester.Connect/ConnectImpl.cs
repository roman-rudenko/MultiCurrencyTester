//#define _LOGGING

using System;
using System.Collections.Generic;
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

		private const int InstancesCountOffset = 0;
		private const int VariablesTicksOffset = InstancesCountOffset + IntOffset;
		private const int InstancesStatusesOffset = VariablesTicksOffset + IntOffset;
		private int InstancesTimesOffset = -1;
		private int MemoryContractOffset = -1;

		private const long MemoryMappedFileSize = 0xA00000; // 10 megabytes
		private const string MemoryMappedFileName = "SmartDev.MultiCurrencyTester.MemoryMappedFile";
		private const string OnDataUpdatedEventName = "SmartDev.MultiCurrencyTester.OnDataUpdatedEvent";
		private const string OnWriteLockMutexName = "SmartDev.MultiCurrencyTester.OnWriteLockMutex";

		//private readonly DateTime MinMt4Date = new DateTime(1970, 1, 1, 0, 0, 0, 0);
		//private int LastArchivedMonth = -1;

		private string _balanceFileName;
		FileStream _balanceFile;
		StreamWriter _balanceFileWriter;
		private MemoryMappedFile _memoryMappedFile;
		private MemoryMappedViewAccessor _memoryMappedViewAccessor;
		private MemoryMappedViewStream _memoryMappedViewStream;
		private IntPtr _memoryMappedViewHandle;
		private EventWaitHandle _onDataUpdatedEvent;
		private Mutex _onWriteLockMutex;
		private int _instanceId = -1;
		private int _instancesCount = -1;
		private InstanceStatus _instanceStatus = InstanceStatus.Unknown;
		private int _instanceTime = -1;
		private int _syncSeconds = 0;

		public ConnectImpl()
		{
			_onDataUpdatedEvent = new EventWaitHandle(true, EventResetMode.ManualReset, OnDataUpdatedEventName);
			_onWriteLockMutex = new Mutex(false, OnWriteLockMutexName);

			using (GetLock())
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
			using (GetLock())
			{
				_memoryMappedViewHandle = IntPtr.Zero;

				if (!string.IsNullOrEmpty(_balanceFileName))
				{
					if (_balanceFileWriter != null)
					{
						_balanceFileWriter.Close();
						_balanceFileWriter.Dispose();
						_balanceFileWriter = null;
					}

					if (_balanceFile != null)
					{
						_balanceFile.Close();
						_balanceFile.Dispose();
						_balanceFile = null;
					}
				}

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
			}

			if (_onWriteLockMutex != null)
			{
				_onWriteLockMutex.Close();
				_onWriteLockMutex.Dispose();
				_onWriteLockMutex = null;
			}
		}

		public void InitializeTestAPI(int instanceId, int instancesCount, int syncSeconds, string logFilePath)
		{
			if (instanceId < 0) throw new ArgumentException("instanceId");
			if (instancesCount < 1) throw new ArgumentException("instancesCount");
			if (instanceId >= instancesCount) throw new ArgumentException("instanceId");
			if (syncSeconds < 0) throw new ArgumentException("syncSeconds");

			_instanceId = instanceId;
			_instancesCount = instancesCount;
			_syncSeconds = syncSeconds;

			_balanceFileName = logFilePath;
			if (!string.IsNullOrEmpty(_balanceFileName))
			{
				if (!Directory.Exists(Path.GetDirectoryName(_balanceFileName)))
				{
					Directory.CreateDirectory(Path.GetDirectoryName(_balanceFileName));
				}
				_balanceFile = File.OpenWrite(_balanceFileName);
				_balanceFileWriter = new StreamWriter(_balanceFile);
			}

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

			while (!isInitialized)
			{
				isInitialized = GetKnownInstancesCount() == _instancesCount;
				if (isInitialized)
				{
					InstanceStatus = InstanceStatus.Initialized;
					OnDataChanged();
				}
				else
				{
					WaitDataChanges();
				}
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
			using (GetLock())
			{
#if _LOGGING
				Log(string.Format("conn{0}.DeclareVariable(\"{1}\", VariableOperations.{2});\r\n", _instanceId, variableName, variableOperation));
#endif
				var contract = LoadData();
				contract.VariableOperations[variableName] = variableOperation;
				SaveData(contract);
				OnDataChanged();
			}
		}

		public void NextTick(int tick, double balance, double equity)
		{
			InstanceTime = tick;
			if (!string.IsNullOrEmpty(_balanceFileName))
			{
				_balanceFileWriter.WriteLine("{0};{1};{2};", tick, balance, equity);
			}
			OnDataChanged();
#if _LOGGING
			Log(string.Format("conn{0}.NextTick({1});\r\n", _instanceId, tick));
#endif
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
				MemoryContract contract;
				using (GetLock())
				{
#if _LOGGING
					if (!isLogged)
					{
						isLogged = true;
						Log(string.Format("/*{2} conn{0}.GetVariable(\"{1}\");*/\r\n", _instanceId, variableName, tran));
					}
#endif
					contract = LoadData();
				}
				int lastTickOfSlowestInstance = GetLastTickOfSlowestInstance(contract);
				bool isDeinitialized = InstanceStatus == InstanceStatus.Deinitialized;
				shouldWait = lastTickOfSlowestInstance < InstanceTime - _syncSeconds;
				if (!(!isDeinitialized && shouldWait))
				{
					VariableOperations variableOperation;
					contract.VariableOperations.TryGetValue(variableName, out variableOperation);

					double result = 0;

					if (contract.Variables.ContainsKey(variableName))
					{
						var variableValues = contract.Variables[variableName];

						switch (variableOperation)
						{
							case VariableOperations.Nothing:
								result = variableValues
								                 .Select(v => v.Value)
								                 .Single();
								break;
							case VariableOperations.Sum:
								result = variableValues
												 .Select(v => v.Value)
												 .Sum();
								break;
							default:
								throw new NotImplementedException();
						}
					}
#if _LOGGING
					Log(string.Format("/*{2} */ conn{0}.GetVariable(\"{1}\"); /*{3} */\r\n", _instanceId, variableName,
						                tran.ToString().Substring(0, 8), tran));
#endif
					return result;
				}
				WaitDataChanges();
			}
			return 0;
		}

		public void SetVariable(string variableName, double variableValue)
		{
			bool shouldWait = true;
			while (shouldWait)
			{
				using (GetLock())
				{
#if _LOGGING
				Log(string.Format("conn{0}.SetVariable(\"{1}\", {2});\r\n", _instanceId, variableName, variableValue));
#endif
					var contract = LoadData();

					int lastTickOfSlowestInstance = GetLastTickOfSlowestInstance(contract);
					bool isDeinitialized = InstanceStatus == InstanceStatus.Deinitialized;
					shouldWait = lastTickOfSlowestInstance < InstanceTime - _syncSeconds;
					if (!(!isDeinitialized && shouldWait))
					{

						VariableOperations variableOperation;
						contract.VariableOperations.TryGetValue(variableName, out variableOperation);
						List<VariableConract> variableValues;
						if (contract.Variables.ContainsKey(variableName))
						{
							variableValues = contract.Variables[variableName];
						}
						else
						{
							variableValues = new List<VariableConract> {new VariableConract {InstaceId = _instanceId}};
							contract.Variables[variableName] = variableValues;
						}

						switch (variableOperation)
						{
							case VariableOperations.Nothing:
								variableValues.Single().Value = variableValue;
								break;
							case VariableOperations.Sum:
								var varQuery = variableValues.Where(v => v.InstaceId == _instanceId);
								if (varQuery.Any())
								{
									varQuery.Single().Value = variableValue;
								}
								else
								{
									variableValues.Add(new VariableConract {InstaceId = _instanceId, Value = variableValue});
								}
								break;
							default:
								throw new NotImplementedException();
						}

						SaveData(contract);
						OnDataChanged();
						return;
					}
				}
				WaitDataChanges();
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

		private class Lock : IDisposable
		{
			private readonly Mutex _onWriteLockMutex;

			public Lock(Mutex onWriteLockMutex)
			{
				_onWriteLockMutex = onWriteLockMutex;
				_onWriteLockMutex.WaitOne();
			}

			public void Dispose()
			{
				_onWriteLockMutex.ReleaseMutex();
			}
		}

		private Lock GetLock()
		{
			return new Lock(_onWriteLockMutex);;
		}

		private void OnDataChanged()
		{
			_onDataUpdatedEvent.Set();
		}

		private void WaitDataChanges()
		{
			IsInstanceWaiting = true;
			_onDataUpdatedEvent.Reset();
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
				var list = new List<VariableConract>();
				contract.Variables[ReadString(ref offset)] = list;

				int variableContractsCount = ReadInt32(ref offset);
				for (int j = 0; j < variableContractsCount; j++)
				{
					list.Add(new VariableConract
						{
							InstaceId = ReadInt32(ref offset),
							Value = ReadDouble(ref offset)
						});
				}
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
				Write(ref offset, variable.Key);
				Write(ref offset, variable.Value.Count);
				foreach (var variableContract in variable.Value)
				{
					Write(ref offset, variableContract.InstaceId);
					Write(ref offset, variableContract.Value);
				}
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

#if _LOGGING
		private void Log(string line)
		{
			File.AppendAllText(string.Format("d:\\test{0}.log", _instanceId), line);
			//File.AppendAllText("d:\\test.log", line);
		}
#endif
	}
}
