using Hangfire.Dashboard;

namespace HangfireNew.Security
{
    /// <summary>
    /// Last line of defence in front of the Hangfire dashboard: only lets a request through
    /// when it carries a valid dashboard sign-in cookie.
    ///
    /// The redirect to the login page happens earlier, in the guard middleware added by
    /// <see cref="HangfireDashboardAuth.UseHangfireDashboardLogin"/>. This filter exists so the
    /// dashboard is still closed even if that middleware is ever removed or reordered.
    /// </summary>
    public class DashboardAuthorizationFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context)
        {
            var httpContext = context.GetHttpContext();
            return httpContext.User?.Identity?.IsAuthenticated == true;
        }
    }
}
