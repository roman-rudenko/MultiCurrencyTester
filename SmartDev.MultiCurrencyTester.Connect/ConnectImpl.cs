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

		[DllImport("kernel32.dll", SetLastError = true)]
		unsafe public static extern long InterlockedCompareExchange(long* lpDestination, long exchange, long comparand);

		//[DllImport("kernel32.dll", SetLastError = true)]
		//unsafe public static extern long InterlockedCompareExchangeRelease(long* lpDestination, long exchange, long comparand);

		private MemOperations _memOp = new MemOperations();

		private string _balanceFileNameTemplate = "d:\\balance\\balance_{0}.csv";
		private string _balanceFileName;
		FileStream _balanceFile;
		StreamWriter _balanceFileWriter;
		private EventWaitHandle _onDataUpdatedEvent;

		public ConnectImpl()
		{
			_onDataUpdatedEvent = new EventWaitHandle(true, EventResetMode.ManualReset, Constants.OnDataUpdatedEventName);
		}

		public void Dispose()
		{
			if (_memOp != null)
			{
				_memOp.Dispose();
				_memOp = null;
			}

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

			if (_onDataUpdatedEvent != null)
			{
				_onDataUpdatedEvent.Close();
				_onDataUpdatedEvent.Dispose();
				_onDataUpdatedEvent = null;
			}
		}

		public void InitializeTestAPI(int instanceId, int instancesCount)
		{
			if (instanceId < 0) throw new ArgumentException("instanceId");
			if (instancesCount < 1) throw new ArgumentException("instancesCount");
			if (instanceId >= instancesCount) throw new ArgumentException("instanceId");

			_memOp.InstanceId = instanceId;
			_memOp.InstancesCount = instancesCount;
			_memOp.InstanceStatus = InstanceStatus.NotInitialized;

#if _LOGGING
				Log(string.Format("conn{0}.InitializeTestAPI({1}, {2});\r\n", _instanceId, instanceId, instancesCount));
#endif

			_balanceFileName = string.Format(_balanceFileNameTemplate, _memOp.InstanceId);
			if (!Directory.Exists(Path.GetDirectoryName(_balanceFileName)))
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_balanceFileName));
			}
			_balanceFile = File.OpenWrite(_balanceFileName);
			_balanceFileWriter = new StreamWriter(_balanceFile);

			bool isInitialized = false;
			while (!isInitialized)
			{
				isInitialized = GetKnownInstancesCount() == _memOp.InstancesCount;
				if (isInitialized)
				{
					_memOp.InstanceStatus = InstanceStatus.Initialized;
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
			if (_memOp.InstanceId < 0) throw new InvalidOperationException();
#if _LOGGING
			Log(string.Format("conn{0}.DeinitializeTestAPI();\r\n", _instanceId));
#endif
			_memOp.InstanceStatus = InstanceStatus.Deinitialized;
			OnDataChanged();
		}

		public void DeclareVariable(string variableName, VariableOperations variableOperation)
		{
#if _LOGGING
				Log(string.Format("conn{0}.DeclareVariable(\"{1}\", VariableOperations.{2});\r\n", _instanceId, variableName, variableOperation));
#endif
			_memOp.SetVariableOperation(variableName, variableOperation);
			OnDataChanged();
		}

		public void NextTick(int tick, double balance, double equity)
		{
			_memOp.InstanceTime = tick;
			_balanceFileWriter.WriteLine("{0};{1};{2};", tick, balance, equity);
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
#if _LOGGING
				if (!isLogged)
				{
					isLogged = true;
					Log(string.Format("/*{2} conn{0}.GetVariable(\"{1}\");*/\r\n", _instanceId, variableName, tran));
				}
#endif

				bool isDeinitialized = _memOp.InstanceStatus == InstanceStatus.Deinitialized;
				shouldWait = GetTimeOfSlowestInstance() < _memOp.InstanceTime;
				if (!(!isDeinitialized && shouldWait))
				{
					double result = 0;

					switch (_memOp.GetVariableOperation(variableName))
					{
						case VariableOperations.Nothing:
							result = _memOp.GetVariableValue(variableName);
							break;
						case VariableOperations.Sum:
							//for (int i = 0; i < _instancesCount; ++i)
							//{
							//    result += contract.Variables
							//                      .Where(v => v.Name == variableName
							//                                  && v.Tick <= InstanceTick
							//                                  && v.InstaceId == i)
							//                      .Select(v => v.Value)
							//                      .LastOrDefault();
							//}
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
				WaitDataChanges();
			}
			return 0;
		}

		//public double GetVariableForTick(string variableName, int tick)
		//{
		//    using (GetLock())
		//    {
		//        var contract = LoadData();
		//        return contract.Variables
		//                       .Where(v => v.Name == variableName && v.Tick == tick)
		//                       .Select(v => v.Value)
		//                       .LastOrDefault();
		//    }
		//}

		public void SetVariable(string variableName, double variableValue)
		{
			bool shouldWait = true;
			while (shouldWait)
			{
				bool isDeinitialized = _memOp.InstanceStatus == InstanceStatus.Deinitialized;
				shouldWait = GetTimeOfSlowestInstance() < _memOp.InstanceTime;
				if (!(!isDeinitialized && shouldWait))
				{
					_memOp.SetVariableValue(variableName, variableValue);
					OnDataChanged();
				}
				WaitDataChanges();
			}
#if _LOGGING
				Log(string.Format("conn{0}.SetVariable(\"{1}\", {2});\r\n", _instanceId, variableName, variableValue));
#endif
		}

		public bool IsInstanceWaiting { get; private set; }

		public InstanceStatus InstanceStatus { get { return _memOp.InstanceStatus; } }

		private int GetKnownInstancesCount()
		{
			int result = 0;
			int instancesCount = _memOp.InstancesCount;
			for (int i = 0; i < instancesCount; i++)
			{
				if (_memOp.GetInstanceStatus(i) != InstanceStatus.Unknown)
				{
					result++;
				}
			}
			return result;
		}

		private int GetTimeOfSlowestInstance()
		{
			int result = int.MaxValue;
			int instancesCount = _memOp.InstancesCount;
			for (int i = 0; i < instancesCount; i++)
			{
				if (_memOp.GetInstanceStatus(i) == InstanceStatus.Deinitialized) continue;
				result = Math.Min(result, _memOp.GetInstanceTime(i));
			}
			return result;
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

		//private int GetLastTickOfSlowestInstance(MemoryContract contract)
		//{
		//    if (_instancesCount == 0) return 0;
		//    int result = int.MaxValue;
		//    for (int i = 0; i < _instancesCount; ++i)
		//    {
		//        if (GetInstanceStatus(i) == InstanceStatus.Deinitialized) continue;
		//        int tick = GetInstanceTime(i);
		//        result = Math.Min(result, tick);
		//    }
		//    return result;
		//}

		//private int GetLastVariableTickOfSlowestInstance(MemoryContract contract, string variableName)
		//{
		//    if (_instancesCount == 0) return 0;
		//    int result = int.MaxValue;
		//    for (int i = 0; i < _instancesCount; ++i)
		//    {
		//        int tick = GetLastVariableTickOfInstance(contract, variableName, i);
		//        result = Math.Min(result, tick);
		//    }
		//    return result;
		//}

		//private int GetLastVariableTickOfInstance(MemoryContract contract, string variableName, int instanceId)
		//{
		//    var variableQueryOfInstance = contract.Variables.Where(v => v.Name == variableName
		//                                                                && v.Tick <= InstanceTick
		//                                                                && v.InstaceId == instanceId);
		//    if (!variableQueryOfInstance.Any()) return 0;
		//    int lastVariableTick = variableQueryOfInstance.Max(v => v.Tick);
		//    return lastVariableTick;
		//}

		//private int GetLastVariableTick(MemoryContract contract, string variableName)
		//{
		//    var variableQuery = contract.Variables.Where(v => v.Name == variableName && v.Tick <= InstanceTick);
		//    if (!variableQuery.Any()) return 0;
		//    int lastVariableTick = variableQuery.Max(v => v.Tick);
		//    return lastVariableTick;
		//}

#if _LOGGING
		private void Log(string line)
		{
			File.AppendAllText(string.Format("d:\\test{0}.log", _instanceId), line);
			//File.AppendAllText("d:\\test.log", line);
		}
#endif
	}
}
