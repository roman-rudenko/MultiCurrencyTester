#property indicator_separate_window
#property indicator_buffers 2
#property indicator_color1 Blue
#property indicator_color2 Red
 
double balance[];
double equity[];
 
int init()
{
    IndicatorShortName("EquityInTesting");
    IndicatorDigits(2);
 
    SetIndexStyle(0, DRAW_LINE);
    SetIndexBuffer(0, balance);
    SetIndexLabel(0, "Balance");
 
    SetIndexStyle(1, DRAW_LINE);
    SetIndexBuffer(1, equity);
    SetIndexLabel(1, "Equity");
}

int start()
{
    balance[0] = GlobalVariableGet("vGrafBalance");
    double equityCurrent = GlobalVariableGet("vGrafEquity");
    if (equity[0] == EMPTY_VALUE || equityCurrent < equity[0])
    {
	    equity[0] = equityCurrent;
    }
    return(0);
}

