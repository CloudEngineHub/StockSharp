namespace StockSharp.Algo.Commissions;

/// <summary>
/// The commission calculating rule interface.
/// </summary>
public interface ICommissionRule : IPersistable
{
	/// <summary>
	/// Title.
	/// </summary>
	string Title { get; }

	/// <summary>
	/// Commission value.
	/// </summary>
	Unit Value { get; }

	/// <summary>
	/// To reset the state.
	/// </summary>
	void Reset();

	/// <summary>
	/// To calculate commission.
	/// </summary>
	/// <param name="message">The message containing the information about the order or own trade.</param>
	/// <returns>The commission. If the commission cannot be calculated then <see langword="null" /> will be returned.</returns>
	decimal? Process(ExecutionMessage message);
}