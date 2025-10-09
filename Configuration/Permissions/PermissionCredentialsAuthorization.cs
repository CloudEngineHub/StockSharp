namespace StockSharp.Configuration.Permissions;

using System.Net;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Security;

/// <summary>
/// The module of the connection access check based on the <see cref="IPermissionCredentialsStorage"/> authorization.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PermissionCredentialsAuthorization"/>.
/// </remarks>
/// <param name="storage">Storage for <see cref="PermissionCredentials"/>.</param>
public class PermissionCredentialsAuthorization(IPermissionCredentialsStorage storage) : IAuthorization
{
	private readonly IPermissionCredentialsStorage _storage = storage ?? throw new ArgumentNullException(nameof(storage));

	ValueTask<string> IAuthorization.ValidateCredentials(string login, SecureString password, IPAddress clientAddress, CancellationToken cancellationToken)
	{
		if (login.IsEmpty())
			throw new ArgumentNullException(nameof(login), LocalizedStrings.LoginNotSpecified);

		if (password.IsEmpty())
			throw new ArgumentNullException(nameof(password), LocalizedStrings.PasswordNotSpecified);

		var credentials = _storage.TryGetByLogin(login);

		if (credentials == null || !credentials.Password.IsEqualTo(password))
			throw new UnauthorizedAccessException(LocalizedStrings.WrongLoginOrPassword);

		var ipRestrictions = credentials.IpRestrictions.ToArray();

		if (ipRestrictions.Length > 0 && (clientAddress == null || !ipRestrictions.Contains(clientAddress)))
			throw new UnauthorizedAccessException(LocalizedStrings.IpAddrNotValid.Put(clientAddress));

		return new(Guid.NewGuid().To<string>());
	}
}