using System.Collections.Generic;

namespace SmartDev.MultiCurrencyTester.Connect
{
	public enum InstanceStatus
	{
		NotInitialized = 0,
		Initialized = 1,
		Deinitialized = 2
	}

	public enum VariableOperations
	{
		Nothing = 0,
		Sum = 1
	}

	public class VariableConract
	{
		public int InstaceId { get; set; }
		public int Tick { get; set; }
		public string Name { get; set; }
		public double Value { get; set; }
	}

	public class MemoryContract
	{
		public MemoryContract()
		{
			InstancesStatuses = new Dictionary<int, InstanceStatus>();
			InstancesTime = new Dictionary<int, int>();
			Variables = new List<VariableConract>();
			VariableOperations = new Dictionary<string, VariableOperations>();
		}

		public int InstancesCount { get; set; }
		public Dictionary<int, InstanceStatus> InstancesStatuses { get; set; }	// <InstanceId, InstanceStatus>
		public Dictionary<int, int> InstancesTime { get; set; }					// <InstanceId, Tick>
		public List<VariableConract> Variables { get; set; }
		public Dictionary<string, VariableOperations> VariableOperations { get; set; } // <VariableName, VariableOperations>
	}
}
