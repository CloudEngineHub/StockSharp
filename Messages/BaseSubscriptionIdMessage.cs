namespace StockSharp.Messages
{
	using System;
	using System.Runtime.Serialization;
	using System.Xml.Serialization;

	/// <summary>
	/// A message containing subscription identifiers.
	/// </summary>
	/// <typeparam name="TMessage">Message type.</typeparam>
	[System.Runtime.Serialization.DataContract]
	[Serializable]
	public abstract class BaseSubscriptionIdMessage<TMessage> : Message, ISubscriptionIdMessage
		where TMessage : BaseSubscriptionIdMessage<TMessage>, new()
	{
		/// <inheritdoc />
		[DataMember]
		public long OriginalTransactionId { get; set; }

		/// <inheritdoc />
		[XmlIgnore]
		public long SubscriptionId { get; set; }

		/// <inheritdoc />
		[XmlIgnore]
		public long[] SubscriptionIds { get; set; }

		/// <inheritdoc />
		public abstract DataType DataType { get; }

		/// <summary>
		/// Initialize <see cref="BaseSubscriptionIdMessage{TMessage}"/>.
		/// </summary>
		/// <param name="type">Message type.</param>
		protected BaseSubscriptionIdMessage(MessageTypes type)
			: base(type)
		{
		}

		/// <summary>
		/// Copy the message into the <paramref name="destination" />.
		/// </summary>
		/// <param name="destination">The object, to which copied information.</param>
		public virtual void CopyTo(TMessage destination)
		{
			base.CopyTo(destination);

			destination.OriginalTransactionId = OriginalTransactionId;
			destination.SubscriptionId = SubscriptionId;
			destination.SubscriptionIds = SubscriptionIds;//?.ToArray();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			var str = base.ToString();

			if (OriginalTransactionId != 0)
				str += $",OriginId={OriginalTransactionId}";

			return str;
		}

		/// <summary>
		/// Create a copy of <see cref="BaseSubscriptionIdMessage{TMessage}"/>.
		/// </summary>
		/// <returns>Copy.</returns>
		public override Message Clone()
		{
			var clone = new TMessage();
			CopyTo(clone);
			return clone;
		}
	}
}