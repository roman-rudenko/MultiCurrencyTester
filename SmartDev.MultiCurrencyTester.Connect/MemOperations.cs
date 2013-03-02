using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;

namespace SmartDev.MultiCurrencyTester.Connect
{
	internal class MemOperations : IDisposable
	{
		private MemoryMappedFile _memoryMappedFile;
		private MemoryMappedViewAccessor _memoryMappedViewAccessor;
		private Mutex _onWriteLockMutex;

		private readonly List<string> _variables = new List<string>();

		private int _instancesTicksOffset = -1;
		private int _variablesOperationsOffset = -1;
		private int _variablesValuesOffset = -1;
		private int _variablesNamesOffset = -1;

		private volatile int _variablesLock = -1; //TODO: возможно переполнение

		public MemOperations()
		{
			InstanceId = -1;
			_onWriteLockMutex = new Mutex(false, Constants.OnWriteLockMutexName);

			using (GetLock())
			{
				try
				{
					_memoryMappedFile = MemoryMappedFile.OpenExisting(Constants.MemoryMappedFileName);
				}
				catch (FileNotFoundException)
				{
					_memoryMappedFile = MemoryMappedFile.CreateNew(Constants.MemoryMappedFileName, Constants.MemoryMappedFileSize);
				}

				_memoryMappedViewAccessor = _memoryMappedFile.CreateViewAccessor();
				//				_memoryMappedViewStream = _memoryMappedFile.CreateViewStream();
				//				_memoryMappedViewHandle = _memoryMappedViewStream.SafeMemoryMappedViewHandle.DangerousGetHandle();
			}
		}

		public void Dispose()
		{
			//_memoryMappedViewHandle = IntPtr.Zero;

			//if (_memoryMappedViewStream != null)
			//{
			//    _memoryMappedViewStream.Close();
			//    _memoryMappedViewStream.Dispose();
			//    _memoryMappedViewStream = null;
			//}

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

			if (_onWriteLockMutex != null)
			{
				_onWriteLockMutex.Close();
				_onWriteLockMutex.Dispose();
				_onWriteLockMutex = null;
			}
		}

		public int VariablesLock
		{
			get { return ReadInt32(Constants.VariablesLockOffset); }
			set { Write(Constants.VariablesLockOffset, value); }
		}
		
		public int InstanceId { get; set; }

		public int InstancesCount
		{
			get { return ReadInt32(Constants.InstancesCountOffset); }
			set
			{
				lock(this)
				using (GetLock())
				{
					if (InstancesCount == 0)
					{
						Write(Constants.InstancesCountOffset, value);
					}
					else if (InstancesCount != value)
					{
						throw new ArgumentException("instancesCount");
					}

					UpdateOffsets();
				}
			}
		}

		public InstanceStatus InstanceStatus
		{
			get { return GetInstanceStatus(InstanceId); }
			set { Write(Constants.InstancesStatusesOffset + InstanceId * Constants.IntOffset, (int) value); }
		}

		public InstanceStatus GetInstanceStatus(int instanceId)
		{
			return (InstanceStatus)ReadInt32(Constants.InstancesStatusesOffset + instanceId * Constants.IntOffset);
		}

		public int InstanceTime
		{
			get { return GetInstanceTime(InstanceId); }
			set { Write(_instancesTicksOffset + InstanceId * Constants.IntOffset, value); }
		}

		public int GetInstanceTime(int instanceId)
		{
			var result = ReadInt32(_instancesTicksOffset + instanceId * Constants.IntOffset);
			return result;
		}

		public int VariablesCount
		{
			get { return ReadInt32(Constants.VariablesCountOffset); }
			private set { Write(Constants.VariablesCountOffset, value); }
		}

		public int GetVariableIndex(string variableName)
		{
			lock (this)
			{
				int result;
				do
				{
					result = _variables.IndexOf(variableName);
				} while (!IsCacheValid);

				return result;
			}
		}

		public VariableOperations GetVariableOperation(string variableName)
		{
			VariableOperations result = default(VariableOperations);

			do
			{
				int index = GetVariableIndex(variableName);
				if (index != -1)
				{
					result = (VariableOperations) ReadInt32(_variablesOperationsOffset + index * Constants.IntOffset);
				}
			} while (!IsCacheValid);

			return result;
		}

		public void SetVariableOperation(string variableName, VariableOperations variableOperation)
		{
			if (GetVariableOperation(variableName) == variableOperation) return;
			lock(this)
			using (GetLock())
			{
				UpdateCache();
				int index = _variables.IndexOf(variableName);
				if (index != -1)
				{
					AddVariableName(variableName);
					index = _variables.IndexOf(variableName);
				}
				Write(_variablesOperationsOffset + index * Constants.IntOffset, (int) variableOperation);
			}
		}

		public double GetVariableValue(string variableName)
		{
			double result = 0;

			do
			{
				int index = GetVariableIndex(variableName);
				if (index != -1)
				{
					result = ReadDouble(_variablesValuesOffset + index * Constants.IntOffset);
				}
			} while (!IsCacheValid);

			return result;
		}

		public void SetVariableValue(string variableName, double value)
		{
			do
			{
				int index = GetVariableIndex(variableName);
				if (index == -1)
				{
					using (GetLock())
					{
						AddVariableName(variableName);
						index = _variables.IndexOf(variableName);
					}
				}
				Write(_variablesValuesOffset + index * Constants.IntOffset, value);
			} while (!IsCacheValid);
		}

		private bool IsCacheValid
		{
			get
			{
				bool result = true;
				while (_variablesLock != VariablesLock)
				{
					result = false;
					if (VariablesLock == -1)
					{
						Thread.Sleep(100);
					}
					else
					{
						UpdateCache();
					}
				}
				return result;
			}
		}

		private void UpdateCache()
		{
			lock (this)
			{
				_variablesLock = VariablesLock;
				_variables.Clear();
				int variablesCount = VariablesCount;
				int offset = _variablesNamesOffset;
				for (int i = 0; i < VariablesCount; i++)
				{
					_variables.Add(ReadString(ref offset));
				}
			}
		}

		private void AddVariableName(string variableName)
		{
			int variablesCount = VariablesCount;
			var variablesOperations = new int[variablesCount + 1];
			var variablesValues = new double[variablesCount + 1];
			for (int i = 0; i < variablesCount; i++)
			{
				variablesOperations[i] = ReadInt32(_variablesOperationsOffset + i * Constants.IntOffset);
				variablesValues[i] = ReadDouble(_variablesValuesOffset + i * Constants.IntOffset);
			}

			_variables.Add(variableName);
			variablesCount++;
			VariablesCount = variablesCount;
			UpdateOffsets();

			int variablesOperationsOffset = _variablesOperationsOffset;
			int variablesValuesOffset = _variablesValuesOffset;
			int variablesNamesOffset = _variablesNamesOffset;
			for (int i = 0; i < variablesCount; i++)
			{
				Write(ref variablesOperationsOffset, variablesOperations[i]);
				Write(ref variablesValuesOffset, variablesValues[i]);
				Write(ref variablesNamesOffset, _variables[i]);
			}
		}

		private UpdateLock GetLock()
		{
			return new UpdateLock(_onWriteLockMutex, _memoryMappedViewAccessor); ;
		}

		private void UpdateOffsets()
		{
			int nextOffset = Constants.InstancesStatusesOffset + InstancesCount * Constants.IntOffset;

			_instancesTicksOffset = nextOffset;
			nextOffset = _instancesTicksOffset + InstancesCount * Constants.IntOffset;

			_variablesValuesOffset = nextOffset;
			nextOffset = _variablesValuesOffset + VariablesCount * Constants.DoubleOffset;

			_variablesOperationsOffset = nextOffset;
			nextOffset = _variablesOperationsOffset + VariablesCount * Constants.IntOffset;

			_variablesNamesOffset = nextOffset;
		}

		private int ReadInt32(int offset)
		{
			return _memoryMappedViewAccessor.ReadInt32(offset);
		}

		private int ReadInt32(ref int offset)
		{
			int result = ReadInt32(offset);
			offset += Constants.IntOffset;
			return result;
		}

		private double ReadDouble(int offset)
		{
			return _memoryMappedViewAccessor.ReadDouble(offset);
		}

		private double ReadDouble(ref int offset)
		{
			double result = ReadDouble(offset);
			offset += Constants.DoubleOffset;
			return result;
		}

		private string ReadString(ref int offset)
		{
			int size = ReadInt32(ref offset);
			var buffer = new byte[size];
			int read = _memoryMappedViewAccessor.ReadArray(offset, buffer, 0, size);
			if (read != size) throw new NotImplementedException();
			offset += size * Constants.ByteOffset;
			return Encoding.ASCII.GetString(buffer);
		}

		private void Write(int offset, int value)
		{
			_memoryMappedViewAccessor.Write(offset, value);
		}

		private void Write(ref int offset, int value)
		{
			Write(offset, value);
			offset += Constants.IntOffset;
		}

		private void Write(int offset, double value)
		{
			_memoryMappedViewAccessor.Write(offset, value);
		}

		private void Write(ref int offset, double value)
		{
			Write(offset, value);
			offset += Constants.DoubleOffset;
		}

		private void Write(ref int offset, string value)
		{
			var buffer = Encoding.ASCII.GetBytes(value);
			Write(ref offset, buffer.Length);
			_memoryMappedViewAccessor.WriteArray(offset, buffer, 0, buffer.Length);
			offset += buffer.Length * Constants.ByteOffset;
		}
	}
}