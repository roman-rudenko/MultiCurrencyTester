namespace SmartDev.MultiCurrencyTester.Connect
{
	internal static class Constants
	{
		public const int IntOffset = sizeof(int);
		public const int DoubleOffset = sizeof(double);
		public const int ByteOffset = sizeof(byte);

		public const int InstancesCountOffset = 0;
		public const int VariablesLockOffset = InstancesCountOffset + IntOffset;
		public const int VariablesCountOffset = VariablesLockOffset + IntOffset;
		public const int InstancesStatusesOffset = VariablesCountOffset + IntOffset;

		public const long MemoryMappedFileSize = 0xA00000; // 10 megabytes
		public const string MemoryMappedFileName = "SmartDev.MultiCurrencyTester.MemoryMappedFile";
		public const string OnDataUpdatedEventName = "SmartDev.MultiCurrencyTester.OnDataUpdatedEvent";
		public const string OnWriteLockMutexName = "SmartDev.MultiCurrencyTester.OnWriteLockMutex";
	}
}