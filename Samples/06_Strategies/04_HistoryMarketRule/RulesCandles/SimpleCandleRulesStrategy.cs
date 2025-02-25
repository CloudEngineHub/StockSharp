﻿using System;

using Ecng.Logging;

using StockSharp.Algo;
using StockSharp.BusinessEntities;
using StockSharp.Algo.Strategies;
using StockSharp.Messages;

namespace StockSharp.Samples.Strategies.HistoryMarketRule
{
	public class SimpleCandleRulesStrategy : Strategy
	{
		private Subscription _subscription;
		protected override void OnStarted(DateTimeOffset time)
		{
			_subscription = new(Security.TimeFrame(TimeSpan.FromMinutes(5)))
			{
				// ready-to-use candles much faster than compression on fly mode
				// turn off compression to boost optimizer (!!! make sure you have candles)

				//MarketData =
				//{
				//	BuildMode = MarketDataBuildModes.Build,
				//	BuildFrom = DataType.Ticks,
				//}
			};
			Subscribe(_subscription);

			var i = 0;

			this.WhenCandlesStarted(_subscription)
				.Do((candle) =>
				{
					i++;

					this
						.WhenTotalVolumeMore(candle, new Unit(100000m))
						.Do((candle1) =>
						{
							this.AddInfoLog($"The rule WhenPartiallyFinished and WhenTotalVolumeMore candle={candle1}");
							this.AddInfoLog($"The rule WhenPartiallyFinished and WhenTotalVolumeMore i={i}");
						}).Apply(this);

				}).Apply(this);

			base.OnStarted(time);
		}
	}
}