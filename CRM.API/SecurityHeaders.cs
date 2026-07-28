namespace CRM.API;

/// <summary>
/// Baseline response security headers (#7). The API only ever returns JSON, so it can be
/// locked down harder than a page-serving app: nothing should embed it in a frame, sniff its
/// content type, load a script from it, or use it as a referrer source.
/// </summary>
public static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, IWebHostEnvironment env) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            // Never let a browser second-guess our declared content types.
            headers["X-Content-Type-Options"] = "nosniff";

            // Defence in depth against clickjacking. frame-ancestors in the CSP below is the
            // modern equivalent; this covers browsers that only honour the legacy header.
            headers["X-Frame-Options"] = "DENY";

            // API URLs can carry record ids — don't leak them to third parties.
            headers["Referrer-Policy"] = "no-referrer";

            // No part of this API needs device access.
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";

            // A JSON API should load nothing and be embedded by nobody. Skipped in
            // Development because Swagger UI is served from this origin and needs its own
            // scripts and styles; it is not mapped outside Development.
            if (!env.IsDevelopment())
                headers["Content-Security-Policy"] =
                    "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

            await next();
        });
}
