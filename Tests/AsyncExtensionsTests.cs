namespace StockSharp.Tests;

[TestClass]
public class AsyncExtensionsTests : BaseTestClass
{
	private class MockAsyncAdapter : MessageAdapter
	{
		public List<Message> SentMessages { get; } = [];
		public Dictionary<long, MarketDataMessage> ActiveSubscriptions { get; } = [];
		public long LastSubscribedId { get; private set; }

		public MockAsyncAdapter(IdGenerator transactionIdGenerator) : base(transactionIdGenerator)
		{
			this.AddMarketDataSupport();
			this.AddTransactionalSupport();
		}

		protected override bool OnSendInMessage(Message message)
		{
			SentMessages.Add(message);

			switch (message.Type)
			{
				case MessageTypes.Connect:
				{
					SendOutMessage(new ConnectMessage());
					break;
				}

				case MessageTypes.Disconnect:
				{
					SendOutMessage(new DisconnectMessage());
					break;
				}

				case MessageTypes.MarketData:
				{
					var mdMsg = (MarketDataMessage)message;

					if (mdMsg.IsSubscribe)
					{
						// ack subscribe
						SendOutMessage(mdMsg.CreateResponse());

						ActiveSubscriptions[mdMsg.TransactionId] = mdMsg;
						LastSubscribedId = mdMsg.TransactionId;

						SendSubscriptionResult(mdMsg);
					}
					else
					{
						ActiveSubscriptions.Remove(mdMsg.OriginalTransactionId);
						// ack unsubscribe
						SendOutMessage(mdMsg.CreateResponse());
					}

					break;
				}

				case MessageTypes.Reset:
				{
					ActiveSubscriptions.Clear();
					break;
				}
			}

			return true;
		}

		public void SimulateData(long subscriptionId, Message data)
		{
			if (data is ISubscriptionIdMessage sid)
				sid.SetSubscriptionIds([subscriptionId]);

			SendOutMessage(data);
		}
	}

	private class MockAsyncAdapterAsync : HistoricalAsyncMessageAdapter
	{
		public List<Message> SentMessages { get; } = [];
		public Dictionary<long, MarketDataMessage> ActiveSubscriptions { get; } = [];
		public long LastSubscribedId { get; private set; }

		public MockAsyncAdapterAsync(IdGenerator transactionIdGenerator) : base(transactionIdGenerator)
		{
			this.AddMarketDataSupport();
			this.AddTransactionalSupport();
		}

		public override ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
		{
			SendOutMessage(new ConnectMessage());
			return default;
		}

		public override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
		{
			SendOutMessage(new DisconnectMessage());
			return default;
		}

		protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		{
			SentMessages.Add(mdMsg);

			if (mdMsg.IsSubscribe)
			{
				SendSubscriptionReply(mdMsg.TransactionId);

				ActiveSubscriptions[mdMsg.TransactionId] = mdMsg;
				LastSubscribedId = mdMsg.TransactionId;

				SendSubscriptionResult(mdMsg);
			}
			else
			{
				ActiveSubscriptions.Remove(mdMsg.OriginalTransactionId);
				SendSubscriptionReply(mdMsg.OriginalTransactionId);
			}

			return default;
		}

		public void SimulateData(long subscriptionId, Message data)
		{
			if (data is ISubscriptionIdMessage sid)
				sid.SetSubscriptionIds([subscriptionId]);
			SendOutMessage(data);
		}

		public override IMessageChannel Clone() => new MockAsyncAdapterAsync(TransactionIdGenerator);
	}

	[TestMethod]
	public async Task Connector_ConnectAsync()
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		var connector = new Connector();
		var adapter = new MockAsyncAdapter(connector.TransactionIdGenerator);
		connector.Adapter.InnerAdapters.Add(adapter);

		await connector.ConnectAsync(cts.Token);
		Assert.AreEqual(ConnectionStates.Connected, connector.ConnectionState);

		await connector.DisconnectAsync(cts.Token);
		Assert.AreEqual(ConnectionStates.Disconnected, connector.ConnectionState);
	}

	[TestMethod]
	public async Task Subscription_Live_SyncAdapter()
	{
		var token = CancellationToken;
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var connector = new Connector();
		var adapter = new MockAsyncAdapter(connector.TransactionIdGenerator);
		connector.Adapter.InnerAdapters.Add(adapter);

		await connector.ConnectAsync(cts.Token);

		var sub = new Subscription(DataType.Level1);

		var got = new List<Level1ChangeMessage>();
		using var enumCts = new CancellationTokenSource();

		var enumerating = Task.Run(async () =>
		{
			await foreach (var l1 in connector.SubscribeAsync<Level1ChangeMessage>(sub, enumCts.Token))
			{
				got.Add(l1);
				if (got.Count >= 3)
					break;
			}
		}, token);

		// wait until subscription ID assigned by adapter
		await Task.Run(async () =>
		{
			while (adapter.ActiveSubscriptions.Count == 0)
				await Task.Delay(10, cts.Token);
		}, cts.Token);

		var id = adapter.LastSubscribedId;

		for (var i = 0; i < 3; i++)
		{
			var l1 = new Level1ChangeMessage { ServerTime = DateTimeOffset.UtcNow };
			adapter.SimulateData(id, l1);
		}

		enumCts.Cancel();
		await enumerating.WithTimeout(TimeSpan.FromSeconds(5));

		Assert.HasCount(3, got);
		Assert.IsTrue(adapter.SentMessages.OfType<MarketDataMessage>().Any(m => !m.IsSubscribe && m.OriginalTransactionId == id));
	}

	[TestMethod]
	public async Task Subscription_History_SyncAdapter()
	{
		var token = CancellationToken;
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var connector = new Connector();
		var adapter = new MockAsyncAdapter(connector.TransactionIdGenerator);
		connector.Adapter.InnerAdapters.Add(adapter);

		await connector.ConnectAsync(cts.Token);

		var sub = new Subscription(DataType.Level1)
		{
			From = DateTimeOffset.UtcNow.AddDays(-2),
			To = DateTimeOffset.UtcNow.AddDays(-1),
		};

		var got = new List<Level1ChangeMessage>();
		using var enumCts = new CancellationTokenSource();

		var enumerating = Task.Run(async () =>
		{
			await foreach (var l1 in connector.SubscribeAsync<Level1ChangeMessage>(sub, enumCts.Token))
			{
				got.Add(l1);
				if (got.Count >= 2)
					break;
			}
		}, token);

		await Task.Run(async () =>
		{
			while (adapter.ActiveSubscriptions.Count == 0)
				await Task.Delay(10, cts.Token);
		}, cts.Token);

		var id = adapter.LastSubscribedId;

		for (var i = 0; i < 2; i++)
		{
			var l1 = new Level1ChangeMessage { ServerTime = DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(i) };
			adapter.SimulateData(id, l1);
		}

		enumCts.Cancel();
		await enumerating.WithTimeout(TimeSpan.FromSeconds(5));

		Assert.HasCount(2, got);
		Assert.IsTrue(adapter.SentMessages.OfType<MarketDataMessage>().Any(m => !m.IsSubscribe && m.OriginalTransactionId == id));
	}

	[TestMethod]
	public async Task Subscription_Live_AsyncAdapter()
	{
		var token = CancellationToken;
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var connector = new Connector();
		var adapter = new MockAsyncAdapterAsync(connector.TransactionIdGenerator);
		connector.Adapter.InnerAdapters.Add(adapter);

		await connector.ConnectAsync(cts.Token);

		var sub = new Subscription(DataType.Level1);

		var got = new List<Level1ChangeMessage>();
		using var enumCts = new CancellationTokenSource();

		var enumerating = Task.Run(async () =>
		{
			await foreach (var l1 in connector.SubscribeAsync<Level1ChangeMessage>(sub, enumCts.Token))
			{
				got.Add(l1);
				if (got.Count >= 3)
					break;
			}
		}, token);

		await Task.Run(async () =>
		{
			while (adapter.ActiveSubscriptions.Count == 0)
				await Task.Delay(10, cts.Token);
		}, cts.Token);

		var id = adapter.LastSubscribedId;

		for (var i = 0; i < 3; i++)
		{
			var l1 = new Level1ChangeMessage { ServerTime = DateTimeOffset.UtcNow };
			adapter.SimulateData(id, l1);
		}

		enumCts.Cancel();
		await enumerating.WithTimeout(TimeSpan.FromSeconds(5));

		Assert.HasCount(3, got);
	}

	[TestMethod]
	public async Task Subscription_History_AsyncAdapter()
	{
		var token = CancellationToken;
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var connector = new Connector();
		var adapter = new MockAsyncAdapterAsync(connector.TransactionIdGenerator);
		connector.Adapter.InnerAdapters.Add(adapter);

		await connector.ConnectAsync(cts.Token);

		var sub = new Subscription(DataType.Level1)
		{
			From = DateTimeOffset.UtcNow.AddDays(-2),
			To = DateTimeOffset.UtcNow.AddDays(-1),
		};

		var got = new List<Level1ChangeMessage>();
		using var enumCts = new CancellationTokenSource();

		var enumerating = Task.Run(async () =>
		{
			await foreach (var l1 in connector.SubscribeAsync<Level1ChangeMessage>(sub, enumCts.Token))
			{
				got.Add(l1);
				if (got.Count >= 2)
					break;
			}
		}, token);

		await Task.Run(async () =>
		{
			while (adapter.ActiveSubscriptions.Count == 0)
				await Task.Delay(10, cts.Token);
		}, cts.Token);

		var id = adapter.LastSubscribedId;

		for (var i = 0; i < 2; i++)
		{
			var l1 = new Level1ChangeMessage { ServerTime = DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(i) };
			adapter.SimulateData(id, l1);
		}

		enumCts.Cancel();
		await enumerating.WithTimeout(TimeSpan.FromSeconds(5));

		Assert.HasCount(2, got);
	}

	[TestMethod]
	public async Task Subscription_Lifecycle()
	{
		using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var connector = new Connector();
		var adapter = new MockAsyncAdapter(connector.TransactionIdGenerator);
		connector.Adapter.InnerAdapters.Add(adapter);

		await connector.ConnectAsync(cts.Token);

		var sub = new Subscription(DataType.Level1);

		var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		connector.SubscriptionStarted += s => { if (ReferenceEquals(s, sub)) started.TrySetResult(true); };

		using var runCts = new CancellationTokenSource();
		var run = connector.SubscribeAsync(sub, runCts.Token).AsTask();

		await started.Task.WithTimeout(TimeSpan.FromSeconds(3));

		var id = adapter.LastSubscribedId;
		Assert.IsGreaterThan(0, id);

		// cancel -> triggers UnSubscribe and completes after stop
		runCts.Cancel();
		await run.WithTimeout(TimeSpan.FromSeconds(3));

		Assert.IsTrue(adapter.SentMessages.OfType<MarketDataMessage>().Any(m => !m.IsSubscribe && m.OriginalTransactionId == id));
	}
}

static class TestTaskExtensions
{
	public static async Task WithTimeout(this Task task, TimeSpan timeout)
	{
		using var cts = new CancellationTokenSource(timeout);
		var delay = Task.Delay(Timeout.Infinite, cts.Token);
		var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
		if (completed == delay)
			throw new TimeoutException("Task did not complete in time.");
		await task.ConfigureAwait(false);
	}

	public static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeout)
	{
		using var cts = new CancellationTokenSource(timeout);
		var delay = Task.Delay(Timeout.Infinite, cts.Token);
		var completed = await Task.WhenAny(task, delay).ConfigureAwait(false);
		if (completed == delay)
			throw new TimeoutException("Task did not complete in time.");
		return await task.ConfigureAwait(false);
	}
}
