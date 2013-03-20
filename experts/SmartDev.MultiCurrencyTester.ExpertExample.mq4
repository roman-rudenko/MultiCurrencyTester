#include <SmartDev.MultiCurrencyTester.Connect.mqh>

extern int MagicNumber = 0;

int init()
{
	if (IsTesting())
	{
		InitializeTestAPI(0, 1, 60, "");
		DeclareVariable("TotalProfitVariableName", VariableOperation_Sum);
		DeclareVariable("OrdersCountVariableName", VariableOperation_Sum);
		DeclareVariable("vGrafBalance", VariableOperation_Sum);
		DeclareVariable("vGrafEquity", VariableOperation_Sum);
	}
	return (0);
}

int deinit()
{
	if (IsTesting())
	{
		DeinitializeTestAPI();
	}
	return (0);
}

int start()
{
	if (IsTesting())
	{
		NextTick(TimeCurrent(), AccountBalance(), AccountEquity());
		SetVariable("vGrafBalance", AccountBalance());
		SetVariable("vGrafEquity", AccountEquity());
		GlobalVariableSet("vGrafBalance", GetVariable("vGrafBalance"));
		GlobalVariableSet("vGrafEquity", GetVariable("vGrafEquity"));
	}
	
	double varValue = GlobalVariableGetExt("VarName");
	GlobalVariableSetExt("VarName", 10);
	
	double totalProfit = GetTotalProfit();
	int ordersCount = GetOrdersCount();

	return (0);
}

double GetTotalProfit()
{
	double result = 0;
	int ordersTotal = OrdersTotal();
	if (ordersTotal > 0)
	{
		for (int i = 0; i < ordersTotal; i++)
		{
			OrderSelect(i, SELECT_BY_POS, MODE_TRADES);
			if (OrderMagicNumber() == MagicNumber)
			{
				result += OrderProfit() + OrderSwap() + OrderCommission();
			}
		}
	}
	
	if (IsTesting())
	{
		SetVariable("TotalProfitVariableName", result);
		return (GetVariable("TotalProfitVariableName"));
	}
	
	return (result);
}

int GetOrdersCount()
{
	int result = 0;
	int ordersTotal = OrdersTotal();
	if (ordersTotal > 0)
	{
		for (int i = 0; i < ordersTotal; i++)
		{
			OrderSelect(i, SELECT_BY_POS, MODE_TRADES);
			if (OrderMagicNumber() == MagicNumber) result++;
		}
	}

	if (IsTesting())
	{
		SetVariable("OrdersCountVariableName", result);
		return (GetVariable("OrdersCountVariableName"));
	}

	return (result);
}

