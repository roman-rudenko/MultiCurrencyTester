using System.Collections.Generic;

namespace SmartDev.MultiCurrencyTester.Connect
{
	public enum InstanceStatus
	{
		Unknown = 0,
		NotInitialized = 1,
		Initialized = 2,
		Deinitialized = 3
	}

	public enum VariableOperations
	{
		Nothing = 0,
		Sum = 1
	}

	public class VariableConract
	{
		public string Name { get; set; }
		public VariableOperations Operation { get; set; }
	}

	//public class MemoryContract
	//{
	//    public MemoryContract()
	//    {
	//        Variables = new List<VariableConract>();
	//        VariableOperations = new Dictionary<string, VariableOperations>();
	//    }

	//    public List<VariableConract> Variables { get; set; }
	//    public Dictionary<string, VariableOperations> VariableOperations { get; set; } // <VariableName, VariableOperations>
	//}
}
