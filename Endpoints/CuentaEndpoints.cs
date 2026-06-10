using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

namespace AnimeRecommender.Endpoints;

public static class CuentaEndpoints
{
    // Sesión con Google (OAuth en el servidor, cookie propia): el navegador nunca ve
    // tokens. Si no hay credenciales configuradas (conGoogle=false), /api/me lo dice
    // y el frontend simplemente no ofrece el login.
    public static void MapCuenta(this IEndpointRouteBuilder app, bool conGoogle)
    {
        // GET /api/me — quién soy, o si el login está siquiera disponible.
        app.MapGet("/api/me", (HttpContext http) =>
        {
            var u = http.User;
            return u.Identity?.IsAuthenticated == true
                ? Results.Ok(new
                {
                    autenticado = true,
                    disponible = conGoogle,
                    nombre = u.FindFirstValue(ClaimTypes.Name) ?? "",
                    foto = u.FindFirstValue("picture"),
                })
                : Results.Ok(new { autenticado = false, disponible = conGoogle });
        });

        if (!conGoogle) return;

        // GET /login — manda a Google y vuelve a la portada con la cookie de sesión puesta.
        app.MapGet("/login", () => Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/" },
            [GoogleDefaults.AuthenticationScheme]));

        // POST /api/logout — borra la cookie de sesión (POST: SameSite=Lax evita el CSRF).
        app.MapPost("/api/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });
    }
}
