using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace HangfireNew.Security
{
    /// <summary>
    /// Login for the Hangfire dashboard.
    ///
    /// Why a login form and not HTTP Basic auth: with Basic auth the browser caches the
    /// credentials and replays them on every later request, so once you have signed in you are
    /// never asked again until the whole browser is closed - and there is no way for the server
    /// to make it forget. Here the sign-in is a short-lived, non-persistent cookie instead:
    ///
    ///   * the browser never stores the username or password, so nothing is "remembered";
    ///   * the cookie is a session cookie - closing the browser ends the session;
    ///   * the cookie expires after <see cref="DashboardAuthOptions.SessionIdleTimeoutMinutes"/>
    ///     of inactivity, after which the login form comes back;
    ///   * "Sign out" ends the session immediately.
    /// </summary>
    public static class HangfireDashboardAuth
    {
        public const string Scheme = "HangfireDashboard";
        public const string CookieName = "hangfire_dashboard_session";

        public const string DashboardPath = "/hangfire";

        // Deliberately siblings of /hangfire rather than children of it: anything under
        // /hangfire is claimed by Hangfire's own dashboard middleware.
        public const string LoginPath = "/hangfire-login";
        public const string LogoutPath = "/hangfire-logout";

        /// <summary>
        /// Binds <see cref="DashboardAuthOptions"/> and registers the cookie scheme that holds
        /// the dashboard sign-in. Call from ConfigureServices, before builder.Build().
        /// </summary>
        public static IServiceCollection AddHangfireDashboardLogin(
            this IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            services.Configure<DashboardAuthOptions>(configuration.GetSection(DashboardAuthOptions.SectionName));
            services.AddAntiforgery();

            var authOptions = new DashboardAuthOptions();
            configuration.GetSection(DashboardAuthOptions.SectionName).Bind(authOptions);

            services
                .AddAuthentication(Scheme)
                .AddCookie(Scheme, options =>
                {
                    options.Cookie.Name = CookieName;
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SameSite = SameSiteMode.Lax;

                    // The app redirects to HTTPS, so require a secure cookie everywhere except
                    // local development, where IIS Express serves plain HTTP.
                    options.Cookie.SecurePolicy = environment.IsDevelopment()
                        ? CookieSecurePolicy.SameAsRequest
                        : CookieSecurePolicy.Always;

                    // No MaxAge / Expires => a browser-session cookie. Nothing is written to disk,
                    // so closing the browser signs the user out.
                    options.Cookie.MaxAge = null;
                    options.ExpireTimeSpan = authOptions.SessionIdleTimeout;
                    options.SlidingExpiration = true;

                    options.LoginPath = LoginPath;
                    options.LogoutPath = LogoutPath;
                    options.AccessDeniedPath = LoginPath;
                });

            return services;
        }

        /// <summary>
        /// Adds the login/logout endpoints and the guard that sends anonymous visitors of
        /// /hangfire to the login form. Call immediately BEFORE UseHangfireDashboard.
        /// </summary>
        public static WebApplication UseHangfireDashboardLogin(this WebApplication app)
        {
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("HangfireDashboardAuth");

            // "Sign out" in the dashboard's own navigation bar.
            Hangfire.Dashboard.NavigationMenu.Items.Add(_ =>
                new Hangfire.Dashboard.MenuItem("Sign out", LogoutPath));

            // --- guard: anything under /hangfire needs a valid session ---
            // StartsWithSegments is segment-aware, so /hangfire-login and /hangfire-logout
            // are not matched here.
            app.Use(async (context, next) =>
            {
                if (!context.Request.Path.StartsWithSegments(DashboardPath, StringComparison.OrdinalIgnoreCase))
                {
                    await next();
                    return;
                }

                var result = await context.AuthenticateAsync(Scheme);
                if (!result.Succeeded || result.Principal?.Identity?.IsAuthenticated != true)
                {
                    var returnUrl = context.Request.Path + context.Request.QueryString;
                    context.Response.Redirect($"{LoginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}");
                    return;
                }

                context.User = result.Principal;
                await next();
            });

            // --- GET login: show the form ---
            app.MapGet(LoginPath, async (HttpContext context, IAntiforgery antiforgery) =>
            {
                var tokens = antiforgery.GetAndStoreTokens(context);
                await WriteLoginPage(context, tokens, GetSafeReturnUrl(context.Request.Query["returnUrl"]), error: null);
            });

            // --- POST login: check the credentials, issue the session cookie ---
            app.MapPost(LoginPath, async (
                HttpContext context,
                IAntiforgery antiforgery,
                IOptionsMonitor<DashboardAuthOptions> optionsMonitor) =>
            {
                var options = optionsMonitor.CurrentValue;
                var returnUrl = GetSafeReturnUrl(context.Request.Query["returnUrl"]);

                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    var freshTokens = antiforgery.GetAndStoreTokens(context);
                    await WriteLoginPage(context, freshTokens, returnUrl,
                        "Your session expired before the form was submitted. Please try again.");
                    return;
                }

                var form = await context.Request.ReadFormAsync();
                var username = form["username"].ToString();
                var password = form["password"].ToString();

                if (!options.IsConfigured)
                {
                    logger.LogWarning(
                        "Hangfire dashboard login refused: DashboardAuth:Username / DashboardAuth:Password are not configured.");
                }

                if (!options.CredentialsMatch(username, password))
                {
                    logger.LogWarning(
                        "Hangfire dashboard login failed for user '{Username}' from {RemoteIpAddress}.",
                        username,
                        context.Connection.RemoteIpAddress);

                    // Small, constant delay to take the speed out of guessing.
                    await Task.Delay(750);

                    var tokens = antiforgery.GetAndStoreTokens(context);
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await WriteLoginPage(context, tokens, returnUrl, "Incorrect username or password.");
                    return;
                }

                var identity = new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, options.Username!) },
                    Scheme);

                await context.SignInAsync(
                    Scheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties
                    {
                        // Explicitly NOT persistent: no "remember me", nothing kept after the
                        // browser closes.
                        IsPersistent = false,
                        AllowRefresh = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.Add(options.SessionIdleTimeout)
                    });

                logger.LogInformation(
                    "Hangfire dashboard login succeeded for user '{Username}' from {RemoteIpAddress}.",
                    username,
                    context.Connection.RemoteIpAddress);

                context.Response.Redirect(returnUrl);
            });

            // --- sign out ---
            app.MapGet(LogoutPath, async (HttpContext context) =>
            {
                await context.SignOutAsync(Scheme);
                context.Response.Redirect(LoginPath);
            });

            app.MapPost(LogoutPath, async (HttpContext context) =>
            {
                await context.SignOutAsync(Scheme);
                context.Response.Redirect(LoginPath);
            });

            return app;
        }

        /// <summary>
        /// Only ever redirect back to a path inside this app - never to an absolute or
        /// protocol-relative URL supplied by the caller.
        /// </summary>
        private static string GetSafeReturnUrl(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) ||
                !candidate.StartsWith("/", StringComparison.Ordinal) ||
                candidate.StartsWith("//", StringComparison.Ordinal) ||
                candidate.StartsWith("/\\", StringComparison.Ordinal) ||
                !candidate.StartsWith(DashboardPath, StringComparison.OrdinalIgnoreCase))
            {
                return DashboardPath;
            }

            return candidate;
        }

        private static async Task WriteLoginPage(
            HttpContext context,
            AntiforgeryTokenSet tokens,
            string returnUrl,
            string? error)
        {
            // Never let the login page (or its antiforgery token) sit in a cache.
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            context.Response.Headers["Pragma"] = "no-cache";
            context.Response.ContentType = "text/html; charset=utf-8";

            var errorHtml = string.IsNullOrEmpty(error)
                ? string.Empty
                : $"<p class=\"error\">{WebUtility.HtmlEncode(error)}</p>";

            var action = $"{LoginPath}?returnUrl={Uri.EscapeDataString(returnUrl)}";

            var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Sign in - Hangfire Dashboard</title>
<style>
  :root {{ color-scheme: light dark; }}
  body {{ margin:0; min-height:100vh; display:flex; align-items:center; justify-content:center;
         font-family: -apple-system, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
         background:#f4f5f7; color:#1f2328; }}
  .card {{ background:#fff; padding:32px; border-radius:10px; width:100%; max-width:360px;
          box-shadow:0 1px 3px rgba(0,0,0,.12), 0 8px 24px rgba(0,0,0,.08); }}
  h1 {{ font-size:18px; margin:0 0 4px; }}
  p.sub {{ margin:0 0 24px; font-size:13px; color:#6b7280; }}
  label {{ display:block; font-size:13px; font-weight:600; margin-bottom:6px; }}
  input {{ width:100%; box-sizing:border-box; padding:9px 11px; font-size:14px;
           border:1px solid #d0d5dd; border-radius:6px; margin-bottom:16px; background:#fff; color:inherit; }}
  input:focus {{ outline:2px solid #2563eb; outline-offset:-1px; border-color:#2563eb; }}
  button {{ width:100%; padding:10px; font-size:14px; font-weight:600; color:#fff;
            background:#2563eb; border:0; border-radius:6px; cursor:pointer; }}
  button:hover {{ background:#1d4ed8; }}
  .error {{ background:#fef2f2; color:#b42318; border:1px solid #fecaca; border-radius:6px;
            padding:9px 11px; font-size:13px; margin:0 0 16px; }}
  @media (prefers-color-scheme: dark) {{
    body {{ background:#111418; color:#e6e8eb; }}
    .card {{ background:#1a1e23; box-shadow:none; border:1px solid #2a2f36; }}
    input {{ background:#111418; border-color:#343a42; }}
    p.sub {{ color:#9aa3ad; }}
    .error {{ background:#2a1416; border-color:#5b2326; color:#f6b5b2; }}
  }}
</style>
</head>
<body>
  <form class=""card"" method=""post"" action=""{WebUtility.HtmlEncode(action)}"" autocomplete=""off"">
    <h1>Hangfire Dashboard</h1>
    <p class=""sub"">Sign in to view background jobs.</p>
    {errorHtml}
    <label for=""username"">Username</label>
    <input id=""username"" name=""username"" type=""text"" autocomplete=""off"" autofocus required>
    <label for=""password"">Password</label>
    <input id=""password"" name=""password"" type=""password"" autocomplete=""new-password"" required>
    <input type=""hidden"" name=""{tokens.FormFieldName}"" value=""{tokens.RequestToken}"">
    <button type=""submit"">Sign in</button>
  </form>
</body>
</html>";

            await context.Response.WriteAsync(html);
        }
    }
}
