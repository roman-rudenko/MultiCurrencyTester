using RGiesecke.DllExport;
using System.Runtime.InteropServices;
using System;
using System.IO;

namespace SmartDev.MultiCurrencyTester.Connect
{
	public static class ConnectAPI
	{
		private static ConnectImpl _impl;

		[DllExport("InitializeTestAPI", CallingConvention = CallingConvention.Cdecl)]
		public static void InitializeTestAPI(int instanceId, int instancesCount)
		{
			try
			{
				if (_impl == null)
				{
					_impl = new ConnectImpl();
					_impl.InitializeTestAPI(instanceId, instancesCount);
				}
			}
			catch (Exception ex)
			{
				File.AppendAllText("SmartDev.MultiCurrencyTester.Connect.log", ex.Message + "\r\n" + ex.StackTrace);
			}
		}

		[DllExport("DeinitializeTestAPI", CallingConvention = CallingConvention.Cdecl)]
		public static void DeinitializeTestAPI()
		{
			try
			{
				if (_impl != null)
				{
					_impl.DeinitializeTestAPI();
					_impl.Dispose();
					_impl = null;
				}
			}
			catch (Exception ex)
			{
				File.AppendAllText("SmartDev.MultiCurrencyTester.Connect.log", ex.Message + "\r\n" + ex.StackTrace);
			}
		}

		[DllExport("NextTick", CallingConvention = CallingConvention.Cdecl)]
		public static void NextTick(int tick, double balance, double equity)
		{
			try
			{
				if (_impl != null)
				{
					_impl.NextTick(tick, balance, equity);
				}
			}
			catch (Exception ex)
			{
				File.AppendAllText("SmartDev.MultiCurrencyTester.Connect.log", ex.Message + "\r\n" + ex.StackTrace);
			}
		}

		[DllExport("DeclareVariable", CallingConvention = CallingConvention.Cdecl)]
		public static void DeclareVariable(string variableName, VariableOperations variableOperation)
		{
			try
			{
				if (_impl != null)
				{
					_impl.DeclareVariable(variableName, variableOperation);
				}
			}
			catch (Exception ex)
			{
				File.AppendAllText("SmartDev.MultiCurrencyTester.Connect.log", ex.Message + "\r\n" + ex.StackTrace);
			}
		}

		[DllExport("GetVariable", CallingConvention = CallingConvention.Cdecl)]
		public static double GetVariable(string variableName)
		{
			try
			{
				if (_impl != null)
				{
					return _impl.GetVariable(variableName);
				}
			}
			catch (Exception ex)
			{
				File.AppendAllText("SmartDev.MultiCurrencyTester.Connect.log", ex.Message + "\r\n" + ex.StackTrace);
			}
			return 0;
		}

		[DllExport("SetVariable", CallingConvention = CallingConvention.Cdecl)]
		public static void SetVariable(string variableName, double variableValue)
		{
			try
			{
				if (_impl != null)
				{
					_impl.SetVariable(variableName, variableValue);
				}
			}
			catch (Exception ex)
			{
				File.AppendAllText("SmartDev.MultiCurrencyTester.Connect.log", ex.Message + "\r\n" + ex.StackTrace);
			}
		}
	}
}
