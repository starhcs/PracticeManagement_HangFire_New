using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Hangfire.Dashboard;
using Microsoft.Extensions.Options;

namespace HangfireNew.Security
{
    /// <summary>
    /// Gates the Hangfire dashboard behind HTTP Basic auth.
    ///
    /// Reads the "Authorization: Basic ..." header, decodes it and compares it against the
    /// configured <see cref="DashboardAuthOptions"/>. Anything that does not match - no header,
    /// a malformed header, wrong username, wrong password, or credentials not configured at all -
    /// gets a 401 with a "WWW-Authenticate: Basic" challenge, which is what makes the browser
    /// show its own login prompt.
    ///
    /// The comparison uses <see cref="CryptographicOperations.FixedTimeEquals"/> so response time
    /// does not leak how much of the password was correct.
    /// </summary>
    public class BasicAuthDashboardFilter : IDashboardAuthorizationFilter
    {
        private const string Realm = "Hangfire Dashboard";

        private readonly IOptionsMonitor<DashboardAuthOptions> _options;
        private readonly ILogger<BasicAuthDashboardFilter> _logger;

        public BasicAuthDashboardFilter(
            IOptionsMonitor<DashboardAuthOptions> options,
            ILogger<BasicAuthDashboardFilter> logger)
        {
            _options = options;
            _logger = logger;
        }

        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();
            var options = _options.CurrentValue;

            // Fail closed: without credentials in configuration nobody gets in.
            if (!options.IsConfigured)
            {
                _logger.LogWarning(
                    "Hangfire dashboard request denied: DashboardAuth:Username / DashboardAuth:Password are not configured.");
                return Challenge(httpContext);
            }

            var header = httpContext.Request.Headers["Authorization"].ToString();
            if (string.IsNullOrWhiteSpace(header))
            {
                return Challenge(httpContext);
            }

            if (!AuthenticationHeaderValue.TryParse(header, out var parsedHeader) ||
                !string.Equals(parsedHeader.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(parsedHeader.Parameter))
            {
                return Challenge(httpContext);
            }

            string decoded;
            try
            {
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parsedHeader.Parameter));
            }
            catch (FormatException)
            {
                return Challenge(httpContext);
            }

            // "username:password" - the password itself may contain ':', the username may not.
            var separatorIndex = decoded.IndexOf(':');
            if (separatorIndex < 0)
            {
                return Challenge(httpContext);
            }

            var suppliedUsername = decoded.Substring(0, separatorIndex);
            var suppliedPassword = decoded.Substring(separatorIndex + 1);

            // Both checks always run - no short-circuit - so a wrong username costs the same as a wrong password.
            var usernameMatches = FixedTimeEquals(suppliedUsername, options.Username);
            var passwordMatches = FixedTimeEquals(suppliedPassword, options.Password);

            if (usernameMatches && passwordMatches)
            {
                return true;
            }

            _logger.LogWarning(
                "Hangfire dashboard sign-in failed for user '{Username}' from {RemoteIpAddress}.",
                suppliedUsername,
                httpContext.Connection.RemoteIpAddress);

            return Challenge(httpContext);
        }

        /// <summary>
        /// Sends back a 401 with the Basic challenge. Always returns false so the caller can
        /// "return Challenge(httpContext);" in one line.
        /// </summary>
        private static bool Challenge(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            httpContext.Response.Headers["WWW-Authenticate"] = $"Basic realm=\"{Realm}\", charset=\"UTF-8\"";
            return false;
        }

        private static bool FixedTimeEquals(string? left, string? right)
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(left ?? string.Empty),
                Encoding.UTF8.GetBytes(right ?? string.Empty));
        }
    }
}
