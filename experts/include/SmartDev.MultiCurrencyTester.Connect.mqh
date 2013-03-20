//+------------------------------------------------------------------+
//|                 SmartDev.MultiCurrencyTester.Connect.mqh 1.0.0.0 |
//|                                  Copyright © 2013, Roman Rudenko |
//|                                                                  |
//+------------------------------------------------------------------+
#define copyright "Copyright © 2013, Roman Rudenko"

#import "SmartDev.MultiCurrencyTester.Connect.dll"
	void InitializeTestAPI(int instanceId, int instancesCount, int syncSeconds, string logFilePath);
	void DeinitializeTestAPI();
	void NextTick(int tick, double balance, double equity);
	void DeclareVariable(string variableName, int variableOperation);
	double GetVariable(string variableName);
	void SetVariable(string variableName, double variableValue);
#import

int VariableOperation_Nothing	= 0;
int VariableOperation_Sum		= 1;

double GlobalVariableGetExt(string name)
{
	if (IsTesting())
	{
		return (GetVariable(name));
	}
	else
	{
		return (GlobalVariableGet(name));
	}
}

double GlobalVariableSetExt(string name, double value)
{
	if (IsTesting())
	{
		SetVariable(name, value);
	}
	else
	{
		GlobalVariableSet(name, value);
	}
}