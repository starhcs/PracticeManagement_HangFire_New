using System.Security.Cryptography;
using System.Text;

namespace HangfireNew.Security
{
    /// <summary>
    /// Credentials and session settings for the Hangfire dashboard login.
    /// Bound to the "DashboardAuth" configuration section.
    ///
    /// The real values must NEVER be committed. In staging and production they come from
    /// the app's environment / Key Vault (or App Configuration) reference, i.e.
    ///     DashboardAuth__Username
    ///     DashboardAuth__Password
    /// appsettings.json only carries empty placeholders so the shape of the section is
    /// visible; appsettings.Development.json carries a throwaway local-only password.
    /// </summary>
    public class DashboardAuthOptions
    {
        public const string SectionName = "DashboardAuth";

        public string? Username { get; set; }

        public string? Password { get; set; }

        /// <summary>
        /// How long a signed-in session may sit idle before the user has to log in again.
        /// The sign-in cookie is a browser-session cookie as well, so closing the browser
        /// also ends the session. Defaults to 20 minutes.
        /// </summary>
        public int SessionIdleTimeoutMinutes { get; set; } = 20;

        /// <summary>
        /// False when either credential is missing or blank. Login is refused in that case,
        /// so an unconfigured deployment can never expose the dashboard.
        /// </summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

        public TimeSpan SessionIdleTimeout =>
            TimeSpan.FromMinutes(SessionIdleTimeoutMinutes > 0 ? SessionIdleTimeoutMinutes : 20);

        /// <summary>
        /// Checks a submitted username/password pair. Both comparisons always run - no
        /// short-circuit - and both use <see cref="CryptographicOperations.FixedTimeEquals"/>
        /// so response time does not leak how much of the credential was correct.
        /// </summary>
        public bool CredentialsMatch(string? username, string? password)
        {
            if (!IsConfigured)
            {
                return false;
            }

            var usernameMatches = FixedTimeEquals(username, Username);
            var passwordMatches = FixedTimeEquals(password, Password);

            return usernameMatches && passwordMatches;
        }

        private static bool FixedTimeEquals(string? left, string? right)
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(left ?? string.Empty),
                Encoding.UTF8.GetBytes(right ?? string.Empty));
        }
    }
}
