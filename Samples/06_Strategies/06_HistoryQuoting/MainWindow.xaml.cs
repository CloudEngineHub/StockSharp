﻿namespace StockSharp.Samples.Strategies.HistoryQuoting;

using System;
using System.Windows;
using System.Windows.Media;

using Ecng.Common;
using Ecng.Collections;
using Ecng.Drawing;
using Ecng.Logging;

using StockSharp.Algo.Candles;
using StockSharp.Algo.Commissions;
using StockSharp.Algo.Storages;
using StockSharp.Algo.Strategies;
using StockSharp.Algo.Testing;
using StockSharp.BusinessEntities;
using StockSharp.Messages;
using StockSharp.Xaml.Charting;
using StockSharp.Configuration;
using StockSharp.Xaml;
using StockSharp.Charting;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
	private HistoryEmulationConnector _connector;
	private ChartCandleElement _candleElement;
	private ChartTradeElement _tradesElem;
	private CandleSeries _candleSeries;
	private Security _security;
	private Portfolio _portfolio;
	private readonly LogManager _logManager;
	private Strategy _strategy;
	private readonly string _pathHistory = Paths.HistoryDataPath;
	private ChartBandElement _pnl;
	private ChartBandElement _unrealizedPnL;
	private ChartBandElement _commissionCurve;
	public MainWindow()
	{
		InitializeComponent();

		_logManager = new LogManager();
		_logManager.Listeners.Add(new FileLogListener("log.txt"));
		_logManager.Listeners.Add(new GuiLogListener(Monitor));

		DatePickerBegin.SelectedDate = Paths.HistoryBeginDate;
		DatePickerEnd.SelectedDate = Paths.HistoryEndDate;

		CandleSettingsEditor.DataType = DataType.TimeFrame(TimeSpan.FromMinutes(1));
	}

	private void Start_Click(object sender, RoutedEventArgs e)
	{

		_security = new Security
		{
			Id = "SBER@TQBR",
			Code = "SBER",
			PriceStep = 0.01m,
			Board = ExchangeBoard.Micex
		};
		_portfolio = new Portfolio { Name = "test account", BeginValue = 1000000 };
		var storageRegistry = new StorageRegistry
		{
			DefaultDrive = new LocalMarketDataDrive(_pathHistory),
		};

		_connector = new HistoryEmulationConnector(new[] { _security }, new[] { _portfolio })
		{
			HistoryMessageAdapter =
			{
				StorageRegistry = storageRegistry,
				StorageFormat = StorageFormats.Binary,
				StartDate = DatePickerBegin.SelectedDate.Value.ChangeKind(DateTimeKind.Utc),
				StopDate = DatePickerEnd.SelectedDate.Value.ChangeKind(DateTimeKind.Utc),
			},
			LogLevel = LogLevels.Info,
		};

		_logManager.Sources.Add(_connector);

		_candleSeries = CandleSettingsEditor.DataType.ToCandleSeries(_security);

		// ready-to-use candles much faster than compression on fly mode
		// turn off compression to boost optimizer (!!! make sure you have candles)
		//_candleSeries.BuildCandlesMode = MarketDataBuildModes.Build;
		//_candleSeries.BuildCandlesFrom2 = DataType.Ticks;

		InitChart();

		_connector.CandleProcessing += Connector_CandleSeriesProcessing;
		_connector.OrderBookReceived += (s, b) => MarketDepthControl.UpdateDepth(b);
		_connector.NewOrder += OrderGrid.Orders.Add;
		_connector.OrderRegisterFailed += OrderGrid.AddRegistrationFail;

		_strategy = new StairsCountertrendStrategy(_candleSeries)
		{
			Security = _security,
			Connector = _connector,
			Portfolio = _portfolio,
		};
		_logManager.Sources.Add(_strategy);
		_strategy.NewMyTrade += MyTradeGrid.Trades.Add;
		_strategy.NewMyTrade += FirstStrategy_NewMyTrade;
		_strategy.PnLChanged += Strategy_PnLChanged;

		StatisticParameterGrid.Parameters.AddRange(_strategy.StatisticManager.Parameters);

		_connector.Connected += Connector_Connected;
		_connector.Connect();
		_connector.SendInMessage(new CommissionRuleMessage
		{
			Rule = new CommissionPerTradeRule { Value = 0.01m }
		});
	}

	private void InitChart()
	{
		//-----------------Chart--------------------------------
		Chart.ClearAreas();

		var area = new ChartArea();
		_candleElement = new ChartCandleElement();
		_tradesElem = new ChartTradeElement { FullTitle = "Trade" };

		Chart.AddArea(area);
		Chart.AddElement(area, _candleElement);
		Chart.AddElement(area, _tradesElem);

		_pnl = (ChartBandElement)EquityCurveChart.CreateCurve("PNL", Colors.Green, DrawStyles.Area);
		_unrealizedPnL = (ChartBandElement)EquityCurveChart.CreateCurve("unrealizedPnL", Colors.Black, DrawStyles.Line);
		_commissionCurve = (ChartBandElement)EquityCurveChart.CreateCurve("commissionCurve", Colors.Red, DrawStyles.Line);
	}

	private void Connector_Connected()
	{
		_strategy.Start();
		_connector.Start();
	}

	private void Strategy_PnLChanged()
	{
		var data = new ChartDrawData();
		data.Group(_strategy.CurrentTime)
			.Add(_pnl, _strategy.PnL)
			.Add(_unrealizedPnL, _strategy.PnLManager.UnrealizedPnL)
			.Add(_commissionCurve, _strategy.Commission ?? 0);
		EquityCurveChart.Draw(data);
	}

	private void FirstStrategy_NewMyTrade(MyTrade myTrade)
	{
		var data = new ChartDrawData();
		data.Group(myTrade.Trade.ServerTime)
			.Add(_tradesElem, myTrade);
		Chart.Draw(data);
	}

	private void Connector_CandleSeriesProcessing(CandleSeries candleSeries, ICandleMessage candle)
	{
		var data = new ChartDrawData();
		data.Group(candle.OpenTime)
			.Add(_candleElement, candle);
		Chart.Draw(data);
	}
}

