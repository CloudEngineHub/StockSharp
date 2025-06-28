﻿namespace StockSharp.Algo.Indicators;

/// <summary>
/// Fibonacci Retracement.
/// </summary>
[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FRKey,
		Description = LocalizedStrings.FibonacciRetracementKey)]
[IndicatorIn(typeof(CandleIndicatorValue))]
[Doc("topics/api/indicators/list_of_indicators/fibonacci_retracement.html")]
[IndicatorOut(typeof(FibonacciRetracementValue))]
public class FibonacciRetracement : BaseComplexIndicator
{
	private readonly Highest _highest;
	private readonly Lowest _lowest;

	/// <summary>
	/// Initializes a new instance of the <see cref="FibonacciRetracement"/>.
	/// </summary>
	public FibonacciRetracement()
	{
		_highest = new();
		_lowest = new();

		Levels = [.. new[] { 0.236m, 0.382m, 0.5m, 0.618m, 0.786m }.Select(l => new FibonacciLevel(l))];

		foreach (var level in Levels)
			AddInner(level);

		Length = 20;
	}

	/// <summary>
	/// Fibonacci levels.
	/// </summary>
	[Browsable(false)]
	public FibonacciLevel[] Levels { get; }

	/// <summary>
	/// Period length.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PeriodKey,
		Description = LocalizedStrings.IndicatorPeriodKey,
		GroupName = LocalizedStrings.GeneralKey)]
	public int Length
	{
		get => _highest.Length;
		set
		{
			_highest.Length = value;
			_lowest.Length = value;

			Reset();
		}
	}

	/// <inheritdoc />
	protected override IIndicatorValue OnProcess(IIndicatorValue input)
	{
		var candle = input.ToCandle();

		var highValue = _highest.Process(input, candle.HighPrice);
		var lowValue = _lowest.Process(input, candle.LowPrice);

		if (input.IsFinal && _highest.IsFormed && _lowest.IsFormed)
			IsFormed = true;

		var result = new FibonacciRetracementValue(this, input.Time);

		var highestHigh = highValue.ToDecimal();
		var lowestLow = lowValue.ToDecimal();

		foreach (var level in Levels)
		{
			var levelValue = lowestLow + (highestHigh - lowestLow) * level.Level;
			result.Add(level, level.Process(input, levelValue));
		}

		return result;
	}

	/// <inheritdoc />
	public override void Reset()
	{
		_highest.Reset();
		_lowest.Reset();

		base.Reset();
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Length = storage.GetValue<int>(nameof(Length));
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage.SetValue(nameof(Length), Length);
	}

	/// <inheritdoc />
	public override string ToString() => base.ToString() + " " + Length;
}

/// <summary>
/// Represents a Fibonacci retracement level.
/// </summary>
[IndicatorHidden]
public class FibonacciLevel : BaseIndicator
{
	/// <summary>
	/// Initializes a new instance of the <see cref="FibonacciLevel"/>.
	/// </summary>
	/// <param name="level"><see cref="Level"/></param>
	public FibonacciLevel(decimal level)
	{
		Level = level;
	}

	/// <summary>
	/// The retracement level.
	/// </summary>
	public decimal Level { get; }

	/// <inheritdoc />
	protected override IIndicatorValue OnProcess(IIndicatorValue input)
	{
		if (input.IsFinal)
			IsFormed = true;

		return input;
	}
	/// <inheritdoc />
	protected override ComplexIndicatorValue CreateValue(DateTimeOffset time)
		=> new FibonacciRetracementValue(this, time);
}

/// <summary>
/// <see cref="FibonacciRetracement"/> indicator value.
/// </summary>
public class FibonacciRetracementValue : ComplexIndicatorValue<FibonacciRetracement>
{
	/// <summary>
	/// Initializes a new instance of the <see cref="FibonacciRetracementValue"/>.
	/// </summary>
	/// <param name="indicator"><see cref="FibonacciRetracement"/></param>
	/// <param name="time"><see cref="IIndicatorValue.Time"/></param>
	public FibonacciRetracementValue(FibonacciRetracement indicator, DateTimeOffset time)
		: base(indicator, time)
	{
	}

	/// <summary>
	/// Gets all level values.
	/// </summary>
	public decimal[] Levels => Indicator.Levels.Select(l => InnerValues[l].ToDecimal()).ToArray();
}
