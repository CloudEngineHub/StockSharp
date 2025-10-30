namespace StockSharp.Coinbase;

public partial class CoinbaseMessageAdapter
{
	private string PortfolioName => nameof(Coinbase) + "_" + Key.ToId();

	/// <inheritdoc />
	public override async ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken)
	{
		var condition = (CoinbaseOrderCondition)regMsg.Condition;

		switch (regMsg.OrderType)
		{
			case null:
			case OrderTypes.Limit:
			case OrderTypes.Market:
				break;
			case OrderTypes.Conditional:
			{
				if (!condition.IsWithdraw)
					break;

				var withdrawId = await _restClient.Withdraw(regMsg.SecurityId.SecurityCode, regMsg.Volume, condition.WithdrawInfo, cancellationToken);

				SendOutMessage(new ExecutionMessage
				{
					DataTypeEx = DataType.Transactions,
					OrderStringId = withdrawId,
					ServerTime = CurrentTime.ConvertToUtc(),
					OriginalTransactionId = regMsg.TransactionId,
					OrderState = OrderStates.Done,
					HasOrderInfo = true,
				});

				await PortfolioLookupAsync(null, cancellationToken);
				return;
			}
			default:
				throw new NotSupportedException(LocalizedStrings.OrderUnsupportedType.Put(regMsg.OrderType, regMsg.TransactionId));
		}

		var isMarket = regMsg.OrderType == OrderTypes.Market;
		var price = isMarket ? (decimal?)null : regMsg.Price;
		
		var result = await _restClient.RegisterOrder(
			regMsg.TransactionId.To<string>(), regMsg.SecurityId.ToSymbol(),
			regMsg.OrderType.ToNative(), regMsg.Side.ToNative(), price,
			condition?.StopPrice, regMsg.Volume, regMsg.TimeInForce,
			regMsg.TillDate.EnsureToday(), regMsg.Leverage, cancellationToken);

		var orderState = result.Status.ToOrderState();

		if (orderState == OrderStates.Failed)
		{
			SendOutMessage(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				ServerTime = result.CreationTime,
				OriginalTransactionId = regMsg.TransactionId,
				OrderState = OrderStates.Failed,
				Error = new InvalidOperationException(),
				HasOrderInfo = true,
			});
		}
	}

	/// <inheritdoc />
	public override async ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		if (cancelMsg.OrderStringId.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.OrderNoExchangeId.Put(cancelMsg.OriginalTransactionId));

		await _restClient.CancelOrder(cancelMsg.OrderStringId, cancellationToken);
	}

	/// <inheritdoc />
	public override async ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken)
	{
		await _restClient.EditOrder(replaceMsg.OldOrderId.To<string>(), replaceMsg.Price, replaceMsg.Volume, cancellationToken);
	}

	/// <inheritdoc />
	public override async ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken)
	{
		var errors = new List<Exception>();

		// Handle CancelOrders mode
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.CancelOrders))
		{
			// Get all active orders
			var orders = await _restClient.GetOrders(cancellationToken);

			foreach (var order in orders)
			{
				var secId = order.Product.ToStockSharp();

				// If SecurityId is specified, cancel only orders for that security
				if (cancelMsg.SecurityId != default && cancelMsg.SecurityId != secId)
					continue;

				// Check Side filter
				if (cancelMsg.Side != null && cancelMsg.Side != order.Side.ToSide())
					continue;

				try
				{
					await _restClient.CancelOrder(order.Id, cancellationToken);
				}
				catch (Exception ex)
				{
					this.AddErrorLog($"Failed to cancel order {order.Id}: {ex.Message}");
					errors.Add(ex);
				}
			}
		}

		// Handle ClosePositions mode
		if (cancelMsg.Mode.HasFlag(OrderGroupCancelModes.ClosePositions))
		{
			// Get current balances to determine positions
			var accounts = await _restClient.GetAccounts(cancellationToken);

			foreach (var account in accounts)
			{
				var available = (decimal)account.Available;

				if (available <= 0)
					continue;

				var secId = new SecurityId
				{
					SecurityCode = account.Currency,
					BoardCode = BoardCodes.Coinbase,
				};

				// If SecurityId is specified, close only that position
				if (cancelMsg.SecurityId != default && cancelMsg.SecurityId != secId)
					continue;

				// Skip USD and stablecoins as they are base currencies
				if (account.Currency.EqualsIgnoreCase("USD") ||
				    account.Currency.EqualsIgnoreCase("USDT") ||
				    account.Currency.EqualsIgnoreCase("USDC"))
					continue;

				// Check Side filter - spot balances are always long positions
				if (cancelMsg.Side != null && cancelMsg.Side != Sides.Sell)
					continue;

				try
				{
					// Create market sell order to close the position
					var product = $"{account.Currency}-USD";

					OrderTypes? orderType = OrderTypes.Market;
					await _restClient.RegisterOrder(
						TransactionIdGenerator.GetNextId().To<string>(),
						product,
						orderType.ToNative(),
						Sides.Sell.ToNative(),
						null, // market order
						null, // no stop price
						available,
						null, // default TIF
						default,
						null, // no leverage
						cancellationToken);
				}
				catch (Exception ex)
				{
					this.AddErrorLog($"Failed to close position for {account.Currency}: {ex.Message}");
					errors.Add(ex);
				}
			}
		}

		// Send result with errors if any
		if (errors.Count > 0)
		{
			SendOutMessage(new ExecutionMessage
			{
				DataTypeEx = DataType.Transactions,
				OriginalTransactionId = cancelMsg.TransactionId,
				ServerTime = CurrentTime.ConvertToUtc(),
				HasOrderInfo = true,
				Error = errors.Count == 1 ? errors[0] : new AggregateException(errors),
			});
		}
	}

	/// <inheritdoc />
	public override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		var transId = lookupMsg.TransactionId;

		SendSubscriptionReply(transId);

		if (!lookupMsg.IsSubscribe)
			return;

		SendOutMessage(new PortfolioMessage
		{
			PortfolioName = PortfolioName,
			BoardCode = BoardCodes.Coinbase,
			OriginalTransactionId = transId,
		});

		var accounts = await _restClient.GetAccounts(cancellationToken);

		foreach (var account in accounts)
		{
			//var currency = account.Currency;

			//if (!currency.StartsWithIgnoreCase("USD") &&
			//    !currency.StartsWithIgnoreCase("EUR"))
			//{
			//	currency += "-USD";
			//}

			SendOutMessage(new PositionChangeMessage
			{
				//PortfolioName = account.Id.To<string>(),
				PortfolioName = PortfolioName,
				SecurityId = new SecurityId
				{
					SecurityCode = account.Currency,
					BoardCode = BoardCodes.Coinbase,
				},
				ServerTime = CurrentTime.ConvertToUtc(),
			}
			.TryAdd(PositionChangeTypes.CurrentValue, (decimal)account.Available, true)
			.TryAdd(PositionChangeTypes.BlockedValue, (decimal)account.Hold, true));
		}

		SendSubscriptionResult(lookupMsg);
	}

	/// <inheritdoc />
	public override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		SendSubscriptionReply(statusMsg.TransactionId);

		if (!statusMsg.IsSubscribe)
			return;

		var orders = await _restClient.GetOrders(cancellationToken);

		foreach (var order in orders)
			ProcessOrder(order, statusMsg.TransactionId);

		if (!statusMsg.IsHistoryOnly())
			await _socketClient.SubscribeOrders(statusMsg.TransactionId, cancellationToken);

		SendSubscriptionResult(statusMsg);
	}

	private void ProcessOrder(Order order, long originTransId)
	{
		if (!long.TryParse(order.ClientOrderId, out var transId))
			return;

		var state = order.Status.ToOrderState();

		SendOutMessage(new ExecutionMessage
		{
			ServerTime = originTransId == 0 ? CurrentTime.ConvertToUtc() : order.CreationTime,
			DataTypeEx = DataType.Transactions,
			SecurityId = order.Product.ToStockSharp(),
			TransactionId = originTransId== 0 ? 0 : transId,
			OriginalTransactionId = originTransId,
			OrderState = state,
			Error = state == OrderStates.Failed ? new InvalidOperationException() : null,
			OrderType = order.Type.ToOrderType(),
			Side = order.Side.ToSide(),
			OrderStringId = order.Id,
			OrderPrice = order.Price?.ToDecimal() ?? 0,
			OrderVolume = order.Size?.ToDecimal(),
			TimeInForce = order.TimeInForce.ToTimeInForce(),
			Balance = (decimal?)order.LeavesQuantity,
			HasOrderInfo = true,
		});
	}

	private void SessionOnOrderReceived(Order order)
		=> ProcessOrder(order, 0);
}