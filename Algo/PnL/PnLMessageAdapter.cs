namespace StockSharp.Algo.PnL;

/// <summary>
/// The message adapter, automatically calculating profit-loss.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PnLMessageAdapter"/>.
/// </remarks>
/// <param name="innerAdapter">The adapter, to which messages will be directed.</param>
public class PnLMessageAdapter(IMessageAdapter innerAdapter) : MessageAdapterWrapper(innerAdapter)
{
	private IPnLManager _pnLManager = new PnLManager();

	/// <summary>
	/// The profit-loss manager.
	/// </summary>
	public IPnLManager PnLManager
	{
		get => _pnLManager;
		set => _pnLManager = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <inheritdoc />
	protected override bool OnSendInMessage(Message message)
	{
		PnLManager.ProcessMessage(message);
		return base.OnSendInMessage(message);
	}

	/// <inheritdoc />
	protected override void OnInnerAdapterNewOutMessage(Message message)
	{
		if (message.Type != MessageTypes.Reset)
		{
			var list = new List<PortfolioPnLManager>();
			var info = PnLManager.ProcessMessage(message, list);

			if (info != null && info.PnL != 0)
				((ExecutionMessage)message).PnL = info.PnL;

			foreach (var manager in list)
			{
				base.OnInnerAdapterNewOutMessage(new PositionChangeMessage
				{
					SecurityId = SecurityId.Money,
					ServerTime = message.LocalTime,
					PortfolioName = manager.PortfolioName,
					BuildFrom = DataType.Transactions,
				}
				.Add(PositionChangeTypes.RealizedPnL, manager.RealizedPnL)
				.TryAdd(PositionChangeTypes.UnrealizedPnL, manager.UnrealizedPnL));
			}
		}

		base.OnInnerAdapterNewOutMessage(message);
	}

	/// <summary>
	/// Create a copy of <see cref="PnLMessageAdapter"/>.
	/// </summary>
	/// <returns>Copy.</returns>
	public override IMessageChannel Clone()
	{
		return new PnLMessageAdapter(InnerAdapter.TypedClone());
	}
}